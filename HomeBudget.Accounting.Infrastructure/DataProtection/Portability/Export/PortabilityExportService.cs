using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Export
{
    public sealed partial class PortabilityExportService
    {
        private readonly IPortabilityExportReader _reader;
        private readonly PortabilityExportOptions _options;
        private readonly PortabilityContractCatalog _catalog;

        public PortabilityExportService(
            IPortabilityExportReader reader,
            PortabilityExportOptions options)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _options.Validate();
            _catalog = PortabilityContractCatalog.Load();
        }

        public async Task<PortabilityExportResult> ExportAsync(
            PortabilityExportRequest request,
            CancellationToken cancellationToken)
        {
            ValidateRequest(request);
            var cutoff = await AcquireSettledCutoffAsync(request, cancellationToken).ConfigureAwait(false);
            using var staging = new PortabilityExportStaging(request.DestinationPath);
            using var summary = new PortabilityExportSummary();
            var validator = new PortabilityExportContractValidator(_catalog);
            var writer = new PortabilityExportFileWriter(_options, staging, validator);
            var artifacts = new List<PortabilityExportArtifact>();

            try
            {
                await WriteContractFilesAsync(writer, artifacts, cancellationToken).ConfigureAwait(false);
                if (request.Completeness == "partial")
                {
                    await WritePartialBusinessStateAsync(
                        request,
                        cutoff,
                        writer,
                        artifacts,
                        summary,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await WriteFullBusinessStateAsync(
                        request,
                        cutoff,
                        writer,
                        artifacts,
                        summary,
                        cancellationToken).ConfigureAwait(false);
                }

                summary.Complete();
                await WriteReconciliationAsync(
                    request,
                    cutoff,
                    writer,
                    artifacts,
                    summary,
                    cancellationToken).ConfigureAwait(false);
                artifacts.AddRange(await new PortabilityExportCsv(writer, staging)
                    .WriteAsync(cancellationToken).ConfigureAwait(false));
                var manifest = PortabilityExportJson.Manifest(request, cutoff, artifacts);
                await writer.WriteJsonAsync(
                    "manifest.json",
                    "schemas/manifest.schema.json",
                    manifest,
                    cancellationToken).ConfigureAwait(false);
                await CreateArchiveAsync(staging, cancellationToken).ConfigureAwait(false);
                staging.Publish();
                return new PortabilityExportResult(
                    request.ExportId,
                    request.DestinationPath,
                    cutoff.ActualCutoffUtc,
                    summary.Operations);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PortabilityExportValidationException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
            {
                throw new PortabilityExportValidationException("export.source-read-failed", exception);
            }
        }

        private async Task<PortabilityExportCutoff> AcquireSettledCutoffAsync(
            PortabilityExportRequest request,
            CancellationToken cancellationToken)
        {
            var cutoff = await _reader.AcquireCutoffAsync(request, cancellationToken).ConfigureAwait(false);
            ValidateCutoff(request, cutoff);
            if (cutoff.ProjectionWatermarkSequence >= cutoff.CommonCutoffSequence)
            {
                return cutoff;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.ProjectionCatchupTimeout);
            bool settled;
            try
            {
                settled = await _reader.WaitForProjectionAsync(cutoff, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                settled = false;
            }

            if (!settled)
            {
                throw new PortabilityExportValidationException("export.projection-lag");
            }

            return cutoff with
            {
                ProjectionWatermarkSequence = cutoff.CommonCutoffSequence,
                ProjectionWatermarkUtc = cutoff.ActualCutoffUtc
            };
        }

        private static void ValidateRequest(PortabilityExportRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.ExportId == Guid.Empty
                || string.IsNullOrWhiteSpace(request.EnvironmentId)
                || string.IsNullOrWhiteSpace(request.LedgerId)
                || string.IsNullOrWhiteSpace(request.LedgerGenerationId)
                || string.IsNullOrWhiteSpace(request.DestinationPath)
                || request.Completeness is not "full" and not "partial"
                || (request.Completeness == "partial" && request.SelectedOperationIds.Count == 0)
                || (request.Completeness == "full" && request.SelectedOperationIds.Count != 0))
            {
                throw new PortabilityExportValidationException("export.request-invalid");
            }
        }

        private static void ValidateCutoff(PortabilityExportRequest request, PortabilityExportCutoff cutoff)
        {
            if (cutoff == null
                || cutoff.ActualCutoffUtc > request.RequestedCutoffUtc
                || cutoff.CommonCutoffSequence < 0
                || cutoff.SourceGenerationId != request.LedgerGenerationId
                || cutoff.SourceGenerationNumber != request.LedgerGenerationNumber)
            {
                throw new PortabilityExportValidationException("export.cutoff-invalid");
            }
        }

        private static async Task CreateArchiveAsync(
            PortabilityExportStaging staging,
            CancellationToken cancellationToken)
        {
            await using var output = new FileStream(
                staging.ArchivePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var archive = new ZipArchive(output, ZipArchiveMode.Create, true);
            foreach (var path in Directory.EnumerateFiles(staging.ContentPath, "*", SearchOption.AllDirectories)
                .OrderBy(value => value, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(staging.ContentPath, path).Replace('\\', '/');
                var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                await using var input = File.OpenRead(path);
                await using var destination = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
                await input.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

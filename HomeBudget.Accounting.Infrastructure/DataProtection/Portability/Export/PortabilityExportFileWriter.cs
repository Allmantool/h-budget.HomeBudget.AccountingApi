using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Export
{
    internal sealed class PortabilityExportFileWriter
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };
        private readonly PortabilityExportOptions _options;
        private readonly PortabilityExportStaging _staging;
        private readonly PortabilityExportContractValidator _validator;

        public PortabilityExportFileWriter(
            PortabilityExportOptions options,
            PortabilityExportStaging staging,
            PortabilityExportContractValidator validator)
        {
            _options = options;
            _staging = staging;
            _validator = validator;
        }

        public async Task<PortabilityExportArtifact> WriteJsonLinesAsync(
            string path,
            string schema,
            IAsyncEnumerable<JsonElement> records,
            Func<JsonElement, bool> include,
            Func<JsonElement, JsonElement> transform,
            Action<JsonElement> observe,
            CancellationToken cancellationToken)
        {
            var filePath = _staging.GetContentPath(path);
            long count = 0;
            await using (var output = OpenFile(filePath))
            {
                await foreach (var source in records.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    if (!include(source))
                    {
                        continue;
                    }

                    count++;
                    if (count > _options.MaxExportRecords)
                    {
                        throw new PortabilityExportValidationException("export.record-count-limit");
                    }

                    var record = transform(source);
                    _validator.Validate(schema, record, path);
                    observe(record);
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
                    await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await output.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            return CreateArtifact(path, "application/x-ndjson", schema, count, filePath);
        }

        public async Task<PortabilityExportArtifact> WriteJsonAsync(
            string path,
            string schema,
            JsonElement value,
            CancellationToken cancellationToken)
        {
            _validator.Validate(schema, value, path);
            var filePath = _staging.GetContentPath(path);
            await using (var output = OpenFile(filePath))
            {
                await JsonSerializer.SerializeAsync(output, value, JsonOptions, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            return CreateArtifact(path, "application/json", schema, null, filePath);
        }

        public async Task<PortabilityExportArtifact> WriteBytesAsync(
            string path,
            string mediaType,
            byte[] bytes,
            CancellationToken cancellationToken)
        {
            var filePath = _staging.GetContentPath(path);
            await File.WriteAllBytesAsync(filePath, bytes, cancellationToken).ConfigureAwait(false);
            return CreateArtifact(path, mediaType, null, null, filePath);
        }

        public async Task<PortabilityExportArtifact> WriteCsvAsync(
            string path,
            IReadOnlyList<string> headers,
            IEnumerable<IReadOnlyList<string>> rows,
            CancellationToken cancellationToken)
        {
            var filePath = _staging.GetContentPath(path);
            await using (var output = OpenFile(filePath))
            await using (var writer = new StreamWriter(output, new UTF8Encoding(false), _options.BufferSizeBytes, true))
            {
                await writer.WriteLineAsync(string.Join(',', headers)).ConfigureAwait(false);
                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(string.Join(',', EscapeRow(row))).ConfigureAwait(false);
                }

                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            return CreateArtifact(path, "text/csv", null, null, filePath);
        }

        private static IEnumerable<string> EscapeRow(IEnumerable<string> row)
        {
            foreach (var value in row)
            {
                var safe = value ?? string.Empty;
                if (safe.Length > 0 && safe[0] is '=' or '+' or '-' or '@')
                {
                    safe = $"'{safe}";
                }

                yield return $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
            }
        }

        private FileStream OpenFile(string path) => new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            _options.BufferSizeBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        private static PortabilityExportArtifact CreateArtifact(
            string path,
            string mediaType,
            string schema,
            long? recordCount,
            string filePath)
        {
            using var stream = File.OpenRead(filePath);
            var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            return new PortabilityExportArtifact(
                path,
                mediaType,
                schema,
                recordCount,
                stream.Length,
                hash);
        }
    }
}

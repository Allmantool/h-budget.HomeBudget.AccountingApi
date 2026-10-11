using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb
{
    public sealed partial class EventStoreDbProtectionProvider
    {
        private async Task<EventStoreDbCapturePlan> PlanCaptureCoreAsync(
            EventStoreDbCaptureKind kind,
            Guid planId,
            EventStoreDbBackupSet previousBackup,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (planId == Guid.Empty)
            {
                throw Failure("plan-id-invalid", "The capture plan identity is invalid.");
            }

            var initialProbe = await _probe.ProbeSourceAsync(cancellationToken).ConfigureAwait(false);
            ValidateSourceProbe(initialProbe);
            ValidatePreviousBackup(kind, previousBackup, initialProbe.Identity.NodeId);

            var inventory = await BuildSourceInventoryAsync(initialProbe, cancellationToken).ConfigureAwait(false);
            var finalProbe = await _probe.ProbeSourceAsync(cancellationToken).ConfigureAwait(false);
            ValidateStableSource(initialProbe, finalProbe);

            var steps = CreateCaptureSteps(kind, inventory, previousBackup);
            return new EventStoreDbCapturePlan(
                planId,
                kind,
                initialProbe.Identity.NodeId,
                initialProbe.ServerVersion,
                initialProbe.WriterPosition,
                initialProbe.ChaserPosition,
                _timeProvider.GetUtcNow(),
                previousBackup?.CaptureId,
                inventory,
                steps);
        }

        private EventStoreDbCopyStep[] CreateCaptureSteps(
            EventStoreDbCaptureKind kind,
            IReadOnlyCollection<EventStoreDbBackupFile> inventory,
            EventStoreDbBackupSet previousBackup)
        {
            var previous = previousBackup?.Inventory.ToDictionary(
                file => file.RelativePath,
                StringComparer.Ordinal) ?? new Dictionary<string, EventStoreDbBackupFile>(StringComparer.Ordinal);
            var steps = inventory
                .Where(file => kind == EventStoreDbCaptureKind.FullFileCopy || IsChanged(file, previous))
                .Select(file => new EventStoreDbCopyStep(
                    GetCopyOrder(file.Role),
                    EventStoreDbCopyAction.Copy,
                    ResolveSourcePath(file.RelativePath),
                    file.RelativePath,
                    file.Sha256,
                    file.Dependencies))
                .ToList();

            if (kind == EventStoreDbCaptureKind.DifferentialFileCopy)
            {
                var currentPaths = inventory.Select(file => file.RelativePath).ToHashSet(StringComparer.Ordinal);
                steps.AddRange(previous.Values
                    .Where(file => !currentPaths.Contains(file.RelativePath))
                    .Select(file => new EventStoreDbCopyStep(
                        50,
                        EventStoreDbCopyAction.RemoveExtraneous,
                        string.Empty,
                        file.RelativePath,
                        file.Sha256,
                        Array.Empty<string>())));
            }

            return steps
                .OrderBy(step => step.Order)
                .ThenBy(step => step.DestinationRelativePath, StringComparer.Ordinal)
                .ToArray();
        }

        private static bool IsChanged(
            EventStoreDbBackupFile current,
            Dictionary<string, EventStoreDbBackupFile> previous)
        {
            return !previous.TryGetValue(current.RelativePath, out var prior) ||
                prior.SizeBytes != current.SizeBytes ||
                !string.Equals(prior.Sha256, current.Sha256, StringComparison.Ordinal);
        }

        private static int GetCopyOrder(EventStoreDbFileRole role) => role switch
        {
            EventStoreDbFileRole.IndexCheckpoint => 10,
            EventStoreDbFileRole.IndexMap => 20,
            EventStoreDbFileRole.IndexTable => 21,
            EventStoreDbFileRole.IndexAuxiliary => 22,
            EventStoreDbFileRole.DatabaseCheckpoint => 30,
            EventStoreDbFileRole.Chunk => 40,
            _ => throw Failure("file-role-unsupported", "The backup file role is unsupported.")
        };

        private string ResolveSourcePath(string relativePath)
        {
            var separator = relativePath.IndexOf('/', StringComparison.Ordinal);
            var area = relativePath[..separator];
            var child = relativePath[(separator + 1)..].Replace('/', System.IO.Path.DirectorySeparatorChar);
            var root = area == "db" ? _policy.Source.DatabasePath : _policy.Source.IndexPath;
            return System.IO.Path.Combine(root, child);
        }
    }
}

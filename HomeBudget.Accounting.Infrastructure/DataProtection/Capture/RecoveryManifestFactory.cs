using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    internal static class RecoveryManifestFactory
    {
        public static RecoveryManifestDraft CreateStaging(
            CaptureRequest request,
            IReadOnlyCollection<CaptureComponentReceipt> receipts,
            PendingWorkDisposition pendingWork,
            DateTimeOffset commonCutoffUtc)
        {
            var recoverySetId = Guid.NewGuid();
            return Create(request, recoverySetId, receipts, pendingWork, commonCutoffUtc, "[]", "staging");
        }

        public static RecoveryManifestDraft Complete(
            RecoveryManifestDraft draft,
            string inventoryJson)
        {
            using var inventory = JsonDocument.Parse(inventoryJson);
            var manifest = Serialize(
                draft.RecoverySetId,
                draft.EnvironmentId,
                draft.LedgerScope,
                draft.SourceGenerationId,
                draft.CaptureStartedUtc,
                draft.CommonCutoffUtc,
                draft.Receipts,
                draft.PendingWork,
                inventory.RootElement,
                "captured");
            return draft with
            {
                ManifestJson = manifest,
                ManifestChecksum = Checksum(manifest)
            };
        }

        private static RecoveryManifestDraft Create(
            CaptureRequest request,
            Guid recoverySetId,
            IReadOnlyCollection<CaptureComponentReceipt> receipts,
            PendingWorkDisposition pendingWork,
            DateTimeOffset commonCutoffUtc,
            string inventoryJson,
            string state)
        {
            using var inventory = JsonDocument.Parse(inventoryJson);
            var ledgerScope = $"{request.Operation.LedgerId}:{request.Operation.GenerationId}";
            var manifest = Serialize(
                recoverySetId,
                request.Operation.EnvironmentId,
                ledgerScope,
                request.Operation.GenerationId,
                request.StartedUtc,
                commonCutoffUtc,
                receipts,
                pendingWork,
                inventory.RootElement,
                state);
            return new RecoveryManifestDraft(
                recoverySetId,
                request.Operation.OperationId,
                request.Operation.EnvironmentId,
                ledgerScope,
                request.Operation.GenerationId,
                request.StartedUtc,
                commonCutoffUtc,
                request.KeyReference,
                Checksum(manifest),
                manifest,
                receipts,
                pendingWork);
        }

        private static string Serialize(
            Guid recoverySetId,
            string environmentId,
            string ledgerScope,
            string sourceGenerationId,
            DateTimeOffset captureStartedUtc,
            DateTimeOffset commonCutoffUtc,
            IReadOnlyCollection<CaptureComponentReceipt> receipts,
            PendingWorkDisposition pendingWork,
            JsonElement inventory,
            string state)
        {
            var payload = new
            {
                schemaVersion = "home-ledger-recovery-set/v1",
                recoverySetId,
                environmentId,
                ledgerScope,
                sourceGenerationId,
                captureStartedUtc,
                commonCutoffUtc,
                state,
                components = receipts.OrderBy(receipt => receipt.ComponentName, StringComparer.Ordinal),
                pendingWork,
                fileInventory = inventory
            };
            return JsonSerializer.Serialize(payload);
        }

        private static string Checksum(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}

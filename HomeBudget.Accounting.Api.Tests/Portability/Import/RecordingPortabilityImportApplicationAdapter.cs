using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import;

namespace HomeBudget.Accounting.Api.Tests.Portability.Import
{
    internal sealed class RecordingPortabilityImportApplicationAdapter : IPortabilityImportApplicationAdapter
    {
        private readonly Dictionary<string, PortabilityImportRecord> _effects = new(StringComparer.Ordinal);

        public List<IReadOnlyList<PortabilityImportRecord>> Batches { get; } = [];

        public bool ReturnAcceptedBeforeSettlement { get; set; }

        public bool TimeoutAfterAcceptanceOnce { get; set; }

        public bool CrashAfterAcceptanceOnce { get; set; }

        public Action AfterFirstBatch { get; set; }

        public int EffectCount => _effects.Count;

        public async Task<IReadOnlyList<PortabilityImportRecordResult>> AcceptBatchAsync(
            IReadOnlyList<PortabilityImportRecord> records,
            PortabilityImportExecutionContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Batches.Add(records.Select(value => value with { Payload = value.Payload.Clone() }).ToArray());
            var results = new List<PortabilityImportRecordResult>(records.Count);
            foreach (var record in records)
            {
                if (_effects.TryGetValue(record.IdempotencyKey, out var prior))
                {
                    results.Add(Result(
                        record,
                        prior.Fingerprint == record.Fingerprint
                            ? PortabilityImportRecordStatuses.Projected
                            : PortabilityImportRecordStatuses.Conflict));
                    continue;
                }

                _effects.Add(record.IdempotencyKey, record);
                results.Add(Result(
                    record,
                    ReturnAcceptedBeforeSettlement
                        ? PortabilityImportRecordStatuses.Accepted
                        : PortabilityImportRecordStatuses.Projected));
            }

            if (Batches.Count == 1)
            {
                AfterFirstBatch?.Invoke();
            }

            await Task.Yield();
            if (TimeoutAfterAcceptanceOnce)
            {
                TimeoutAfterAcceptanceOnce = false;
                throw new TimeoutException("Synthetic timeout after durable acceptance.");
            }

            if (CrashAfterAcceptanceOnce)
            {
                CrashAfterAcceptanceOnce = false;
                throw new SimulatedImportProcessCrashException();
            }

            return results;
        }

        public Task<PortabilityImportRecordResult> GetStatusAsync(
            PortabilityImportRecord record,
            PortabilityImportExecutionContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_effects.TryGetValue(record.IdempotencyKey, out var existing))
            {
                return Task.FromResult(Result(record, PortabilityImportRecordStatuses.NotFound));
            }

            return Task.FromResult(Result(
                record,
                existing.Fingerprint == record.Fingerprint
                    ? PortabilityImportRecordStatuses.Projected
                    : PortabilityImportRecordStatuses.Conflict));
        }

        private static PortabilityImportRecordResult Result(
            PortabilityImportRecord record,
            string status) => new(
                record.SourceEntityId,
                record.Fingerprint,
                status,
                record.SourceEntityId,
                status == PortabilityImportRecordStatuses.Conflict ? "import.identity-conflict" : null);
    }
}

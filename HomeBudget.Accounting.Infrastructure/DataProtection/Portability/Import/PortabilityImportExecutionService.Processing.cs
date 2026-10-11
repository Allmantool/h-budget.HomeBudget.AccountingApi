using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    internal sealed partial class PortabilityImportExecutionService
    {
        private static readonly Dictionary<string, int> Stages =
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["currencies"] = 0,
                ["categories"] = 1,
                ["contractors"] = 2,
                ["settings"] = 3,
                ["accounts"] = 4,
                ["rates"] = 5,
                ["operations"] = 6,
                ["lineage"] = 7
            };

        private async Task<bool> ExecuteRecordsAsync(
            Guid operationId,
            IReadOnlyList<PortabilityImportRecord> records,
            PortabilityImportExecutionContext context,
            CancellationToken cancellationToken)
        {
            if (records == null)
            {
                throw PortabilityImportExecutionException.Create("import.records-missing");
            }

            foreach (var stage in records.GroupBy(Stage).OrderBy(value => value.Key))
            {
                var ordered = stage.OrderBy(value => value.SourceEntityId, StringComparer.Ordinal).ToArray();
                for (var offset = 0; offset < ordered.Length; offset += _options.BatchSize)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return true;
                    }

                    var batch = ordered.Skip(offset).Take(_options.BatchSize).ToArray();
                    await ExecuteBatchAsync(
                        operationId,
                        batch,
                        context,
                        cancellationToken).ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private async Task ExecuteBatchAsync(
            Guid operationId,
            IReadOnlyList<PortabilityImportRecord> batch,
            PortabilityImportExecutionContext context,
            CancellationToken cancellationToken)
        {
            var journal = await RequireJournalAsync(operationId, cancellationToken).ConfigureAwait(false);
            var outcomes = journal.Outcomes.ToDictionary(
                value => (value.EntityType, value.SourceEntityId));
            var unresolved = new List<PortabilityImportRecord>();
            foreach (var record in batch)
            {
                var outcome = outcomes[(record.EntityType, record.SourceEntityId)];
                if (outcome.State == "projected")
                {
                    continue;
                }

                if (outcome.State is "accepted" or "persisted")
                {
                    await SettleAsync(operationId, record, outcome.State, context).ConfigureAwait(false);
                }
                else if (outcome.State == "validated")
                {
                    unresolved.Add(record);
                }
                else
                {
                    throw PortabilityImportExecutionException.Create(
                        outcome.ErrorClassification ?? "import.outcome-terminal",
                        record.EntryPath,
                        record.SourceEntityId);
                }
            }

            if (unresolved.Count == 0)
            {
                return;
            }

            IReadOnlyList<PortabilityImportRecordResult> results;
            try
            {
                results = await _adapter.AcceptBatchAsync(
                    unresolved,
                    context,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                results = await ResolveUnknownBatchAsync(unresolved, context).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                results = await ResolveUnknownBatchAsync(unresolved, context).ConfigureAwait(false);
            }

            ValidateResults(unresolved, results);
            foreach (var record in unresolved)
            {
                var result = results.Single(value => value.SourceEntityId == record.SourceEntityId);
                await ApplyResultAsync(operationId, record, "validated", result, context).ConfigureAwait(false);
            }
        }

        private async Task<IReadOnlyList<PortabilityImportRecordResult>> ResolveUnknownBatchAsync(
            IReadOnlyList<PortabilityImportRecord> records,
            PortabilityImportExecutionContext context)
        {
            using var timeout = new CancellationTokenSource(_options.SettlementTimeout);
            var results = new List<PortabilityImportRecordResult>(records.Count);
            foreach (var record in records)
            {
                var result = await _adapter.GetStatusAsync(record, context, timeout.Token).ConfigureAwait(false);
                ValidateResult(record, result);
                if (result.Status == PortabilityImportRecordStatuses.NotFound)
                {
                    throw PortabilityImportExecutionException.Create(
                        "import.acceptance-unknown",
                        record.EntryPath,
                        record.SourceEntityId);
                }

                results.Add(result);
            }

            return results;
        }

        private async Task ApplyResultAsync(
            Guid operationId,
            PortabilityImportRecord record,
            string expectedState,
            PortabilityImportRecordResult result,
            PortabilityImportExecutionContext context)
        {
            ValidateResult(record, result);
            var nextState = ToOutcomeState(result.Status);
            await TransitionAsync(
                operationId,
                record,
                expectedState,
                nextState,
                result.ErrorCode).ConfigureAwait(false);
            if (nextState is "accepted" or "persisted")
            {
                await SettleAsync(operationId, record, nextState, context).ConfigureAwait(false);
            }
            else if (nextState != "projected")
            {
                throw PortabilityImportExecutionException.Create(
                    result.ErrorCode ?? "import.record-rejected",
                    record.EntryPath,
                    record.SourceEntityId);
            }
        }

        private async Task SettleAsync(
            Guid operationId,
            PortabilityImportRecord record,
            string expectedState,
            PortabilityImportExecutionContext context)
        {
            using var timeout = new CancellationTokenSource(_options.SettlementTimeout);
            var currentState = expectedState;
            try
            {
                while (currentState is "accepted" or "persisted")
                {
                    var result = await _adapter.GetStatusAsync(
                        record,
                        context,
                        timeout.Token).ConfigureAwait(false);
                    ValidateResult(record, result);
                    if (result.Status == PortabilityImportRecordStatuses.NotFound)
                    {
                        throw PortabilityImportExecutionException.Create(
                            "import.acceptance-unknown",
                            record.EntryPath,
                            record.SourceEntityId);
                    }

                    var nextState = ToOutcomeState(result.Status);
                    if (nextState == currentState)
                    {
                        await Task.Delay(_options.SettlementPollInterval, timeout.Token).ConfigureAwait(false);
                        continue;
                    }

                    await TransitionAsync(
                        operationId,
                        record,
                        currentState,
                        nextState,
                        result.ErrorCode).ConfigureAwait(false);
                    currentState = nextState;
                }
            }
            catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
            {
                throw PortabilityImportExecutionException.Create(
                    "import.settlement-timeout",
                    record.EntryPath,
                    record.SourceEntityId,
                    exception);
            }

            if (currentState != "projected")
            {
                throw PortabilityImportExecutionException.Create(
                    "import.record-rejected",
                    record.EntryPath,
                    record.SourceEntityId);
            }
        }

        private async Task TransitionAsync(
            Guid operationId,
            PortabilityImportRecord record,
            string expectedState,
            string nextState,
            string errorCode)
        {
            using var timeout = new CancellationTokenSource(_options.SettlementTimeout);
            await _journal.TryTransitionOutcomeAsync(
                new PortabilityImportOutcomeTransition(
                    operationId,
                    record.EntityType,
                    record.SourceEntityId,
                    record.Fingerprint,
                    record.IdempotencyKey,
                    expectedState,
                    nextState,
                    errorCode,
                    DateTime.UtcNow),
                timeout.Token).ConfigureAwait(false);
        }

        private static void ValidateResults(
            IReadOnlyList<PortabilityImportRecord> records,
            IReadOnlyList<PortabilityImportRecordResult> results)
        {
            if (results == null || results.Count != records.Count)
            {
                throw PortabilityImportExecutionException.Create("import.adapter-result-invalid");
            }

            var expected = records.ToDictionary(value => value.SourceEntityId, StringComparer.Ordinal);
            foreach (var result in results)
            {
                if (!expected.TryGetValue(result.SourceEntityId, out var record)
                    || result.Fingerprint != record.Fingerprint)
                {
                    throw PortabilityImportExecutionException.Create("import.adapter-result-invalid");
                }
            }
        }

        private static int Stage(PortabilityImportRecord record) =>
            Stages.TryGetValue(record.EntityType, out var stage)
                ? stage
                : throw PortabilityImportExecutionException.Create(
                    "import.entity-type-unsupported",
                    record.EntryPath,
                    record.SourceEntityId);

        private static string ToOutcomeState(string status) => status switch
        {
            PortabilityImportRecordStatuses.Accepted => "accepted",
            PortabilityImportRecordStatuses.Persisted => "persisted",
            PortabilityImportRecordStatuses.Projected => "projected",
            PortabilityImportRecordStatuses.Conflict => "conflict",
            PortabilityImportRecordStatuses.Failed => "failed",
            _ => throw PortabilityImportExecutionException.Create("import.adapter-result-invalid")
        };

        private static void ValidateResult(
            PortabilityImportRecord record,
            PortabilityImportRecordResult result)
        {
            if (result == null
                || result.SourceEntityId != record.SourceEntityId
                || result.Fingerprint != record.Fingerprint)
            {
                throw PortabilityImportExecutionException.Create("import.adapter-result-invalid");
            }
        }
    }
}

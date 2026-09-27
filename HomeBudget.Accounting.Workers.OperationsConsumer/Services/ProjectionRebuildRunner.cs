using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

using HomeBudget.Accounting.Domain;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Workers.OperationsConsumer.Clients;
using HomeBudget.Accounting.Workers.OperationsConsumer.Configuration;
using HomeBudget.Accounting.Workers.OperationsConsumer.Models;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Services
{
    internal sealed class ProjectionRebuildRunner(
        IPaymentsHistoryDocumentsClient historyClient,
        IEventStoreDbStreamReadClient<PaymentOperationEvent> eventStore,
        ISender sender,
        IOptions<ProjectionRebuildOptions> rebuildOptions,
        IOptions<MongoDbOptions> mongoOptions,
        IOptions<EventStoreDbOptions> eventStoreOptions,
        ILogger<ProjectionRebuildRunner> logger)
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        private readonly ProjectionRebuildOptions _options = rebuildOptions.Value;
        private readonly string _mongoEndpoint = new MongoUrl(mongoOptions.Value.ConnectionString).Server.ToString();
        private readonly string _mongoDatabase = mongoOptions.Value.PaymentsHistory;
        private readonly string _eventStoreEndpoint = eventStoreOptions.Value.Url.Authority;

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            ValidateBaseOptions();
            if (_options.Execute)
            {
                await ExecuteAsync(cancellationToken);
                return;
            }

            await CreateDryRunPlanAsync(cancellationToken);
        }

        private async Task CreateDryRunPlanAsync(CancellationToken cancellationToken)
        {
            var scopes = await historyClient.DiscoverProjectionScopesAsync(cancellationToken);
            EnsureBoundedScope(scopes.Count);
            var items = new List<ProjectionRebuildPlanItem>(scopes.Count);
            foreach (var scope in scopes)
            {
                var events = await ReadCompleteStreamAsync(scope.StreamId, requiredRevision: null, cancellationToken);
                var item = new ProjectionRebuildPlanItem(
                    scope.PaymentAccountId,
                    scope.FinancialPeriodIdentifier,
                    scope.StreamId,
                    events[^1].SequenceNumber,
                    events.Count,
                    scope.PublishedRevision);
                ValidateScope(item, events);
                items.Add(item);
            }

            var plan = new ProjectionRebuildPlan(
                _options.ExpectedTarget,
                _mongoEndpoint,
                _mongoDatabase,
                _eventStoreEndpoint,
                DateTime.UtcNow,
                items);
            var outputPath = Path.GetFullPath(_options.PlanOutputFile);
            await using (var output = new FileStream(
                outputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(output, plan, JsonOptions, cancellationToken);
            }

            logger.LogInformation(
                "Projection rebuild dry-run wrote {ScopeCount} immutable scopes to {PlanPath}; no projection was changed.",
                items.Count,
                outputPath);
        }

        private async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            if (!_options.OldWritersStopped)
            {
                throw new InvalidOperationException(
                    "Projection rebuild execution requires explicit confirmation that old projection writers are stopped.");
            }

            if (!string.Equals(_options.ExpectedTarget, _options.ConfirmTarget, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Projection rebuild target confirmation does not match the expected target.");
            }

            if (string.IsNullOrWhiteSpace(_options.ScopeFile))
            {
                throw new InvalidOperationException("Projection rebuild execution requires an immutable dry-run scope file.");
            }

            var plan = JsonSerializer.Deserialize<ProjectionRebuildPlan>(
                await File.ReadAllTextAsync(Path.GetFullPath(_options.ScopeFile), cancellationToken),
                JsonOptions) ?? throw new InvalidOperationException("Projection rebuild scope file is empty or invalid.");
            ValidatePlanTarget(plan);
            EnsureBoundedScope(plan.Items.Count);
            foreach (var item in plan.Items)
            {
                if (item.PublishedRevision > item.ExpectedRevision)
                {
                    throw new InvalidOperationException(
                        $"Projection head for scope '{item.StreamId}' is ahead of its authoritative EventStore revision.");
                }

                var events = await ReadCompleteStreamAsync(
                    item.StreamId,
                    item.ExpectedRevision,
                    cancellationToken);
                if (events.Count != item.ExpectedEventCount)
                {
                    throw new InvalidOperationException(
                        $"Projection rebuild scope '{item.StreamId}' changed after dry-run planning.");
                }

                ValidateScope(item, events);

                var result = await sender.Send(
                    new SyncOperationsHistoryCommand(
                        item.PaymentAccountId,
                        events,
                        new ProjectionCheckpoint
                        {
                            StreamId = item.StreamId,
                            Revision = item.ExpectedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        }),
                    cancellationToken);
                if (result?.IsSucceeded != true)
                {
                    throw new InvalidOperationException(
                        $"Projection rebuild failed for scope '{item.StreamId}'.");
                }
            }

            await ValidatePublishedScopesAsync(plan.Items, cancellationToken);

            logger.LogInformation(
                "Projection rebuild executed {ScopeCount} planned scopes for target {Target}.",
                plan.Items.Count,
                plan.Target);
        }

        private async Task<IReadOnlyList<PaymentOperationEvent>> ReadCompleteStreamAsync(
            string streamId,
            long? requiredRevision,
            CancellationToken cancellationToken)
        {
            var events = await eventStore.ReadAsync(streamId, cancellationToken: cancellationToken)
                .ToListAsync(cancellationToken);
            if (events.Count == 0)
            {
                throw new InvalidOperationException($"Projection source stream '{streamId}' is empty or unavailable.");
            }

            var revision = requiredRevision ?? events.Max(static paymentEvent => paymentEvent.SequenceNumber);
            if (!ProjectionStreamReadValidator.IsComplete(events, revision) ||
                events[^1].SequenceNumber != revision)
            {
                throw new InvalidOperationException(
                    $"Projection source stream '{streamId}' is incomplete or changed; expected revision '{revision}'.");
            }

            return events;
        }

        private async Task ValidatePublishedScopesAsync(
            IReadOnlyCollection<ProjectionRebuildPlanItem> items,
            CancellationToken cancellationToken)
        {
            var currentScopes = await historyClient.DiscoverProjectionScopesAsync(cancellationToken);
            var currentByPeriod = currentScopes.ToDictionary(
                static scope => scope.FinancialPeriodIdentifier,
                StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (!currentByPeriod.TryGetValue(item.FinancialPeriodIdentifier, out var current) ||
                    current.PublishedRevision != item.ExpectedRevision ||
                    current.PaymentAccountId != item.PaymentAccountId ||
                    !string.Equals(current.StreamId, item.StreamId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Projection rebuild did not publish authoritative revision '{item.ExpectedRevision}' for scope '{item.StreamId}'.");
                }
            }
        }

        private static void ValidateScope(
            ProjectionRebuildPlanItem item,
            IReadOnlyCollection<PaymentOperationEvent> events)
        {
            var expectedStream = PaymentOperationNamesGenerator.GenerateForAccountMonthStream(
                item.FinancialPeriodIdentifier);
            if (!string.Equals(item.StreamId, expectedStream, StringComparison.Ordinal) ||
                events.Any(paymentEvent =>
                    paymentEvent.Payload is null ||
                    paymentEvent.Payload.PaymentAccountId != item.PaymentAccountId ||
                    !string.Equals(
                        paymentEvent.Payload.GetMonthPeriodPaymentAccountIdentifier(),
                        item.FinancialPeriodIdentifier,
                        StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Projection rebuild scope '{item.StreamId}' does not match its account, period, or source events.");
            }
        }

        private void ValidateBaseOptions()
        {
            if (string.IsNullOrWhiteSpace(_options.ExpectedTarget))
            {
                throw new InvalidOperationException("Projection rebuild requires an explicit expected target name.");
            }

            if (_options.MaxScopes <= 0)
            {
                throw new InvalidOperationException("Projection rebuild MaxScopes must be positive.");
            }
        }

        private void ValidatePlanTarget(ProjectionRebuildPlan plan)
        {
            if (!string.Equals(plan.Target, _options.ExpectedTarget, StringComparison.Ordinal) ||
                !string.Equals(plan.MongoEndpoint, _mongoEndpoint, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(plan.MongoDatabase, _mongoDatabase, StringComparison.Ordinal) ||
                !string.Equals(plan.EventStoreEndpoint, _eventStoreEndpoint, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Projection rebuild scope file target does not match the configured Mongo/EventStore target.");
            }
        }

        private void EnsureBoundedScope(int count)
        {
            if (count == 0 || count > _options.MaxScopes)
            {
                throw new InvalidOperationException(
                    $"Projection rebuild discovered '{count}' scopes; the configured bound is '{_options.MaxScopes}'.");
            }
        }
    }
}

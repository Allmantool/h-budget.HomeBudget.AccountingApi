using System.Collections.Generic;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediatR;
using Microsoft.Extensions.Logging;
using Serilog.Context;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Components.Accounts.Commands.Models;
using HomeBudget.Components.Accounts.Services.Interfaces;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Extensions;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Exstensions;
using HomeBudget.Core.Models;
using HomeBudget.Core.Observability;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal class SyncOperationsHistoryCommandHandler(
        ISender sender,
        ILogger<SyncOperationsHistoryCommandHandler> logger,
        IPaymentAccountService paymentAccountService,
        IPaymentsHistoryDocumentsClient historyDocumentsClient,
        IPaymentOperationsHistoryService operationsHistoryService,
        IOutboxPaymentStatusService outboxPaymentStatusService = null)
    : IRequestHandler<SyncOperationsHistoryCommand, Result<decimal>>
    {
        public async Task<Result<decimal>> Handle(SyncOperationsHistoryCommand request, CancellationToken cancellationToken)
        {
            var accountId = request.PaymentAccountId;
            var events = request.Events;

            if (events.IsNullOrEmpty())
            {
                return default;
            }

            var financialTransaction = events.First().Payload;
            var monthPeriodIdentifier = financialTransaction.GetMonthPeriodPaymentAccountIdentifier();

            var correlationId = events.FirstOrDefault()?.Metadata.Get(EventMetadataKeys.CorrelationId);
            var traceParent = events.FirstOrDefault()?.Metadata.Get(EventMetadataKeys.TraceParent);
            var traceState = events.FirstOrDefault()?.Metadata.Get(EventMetadataKeys.TraceState);
            var baggage = events.FirstOrDefault()?.Metadata.Get(EventMetadataKeys.Baggage);
            var messageId = events.FirstOrDefault()?.Metadata.Get(EventMetadataKeys.MessageId);
            var commandId = events.FirstOrDefault()?.Metadata.Get(EventMetadataKeys.CommandId);
            var traceId = events.FirstOrDefault()?.Metadata.Get(EventMetadataKeys.TraceId);
            var importBatchId = events.FirstOrDefault()?.Metadata.Get(EventMetadataKeys.ImportBatchId);
            var propagationContext = TraceContextPropagation.Extract(
                TraceContextPropagation.BuildCarrier(traceParent, traceState, baggage));
            var (parentContext, links) = TraceContextPropagation.ResolveParentAndLinks(
                events.Select(ev => (IReadOnlyDictionary<string, string>)TraceContextPropagation.BuildCarrier(
                    ev.Metadata.Get(EventMetadataKeys.TraceParent),
                    ev.Metadata.Get(EventMetadataKeys.TraceState),
                    ev.Metadata.Get(EventMetadataKeys.Baggage))));

            using (LogContext.PushProperty(EventMetadataKeys.CorrelationId, correlationId))
            using (LogContext.PushProperty(EventMetadataKeys.MessageId, messageId))
            using (LogContext.PushProperty("MessageId", messageId))
            using (LogContext.PushProperty("CommandId", commandId))
            using (LogContext.PushProperty("OperationId", financialTransaction.Key))
            using (LogContext.PushProperty("PaymentAccountId", accountId))
            using (LogContext.PushProperty("StreamId", request.Checkpoint?.StreamId))
            using (LogContext.PushProperty("CorrelationId", correlationId))
            using (LogContext.PushProperty("TraceId", traceId))
            using (LogContext.PushProperty("ImportBatchId", importBatchId))
            using (LogContext.PushProperty("projection_name", "sync_operations_history"))
            using (LogContext.PushProperty("aggregate_id", accountId))
            {
                var projectionRunId = Guid.NewGuid();
                using var activity = Activity.Current != null
                    ? ActivityPropagation.StartActivity(
                        "projection.sync_operations_history",
                        ActivityKind.Internal)
                    : ActivityPropagation.StartActivity(
                        "projection.sync_operations_history",
                        ActivityKind.Internal,
                        parentContext,
                        links);
                using var baggageScope = TraceContextPropagation.UseExtractedBaggage(propagationContext);

                if (activity != null)
                {
                    activity.SetCorrelationId(correlationId);
                    activity.SetTag("messaging.system", "eventstore");
                    activity.SetTag("messaging.event_count", events.Count());
                    activity.SetAccount(accountId);
                    activity.SetTag("messaging.message_id", messageId);
                    activity.SetTag("month.period", monthPeriodIdentifier);
                    activity.SetTag("projection.name", "sync_operations_history");
                }

                try
                {
                    await BenchmarkService.WithBenchmarkAsync(
                        async () => await operationsHistoryService.SyncHistoryAsync(
                            monthPeriodIdentifier,
                            events,
                            request.Checkpoint,
                            cancellationToken,
                            projectionRunId),
                        $"Execute {nameof(IPaymentOperationsHistoryService.SyncHistoryAsync)} for '{events.Count()}' events in scope of account '{accountId}'",
                        logger,
                        new { monthPeriodIdentifier });

                    var balanceSnapshot = await BenchmarkService.WithBenchmarkAsync(
                        async () => await historyDocumentsClient.CreateAccountBalanceSnapshotAsync(
                            accountId,
                            cancellationToken),
                        $"Create fenced balance snapshot for account '{accountId}'",
                        logger,
                        new { PaymentAccountId = accountId });
                    var finalBalance = await paymentAccountService.GetInitialBalanceAsync(accountId.ToString()) +
                        balanceSnapshot.ProjectedBalance;

                    await BenchmarkService.WithBenchmarkAsync(
                        async () =>
                        {
                            using var updateActivity = ActivityPropagation.StartActivity("mediatr.send.update_payment_balance", ActivityKind.Internal);
                            if (updateActivity != null)
                            {
                                updateActivity.SetCorrelationId(correlationId);
                                updateActivity.SetAccount(accountId);
                                updateActivity.SetTag("messaging.message_id", messageId);
                            }

                            var updateResult = await sender.Send(
                                new UpdatePaymentAccountBalanceCommand(
                                    accountId,
                                    finalBalance,
                                    balanceSnapshot.Fence),
                                cancellationToken);
                            if (updateResult?.IsSucceeded != true)
                            {
                                throw new InvalidOperationException(
                                    $"Fenced balance publication failed for account '{accountId}'.");
                            }

                            updateActivity?.SetStatus(ActivityStatusCode.Ok);
                        },
                        "Sending UpdatePaymentAccountBalanceCommand",
                        logger,
                        new { PaymentAccountId = accountId });

                    await MarkCommandsProjectedAsync(events);
                    await historyDocumentsClient.CompleteProjectionRunAsync(projectionRunId, "Succeeded");

                    activity?.SetStatus(ActivityStatusCode.Ok);
                    activity?.SetTag(ActivityTags.MongoCollection, "payments_projection");
                    activity?.AddEvent(ActivityEvents.ProjectionUpdated);

                    return Result<decimal>.Succeeded(finalBalance);
                }
                catch (Exception ex)
                {
                    await historyDocumentsClient.CompleteProjectionRunAsync(
                        projectionRunId,
                        "Failed",
                        ex.Message);
                    throw;
                }
            }
        }

        private async Task MarkCommandsProjectedAsync(IEnumerable<PaymentOperationEvent> events)
        {
            if (outboxPaymentStatusService is null)
            {
                return;
            }

            var commandIds = events
                .Select(x => x.Metadata.Get(EventMetadataKeys.CommandId))
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Distinct(System.StringComparer.Ordinal)
                .ToArray();

            foreach (var commandId in commandIds)
            {
                await outboxPaymentStatusService.MarkProjectedAsync(commandId, System.DateTime.UtcNow);
            }
        }
    }
}

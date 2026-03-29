using System.Collections.Generic;
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
        IPaymentOperationsHistoryService operationsHistoryService)
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
            var propagationContext = TraceContextPropagation.Extract(
                TraceContextPropagation.BuildCarrier(traceParent, traceState, baggage));
            var (parentContext, links) = TraceContextPropagation.ResolveParentAndLinks(
                events.Select(ev => (IReadOnlyDictionary<string, string>)TraceContextPropagation.BuildCarrier(
                    ev.Metadata.Get(EventMetadataKeys.TraceParent),
                    ev.Metadata.Get(EventMetadataKeys.TraceState),
                    ev.Metadata.Get(EventMetadataKeys.Baggage))));

            using (LogContext.PushProperty(EventMetadataKeys.CorrelationId, correlationId))
            using (LogContext.PushProperty(EventMetadataKeys.MessageId, messageId))
            using (LogContext.PushProperty("projection_name", "sync_operations_history"))
            using (LogContext.PushProperty("aggregate_id", accountId))
            {
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

                await BenchmarkService.WithBenchmarkAsync(
                    async () => await operationsHistoryService.SyncHistoryAsync(monthPeriodIdentifier, events),
                    $"Execute {nameof(IPaymentOperationsHistoryService.SyncHistoryAsync)} for '{events.Count()}' events in scope of account '{accountId}'",
                    logger,
                    new { monthPeriodIdentifier });

                var periodBalancesPaymentDocuments = await BenchmarkService.WithBenchmarkAsync(
                    async () => await historyDocumentsClient.GetAllPeriodBalancesForAccountAsync(accountId),
                    $"Retrieve balance for account '{accountId}'",
                    logger,
                    new { PaymentAccountId = accountId });

                if (periodBalancesPaymentDocuments.IsNullOrEmpty())
                {
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return default;
                }

                var monthBalanceHistoryRecords = periodBalancesPaymentDocuments
                    .Where(d => d != null)
                    .Select(d => d.Payload);

                var syncedStateRecords = monthBalanceHistoryRecords
                    .GroupBy(i => i.Record.Key)
                    .Select(gr => gr.MaxBy(ev => (ev.Record.OperationDay, ev.Record.OperationUnixTime)));

                var totalBalanceForAccount = syncedStateRecords.Sum(r => r.Balance);

                var finalBalance = await paymentAccountService.GetInitialBalanceAsync(accountId.ToString()) + totalBalanceForAccount;

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

                        await sender.Send(new UpdatePaymentAccountBalanceCommand(accountId, finalBalance), cancellationToken);

                        updateActivity?.SetStatus(ActivityStatusCode.Ok);
                    },
                    "Sending UpdatePaymentAccountBalanceCommand",
                    logger,
                    new { PaymentAccountId = accountId });

                activity?.SetStatus(ActivityStatusCode.Ok);
                activity?.SetTag(ActivityTags.MongoCollection, "payments_projection");
                activity?.AddEvent(ActivityEvents.ProjectionUpdated);

                return Result<decimal>.Succeeded(finalBalance);
            }
        }
    }
}

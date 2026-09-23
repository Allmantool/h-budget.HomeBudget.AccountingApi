using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;

using AutoMapper;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Observability;

namespace HomeBudget.Components.Operations.Services
{
    internal sealed class TransferOutboxRegistrationFactory(
        IMapper mapper,
        IDateTimeProvider dateTimeProvider) : ITransferOutboxRegistrationFactory
    {
        public TransferCommandRegistrationRequest Create(
            CrossAccountsTransferOperation transfer,
            TransferCommandContext context,
            string correlationId)
        {
            var operations = transfer.PaymentOperations.ToArray();
            var createdUtc = dateTimeProvider.GetNowUtc();
            var senderCommandId = Guid.NewGuid().ToString();
            var recipientCommandId = Guid.NewGuid().ToString();
            var parentCommandId = Guid.NewGuid().ToString();
            return new TransferCommandRegistrationRequest
            {
                TransferId = transfer.Key,
                CommandId = parentCommandId,
                IdempotencyKeyHash = context.IdempotencyKeyHash,
                RequestFingerprint = context.RequestFingerprint,
                SourceReference = context.SourceReference,
                SenderAccountId = operations[0].PaymentAccountId,
                RecipientAccountId = operations[1].PaymentAccountId,
                SenderAmount = context.SenderAmount,
                RecipientAmount = context.RecipientAmount,
                SenderCurrency = context.SenderCurrency,
                RecipientCurrency = context.RecipientCurrency,
                OperationDate = operations[0].OperationDay,
                SenderOutbox = CreateOutbox(operations[0], senderCommandId, context, "sender", correlationId, createdUtc),
                RecipientOutbox = CreateOutbox(operations[1], recipientCommandId, context, "recipient", correlationId, createdUtc)
            };
        }

        private OutboxAccountPaymentsEntity CreateOutbox(
            FinancialTransaction operation,
            string commandId,
            TransferCommandContext context,
            string side,
            string correlationId,
            DateTime createdUtc)
        {
            var paymentEvent = mapper.Map<PaymentOperationEvent>(new AddPaymentOperationCommand(operation)
            {
                CorrelationId = correlationId
            });
            paymentEvent.EnvelopId = Guid.Parse(commandId);
            paymentEvent.Metadata[EventMetadataKeys.CorrelationId] = correlationId;
            paymentEvent.Metadata[EventMetadataKeys.MessageId] = commandId;
            paymentEvent.Metadata[EventMetadataKeys.CommandId] = commandId;
            paymentEvent.Metadata[EventMetadataKeys.SourceSystem] = PaymentOperationEventIdentity.DefaultSourceSystem;

            var activity = Activity.Current;
            var propagation = TraceContextPropagation.Capture(activity);
            var traceParent = propagation.TryGetValue(TraceContextPropagation.TraceParent, out var parent)
                ? parent
                : string.Empty;
            var traceState = propagation.TryGetValue(TraceContextPropagation.TraceState, out var state)
                ? state
                : string.Empty;
            paymentEvent.Metadata[EventMetadataKeys.TraceParent] = traceParent;
            paymentEvent.Metadata[EventMetadataKeys.TraceState] = traceState;

            return new OutboxAccountPaymentsEntity
            {
                EventType = paymentEvent.EventType.ToString(),
                AggregateId = operation.PaymentAccountId.ToString(),
                OperationId = operation.Key.ToString(),
                PartitionKey = operation.GetPaymentAccountIdentifier(),
                CorrelationId = correlationId,
                MessageId = commandId,
                IdempotencyKeyHash = PaymentCommandFingerprint.CreateDerivedIdempotencyKeyHash(context.IdempotencyKeyHash, side),
                RequestFingerprint = context.RequestFingerprint,
                CommandType = PaymentCommandTypes.TransferCreate,
                CausationId = activity?.SpanId.ToString() ?? string.Empty,
                TraceParent = traceParent,
                TraceState = traceState,
                Payload = JsonSerializer.Serialize(paymentEvent),
                CreatedAt = createdUtc,
                UpdatedAt = createdUtc,
                CreatedUtc = createdUtc,
                UpdatedUtc = createdUtc
            };
        }
    }
}

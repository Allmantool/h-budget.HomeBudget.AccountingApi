﻿using System;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using Confluent.Kafka;
using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Accounts.CQRS.Commands.Models;
using HomeBudget.Components.Operations.CQRS.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.CQRS.Commands.Handlers
{
    internal class SavePaymentOperationCommandHandler(
        IMapper mapper,
        ISender sender,
        IEventStoreDbClient<PaymentOperationEvent> eventStoreDbClient,
        IKafkaDependentProducer<string, string> producer,
        IPaymentOperationsHistoryService paymentOperationsHistoryService)
        : IRequestHandler<SavePaymentOperationCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(SavePaymentOperationCommand request, CancellationToken cancellationToken)
        {
            var paymentAccountId = request.OperationForAdd.PaymentAccountId;

            var paymentSavedEvent = mapper.Map<PaymentOperationEvent>(request);

            producer.Produce(
                nameof(paymentSavedEvent),
                PaymentEventToMessageConverter.Convert(paymentSavedEvent),
                DeliveryReportHandler
            );

            await eventStoreDbClient.SendAsync(
                paymentSavedEvent,
                token: cancellationToken);

            var upToDateBalanceResult = await paymentOperationsHistoryService.SyncHistoryAsync(paymentAccountId);

            await sender.Send(
                new UpdatePaymentAccountBalanceCommand(
                    paymentAccountId,
                    upToDateBalanceResult.Payload),
                cancellationToken);

            return new Result<Guid>(paymentSavedEvent.Payload.Key);
        }

        private static void DeliveryReportHandler(DeliveryReport<string, string> deliveryReport)
        {
            if (deliveryReport.Status == PersistenceStatus.Persisted)
            {
                // success logic
            }

            if (deliveryReport.Status == PersistenceStatus.NotPersisted)
            {
                // add error handling
            }
        }
    }
}

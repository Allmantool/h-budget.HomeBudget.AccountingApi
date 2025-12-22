using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Domain;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Workers.OperationsConsumer.Logs;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Exceptions;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Handlers
{
    internal class PaymentOperationsDeliveryHandler : IPaymentOperationsDeliveryHandler
    {
        private readonly ILogger<PaymentOperationsDeliveryHandler> _logger;
        private readonly EventStoreDbOptions _options;
        private readonly IEventStoreDbWriteClient<PaymentOperationEvent> _eventStoreDbWriteClient;

        public PaymentOperationsDeliveryHandler(
            ILogger<PaymentOperationsDeliveryHandler> logger,
            IOptions<EventStoreDbOptions> options,
            IEventStoreDbWriteClient<PaymentOperationEvent> eventStoreDbWriteClient)
        {
            _logger = logger;
            _options = options.Value;
            _eventStoreDbWriteClient = eventStoreDbWriteClient;
        }

        public async Task HandleAsync(IEnumerable<PaymentOperationEvent> paymentEvents, CancellationToken cancellationToken)
        {
            if (paymentEvents.IsNullOrEmpty())
            {
                return;
            }

            try
            {
                var eventsGroupedByStream = paymentEvents
                    .GroupBy(GenerateStreamName)
                    .ToDictionary(g => g.Key, g => g.AsEnumerable());

                var sendTasks = eventsGroupedByStream.Select(async kvp =>
                {
                    var streamName = kvp.Key;
                    var streamEvents = kvp.Value;

                    var eventTypeTitle = $"{streamEvents.First().EventType}_{streamEvents.First().Payload.Key}";

                    try
                    {
                        await _eventStoreDbWriteClient.SendBatchAsync(streamEvents, streamName, eventTypeTitle, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.FailedToSendEventToStream(ex, streamName, ex.Message);
                    }
                });

                await Task.WhenAll(sendTasks);
            }
            catch (Exception ex)
            {
                _logger.FailedToProccessEvent(ex, nameof(PaymentOperationsDeliveryHandler), ex.Message);
            }
        }

        private static string GenerateStreamName(PaymentOperationEvent paymentEvent)
        {
            var accountPerMonthIdentifier = paymentEvent.Payload.GetMonthPeriodPaymentAccountIdentifier();
            return PaymentOperationNamesGenerator.GenerateForAccountMonthStream(accountPerMonthIdentifier);
        }
    }
}

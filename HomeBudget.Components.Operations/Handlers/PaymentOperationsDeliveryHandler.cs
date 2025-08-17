﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EventStoreDbClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Exceptions;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Handlers
{
    internal class PaymentOperationsDeliveryHandler : IPaymentOperationsDeliveryHandler
    {
        private readonly ILogger<PaymentOperationsDeliveryHandler> _logger;
        private readonly EventStoreDbOptions _options;
        private readonly IEventStoreDbClient<PaymentOperationEvent> _eventStoreDbClient;

        public PaymentOperationsDeliveryHandler(
            ILogger<PaymentOperationsDeliveryHandler> logger,
            IOptions<EventStoreDbOptions> options,
            IEventStoreDbClient<PaymentOperationEvent> eventStoreDbClient)
        {
            _logger = logger;
            _options = options.Value;
            _eventStoreDbClient = eventStoreDbClient;
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
                        await _eventStoreDbClient.SendBatchAsync(streamEvents, streamName, eventTypeTitle, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send events to stream {StreamName}: {Message}", streamName, ex.Message);
                    }
                });

                await Task.WhenAll(sendTasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Handler} failed to process payment events: {Message}", nameof(PaymentOperationsDeliveryHandler), ex.Message);
            }
        }

        private string GenerateStreamName(PaymentOperationEvent paymentEvent)
        {
            var accountPerMonthIdentifier = paymentEvent.Payload.GetMonthPeriodIdentifier();
            return PaymentOperationNamesGenerator.GenerateForAccountMonthStream(accountPerMonthIdentifier);
        }
    }
}

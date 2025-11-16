using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Handlers;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Logs;
using HomeBudget.Core.Exceptions;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.BackgroundServices
{
    internal class PaymentOperationsBatchProcessorBackgroundService : BackgroundService
    {
        private readonly EventStoreDbOptions _options;
        private readonly Channel<PaymentOperationEvent> _paymentEventsChannel;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IPaymentOperationsDeliveryHandler _operationsDeliveryHandler;
        private readonly ILogger<PaymentOperationsBatchProcessorBackgroundService> _logger;
        private readonly TimeSpan _flushInterval;

        private const int DelayForChannelReader = 10;

        public PaymentOperationsBatchProcessorBackgroundService(
            Channel<PaymentOperationEvent> paymentEventsChannel,
            ILogger<PaymentOperationsBatchProcessorBackgroundService> logger,
            IDateTimeProvider dateTimeProvider,
            IOptions<EventStoreDbOptions> eventStoreDbOptions,
            IPaymentOperationsDeliveryHandler operationsDeliveryHandler)
        {
            _logger = logger;
            _options = eventStoreDbOptions.Value;
            _paymentEventsChannel = paymentEventsChannel;
            _operationsDeliveryHandler = operationsDeliveryHandler;
            _dateTimeProvider = dateTimeProvider;

            _flushInterval = TimeSpan.FromMilliseconds(_options.BatchProcessingFlushPeriodInMs);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var eventsBatch = await CollectBatchAsync(stoppingToken);

                if (!eventsBatch.IsNullOrEmpty())
                {
                    try
                    {
                        await _operationsDeliveryHandler.HandleAsync(eventsBatch, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.OperationDeliveryError(nameof(PaymentOperationsBatchProcessorBackgroundService), ex.Message, ex);
                    }
                    finally
                    {
                        eventsBatch.Clear();
                    }
                }
            }
        }

        private async Task<List<PaymentOperationEvent>> CollectBatchAsync(CancellationToken stoppingToken)
        {
            var batchStartTime = _dateTimeProvider.GetNowUtc();
            var eventsBatch = new List<PaymentOperationEvent>();

            while (_dateTimeProvider.GetNowUtc() - batchStartTime < _flushInterval && eventsBatch.Count <= _options.EventProcessingBatchSize)
            {
                if (_paymentEventsChannel.Reader.TryRead(out var evt))
                {
                    eventsBatch.Add(evt);
                }
                else
                {
                    await Task.Delay(DelayForChannelReader, stoppingToken);
                }
            }

            return eventsBatch;
        }
    }
}

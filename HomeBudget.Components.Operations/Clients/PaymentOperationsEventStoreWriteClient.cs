using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using EventStore.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Operations.Factories;
using HomeBudget.Components.Operations.Logs;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Clients
{
    internal sealed class PaymentOperationsEventStoreWriteClient
        : BaseEventStoreWriteClient<PaymentOperationEvent>, IDisposable
    {
        private readonly Channel<PaymentOperationEvent> _paymentEventsBuffer;

        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly ILogger<PaymentOperationsEventStoreWriteClient> _logger;
        private readonly EventStoreDbOptions _opts;

        private readonly CancellationTokenSource _cts = new();
        private readonly SemaphoreSlim _requestRateLimiter;

        public PaymentOperationsEventStoreWriteClient(
            ILogger<PaymentOperationsEventStoreWriteClient> logger,
            EventStoreClient client,
            IOptions<EventStoreDbOptions> options)
            : base(client, options.Value, logger)
        {
            _opts = options.Value;
            _logger = logger;
            _paymentEventsBuffer = PaymentOperationEventChannelFactory.CreateBufferChannel(_opts);
            _retryPolicy = EventStoreRetryPolicies.BuildRetryPolicy(_opts, _logger);
            _requestRateLimiter = new(_opts.RequestRateLimiter);
        }

        public override async Task<IWriteResult> SendBatchAsync(
            IEnumerable<PaymentOperationEvent> eventsForSending,
            string streamName,
            string eventType = null,
            CancellationToken ctx = default)
        {
            try
            {
                await _requestRateLimiter.WaitAsync(ctx);

                return await _retryPolicy.ExecuteAsync(
                    async retryCtx => await base.SendBatchAsync(eventsForSending, streamName, eventType, ctx),
                    ctx);
            }
            catch (Exception ex)
            {
                PaymentOperationsEventStoreClientLogs.SendEventToDeadQueue(_logger, ex.Message, ex);
                await SendToDeadLetterQueueAsync(eventsForSending, ex);
                throw;
            }
            finally
            {
                _requestRateLimiter.Release();
            }
        }

        public override async Task<IWriteResult> SendAsync(
            PaymentOperationEvent eventForSending,
            string streamName = default,
            string eventType = default,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(eventForSending);

            var ctx = new Context
            {
                [nameof(PaymentOperationEvent.EventType)] = eventType,
                [nameof(PaymentOperationEvent.Payload.Key)] = eventForSending.Payload?.Key
            };

            try
            {
                return await _retryPolicy.ExecuteAsync(
                    async retryCtx =>
                    {
                        eventForSending.Metadata ??= new Dictionary<string, string>();
                        eventForSending.Metadata[EventDbEventMetadataKeys.CorrelationId] = retryCtx.CorrelationId.ToString();
                        eventForSending.Metadata[EventDbEventMetadataKeys.RetryCount] = retryCtx.Count.ToString();

                        eventForSending.EnvelopId = retryCtx.CorrelationId;

                        var opKey = eventForSending.Payload?.Key.ToString();
                        PaymentOperationsEventStoreClientLogs.SendingEvent(_logger, eventType, opKey, retryCtx.CorrelationId);

                        var result = await base.SendAsync(
                            eventForSending,
                            streamName ?? string.Empty,
                            eventType ?? string.Empty,
                            token);

                        PaymentOperationsEventStoreClientLogs.EventSent(_logger, eventType, opKey, retryCtx.CorrelationId);
                        return result;
                    },
                    ctx);
            }
            catch (Exception ex)
            {
                PaymentOperationsEventStoreClientLogs.RetriesExhausted(_logger, eventType, eventForSending.Payload?.Key.ToString(), ex);
                await SendToDeadLetterQueueAsync(eventForSending, ex);
                throw;
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _paymentEventsBuffer.Writer.TryComplete();

            try
            {
                _requestRateLimiter.Dispose();

                base.Dispose();
            }
            catch
            {
            }

            _cts.Dispose();
        }
    }
}

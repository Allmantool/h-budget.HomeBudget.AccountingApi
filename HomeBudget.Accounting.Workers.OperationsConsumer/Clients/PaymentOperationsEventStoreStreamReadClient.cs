using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using EventStore.Client;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Domain;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Accounting.Workers.OperationsConsumer.Factories;
using HomeBudget.Accounting.Workers.OperationsConsumer.Logs;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Clients
{
    internal sealed class PaymentOperationsEventStoreStreamReadClient
        : BaseEventStoreStreamReadClient<PaymentOperationEvent>, IDisposable
    {
        private readonly Channel<PaymentOperationEvent> _paymentEventsBuffer;
        private readonly ConcurrentDictionary<string, PaymentOperationEvent> _latestEventsPerAccount = new();

        private readonly ILogger<PaymentOperationsEventStoreStreamReadClient> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly EventStoreDbOptions _opts;

        private readonly CancellationTokenSource _cts = new();
        private readonly Task _processorTask;

        public PaymentOperationsEventStoreStreamReadClient(
            ILogger<PaymentOperationsEventStoreStreamReadClient> logger,
            IServiceScopeFactory serviceScopeFactory,
            IDateTimeProvider dateTimeProvider,
            EventStoreClient client,
            IOptions<EventStoreDbOptions> options)
            : base(client, options.Value, logger)
        {
            _opts = options.Value;
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _dateTimeProvider = dateTimeProvider;
            _paymentEventsBuffer = PaymentOperationEventChannelFactory.CreateBufferChannel(_opts);
            _processorTask = Task.Run(ProcessEventBatchAsync, _cts.Token);
            _processorTask.ContinueWith(
                t => _logger.BatchProcessorCrashed(t.Exception),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        public override IAsyncEnumerable<PaymentOperationEvent> ReadAsync(
            string streamName,
            int maxEvents = int.MaxValue,
            CancellationToken token = default)
        {
            return base.ReadAsync(streamName, maxEvents, token);
        }

        protected override async Task OnEventAppearedAsync(PaymentOperationEvent eventData)
        {
            try
            {
                await _paymentEventsBuffer.Writer.WriteAsync(eventData, _cts.Token);
            }
            catch (ChannelClosedException)
            {
                _logger.ChannelClosedDropping(eventData.EventType.ToString());
            }
            catch (OperationCanceledException)
            {
                _logger.ChannelWriteCanceled();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _paymentEventsBuffer.Writer.TryComplete();

            try
            {
                _processorTask?.Wait(TimeSpan.FromSeconds(5));

                base.Dispose();
            }
            catch
            {
            }

            _cts.Dispose();
        }

        private async Task ProcessEventBatchAsync()
        {
            var delayMs = Math.Max(0, _opts.EventBatchingDelayInMs);

            while (await _paymentEventsBuffer.Reader.WaitToReadAsync(_cts.Token))
            {
                while (_paymentEventsBuffer.Reader.TryRead(out var evt))
                {
                    _latestEventsPerAccount[evt.Payload.GetMonthPeriodPaymentAccountIdentifier()] = evt;
                }

                if (delayMs > 0)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(delayMs), _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }

                foreach (var (periodKey, latestEvent) in _latestEventsPerAccount.ToArray())
                {
                    try
                    {
                        await HandlePaymentOperationEventAsync(latestEvent.Payload, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return; // shutdown
                    }
                    catch (Exception ex)
                    {
                        _logger.HandleEventsFailed(periodKey, ex);
                    }
                    finally
                    {
                        _latestEventsPerAccount.Remove(periodKey, out _);
                    }
                }
            }
        }

        private async Task HandlePaymentOperationEventAsync(FinancialTransaction transaction, CancellationToken ct)
        {
            var accountId = transaction.PaymentAccountId;
            var monthPeriodPaymentAccountIdentifier = transaction.GetMonthPeriodPaymentAccountIdentifier();

            var paymentAccountStream = PaymentOperationNamesGenerator.GenerateForAccountMonthStream(monthPeriodPaymentAccountIdentifier);

            var events = await ReadAsync(paymentAccountStream, token: ct).ToListAsync(ct);

            foreach (var e in events)
            {
                if (e.ProcessedAt == default)
                {
                    e.ProcessedAt = _dateTimeProvider.GetNowUtc();
                }
            }

            try
            {
                await SendSyncOperationsHistoryAsync(accountId, events, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.SyncFailed(accountId, paymentAccountStream, ex);
                throw;
            }
        }

        private async Task SendSyncOperationsHistoryAsync(
            Guid paymentAccountId,
            IEnumerable<PaymentOperationEvent> events,
            CancellationToken ct)
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();

            _logger.DispatchingSync(paymentAccountId, events.Count());
            await sender.Send(new SyncOperationsHistoryCommand(paymentAccountId, events), ct);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using HomeBudget.Accounting.Notifications.Models;

namespace HomeBudget.Accounting.Notifications.Services
{
    internal sealed class NotificationChannel : INotificationChannel
    {
        private const int ReplayBufferSize = 100;

        private readonly Lock _gate = new();
        private readonly LinkedList<PaymentAccountNotification> _recentEvents = [];
        private readonly Dictionary<long, Channel<PaymentAccountNotification>> _subscribers = [];

        private long _subscriptionIdSequence;

        public async Task PublishAsync(PaymentAccountNotification evt)
        {
            ArgumentNullException.ThrowIfNull(evt);

            KeyValuePair<long, Channel<PaymentAccountNotification>>[] subscribers;

            lock (_gate)
            {
                _recentEvents.AddLast(evt);

                if (_recentEvents.Count > ReplayBufferSize)
                {
                    _recentEvents.RemoveFirst();
                }

                subscribers = _subscribers.ToArray();
            }

            foreach (var (_, subscriber) in subscribers)
            {
                await subscriber.Writer.WriteAsync(evt);
            }
        }

        public async IAsyncEnumerable<PaymentAccountNotification> ReadAsync(
            string lastEventId = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var subscriber = Channel.CreateUnbounded<PaymentAccountNotification>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            var subscriptionId = RegisterSubscriber(subscriber);

            try
            {
                foreach (var replayEvent in GetReplayEvents(lastEventId))
                {
                    yield return replayEvent;
                }

                await foreach (var evt in subscriber.Reader.ReadAllAsync(ct))
                {
                    yield return evt;
                }
            }
            finally
            {
                UnregisterSubscriber(subscriptionId, subscriber);
            }
        }

        private long RegisterSubscriber(Channel<PaymentAccountNotification> subscriber)
        {
            lock (_gate)
            {
                var subscriptionId = ++_subscriptionIdSequence;
                _subscribers[subscriptionId] = subscriber;
                return subscriptionId;
            }
        }

        private void UnregisterSubscriber(long subscriptionId, Channel<PaymentAccountNotification> subscriber)
        {
            lock (_gate)
            {
                _subscribers.Remove(subscriptionId);
            }

            subscriber.Writer.TryComplete();
        }

        private PaymentAccountNotification[] GetReplayEvents(string lastEventId)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(lastEventId))
                {
                    return [];
                }

                return _recentEvents
                    .SkipWhile(e => e.EventId != lastEventId)
                    .Skip(1)
                    .ToArray();
            }
        }
    }
}

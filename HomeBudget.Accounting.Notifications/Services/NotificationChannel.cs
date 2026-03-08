using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using HomeBudget.Accounting.Notifications.Models;
using HomeBudget.Accounting.Notifications.Services;

internal class NotificationChannel : INotificationChannel
{
    private readonly Channel<PaymentAccountNotification> _channel = Channel.CreateUnbounded<PaymentAccountNotification>();
    private readonly int _bufferSize = 100;
    private readonly LinkedList<PaymentAccountNotification> _recentEvents = new();

    public async Task PublishAsync(PaymentAccountNotification evt)
    {
        lock (_recentEvents)
        {
            _recentEvents.AddLast(evt);
            if (_recentEvents.Count > _bufferSize)
            {
                _recentEvents.RemoveFirst();
            }
        }

        await _channel.Writer.WriteAsync(evt);
    }

    public async IAsyncEnumerable<PaymentAccountNotification> ReadAsync(
        string lastEventId = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(lastEventId))
        {
            List<PaymentAccountNotification> toReplay;

            lock (_recentEvents)
            {
                toReplay = _recentEvents
                    .SkipWhile(e => e.EventId != lastEventId)
                    .Skip(1) // skip the last delivered
                    .ToList();
            }

            foreach (var evt in toReplay)
            {
                yield return evt;
            }
        }

        while (await _channel.Reader.WaitToReadAsync(ct))
        {
            while (_channel.Reader.TryRead(out var evt))
            {
                yield return evt;
            }
        }
    }
}
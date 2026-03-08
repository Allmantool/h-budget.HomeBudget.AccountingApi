using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using HomeBudget.Accounting.Notifications.Models;

namespace HomeBudget.Accounting.Notifications.Services
{
    public sealed class NotificationChannel : INotificationChannel
    {
        private readonly Channel<PaymentAccountNotification> _channel =
            Channel.CreateUnbounded<PaymentAccountNotification>(
                new UnboundedChannelOptions
                {
                    SingleReader = false,
                    SingleWriter = false
                });

        public ValueTask PublishAsync(PaymentAccountNotification notification)
            => _channel.Writer.WriteAsync(notification);

        public ValueTask<PaymentAccountNotification> ReadAsync(CancellationToken ct)
            => _channel.Reader.ReadAsync(ct);

        public IAsyncEnumerable<PaymentAccountNotification> ReadAllAsync(CancellationToken ct)
            => _channel.Reader.ReadAllAsync(ct);
    }
}
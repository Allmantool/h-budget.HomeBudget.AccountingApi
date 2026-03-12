using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using HomeBudget.Accounting.Notifications.Models;
using HomeBudget.Accounting.Notifications.Services;

namespace HomeBudget.Accounting.Api.Tests.Notifications
{
    public class NotificationChannelTests
    {
        [Test]
        public async Task PublishAsync_ShouldBroadcastToAllSubscribers()
        {
            var sut = new NotificationChannel();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var firstSubscriber = sut.ReadAsync(ct: cts.Token).GetAsyncEnumerator(cts.Token);
            var secondSubscriber = sut.ReadAsync(ct: cts.Token).GetAsyncEnumerator(cts.Token);

            var firstMove = firstSubscriber.MoveNextAsync().AsTask();
            var secondMove = secondSubscriber.MoveNextAsync().AsTask();

            var expectedEvent = new PaymentAccountNotification(
                Guid.NewGuid().ToString("N"),
                "balance-updated",
                Guid.NewGuid());

            await sut.PublishAsync(expectedEvent);

            (await firstMove).Should().BeTrue();
            (await secondMove).Should().BeTrue();

            firstSubscriber.Current.Should().Be(expectedEvent);
            secondSubscriber.Current.Should().Be(expectedEvent);

            await firstSubscriber.DisposeAsync();
            await secondSubscriber.DisposeAsync();
        }

        [Test]
        public async Task ReadAsync_ShouldReplayEventsAfterLastEventId()
        {
            var sut = new NotificationChannel();
            var firstEvent = new PaymentAccountNotification(Guid.NewGuid().ToString("N"), "created", Guid.NewGuid());
            var secondEvent = new PaymentAccountNotification(Guid.NewGuid().ToString("N"), "updated", Guid.NewGuid());
            var thirdEvent = new PaymentAccountNotification(Guid.NewGuid().ToString("N"), "updated", Guid.NewGuid());

            await sut.PublishAsync(firstEvent);
            await sut.PublishAsync(secondEvent);
            await sut.PublishAsync(thirdEvent);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var subscriber = sut.ReadAsync(firstEvent.EventId, cts.Token).GetAsyncEnumerator(cts.Token);

            try
            {
                var replayedEvents = new[]
                {
                    await ReadNextAsync(subscriber),
                    await ReadNextAsync(subscriber)
                };

                replayedEvents.Should().Equal(secondEvent, thirdEvent);
            }
            finally
            {
                await subscriber.DisposeAsync();
            }
        }

        private static async Task<PaymentAccountNotification> ReadNextAsync(IAsyncEnumerator<PaymentAccountNotification> subscriber)
        {
            (await subscriber.MoveNextAsync()).Should().BeTrue();
            return subscriber.Current;
        }
    }
}

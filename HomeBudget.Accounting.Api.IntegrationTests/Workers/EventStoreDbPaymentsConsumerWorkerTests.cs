using System;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Workers.OperationsConsumer;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.IntegrationTests.Workers
{
    [TestFixture]
    public class EventStoreDbPaymentsConsumerWorkerTests
    {
        [Test]
        public async Task StartAsync_WhenPersistentSubscriptionCreateFailsOnce_RetriesCreateBeforeSubscribe()
        {
            var createSucceeded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var createAttempts = 0;

            var subscriptionClient = new Mock<IEventStoreDbSubscriptionReadClient<PaymentOperationEvent>>(MockBehavior.Strict);
            subscriptionClient
                .Setup(client => client.CreatePersistentSubscriptionAsync(It.IsAny<CancellationToken>()))
                .Returns<CancellationToken>(_ =>
                {
                    createAttempts++;

                    if (createAttempts == 1)
                    {
                        return Task.FromException(new InvalidOperationException("EventStore is not ready yet."));
                    }

                    createSucceeded.TrySetResult();
                    return Task.CompletedTask;
                });

            subscriptionClient
                .Setup(client => client.SubscribeAsync(It.IsAny<CancellationToken>()))
                .Returns<CancellationToken>(async cancellationToken =>
                {
                    subscribed.TrySetResult();
                    await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
                    return null;
                });

            var worker = new EventStoreDbPaymentsConsumerWorker(
                Mock.Of<ILogger<EventStoreDbPaymentsConsumerWorker>>(),
                Options.Create(new EventStoreDbOptions { RetryInSeconds = 0 }),
                subscriptionClient.Object);

            await worker.StartAsync(CancellationToken.None);

            try
            {
                await WaitAsync(createSucceeded.Task);
                await WaitAsync(subscribed.Task);

                subscriptionClient.Verify(
                    client => client.CreatePersistentSubscriptionAsync(It.IsAny<CancellationToken>()),
                    Times.AtLeast(2));
                subscriptionClient.Verify(
                    client => client.SubscribeAsync(It.IsAny<CancellationToken>()),
                    Times.Once);
            }
            finally
            {
                await worker.StopAsync(CancellationToken.None);
                worker.Dispose();
            }
        }

        private static async Task WaitAsync(Task task)
        {
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));

            if (completed != task)
            {
                Assert.Fail("Timed out waiting for the EventStore subscription worker retry path.");
            }

            await task;
        }
    }
}

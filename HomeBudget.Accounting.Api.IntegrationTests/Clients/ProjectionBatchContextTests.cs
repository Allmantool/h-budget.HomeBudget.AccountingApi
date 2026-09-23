using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using FluentAssertions;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Accounting.Workers.OperationsConsumer.Clients;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Observability;

namespace HomeBudget.Accounting.Api.IntegrationTests.Clients
{
    [TestFixture]
    public sealed class ProjectionBatchContextTests
    {
        [Test]
        public async Task Merge_WhenIncomingSequenceIsNewer_UsesLatestEventAndAcknowledgesEveryDelivery()
        {
            var firstAcknowledgements = 0;
            var secondAcknowledgements = 0;
            var firstContext = Context(acknowledge: () =>
            {
                firstAcknowledgements++;
                return Task.CompletedTask;
            });
            var secondContext = Context(acknowledge: () =>
            {
                secondAcknowledgements++;
                return Task.CompletedTask;
            });
            var batch = Batch(sequence: 1, firstContext, "first");
            var incoming = Batch(sequence: 2, secondContext, "second");

            batch.Merge(incoming);
            await batch.CompleteAsync();

            batch.LatestEvent.SequenceNumber.Should().Be(2);
            batch.SubscriptionContext.Should().BeSameAs(secondContext);
            batch.PropagationCarriers.Should().HaveCount(2);
            firstAcknowledgements.Should().Be(1);
            secondAcknowledgements.Should().Be(1);
        }

        [Test]
        public async Task Merge_WhenIncomingSequenceIsOlder_RetainsLatestEventButRetriesEveryDelivery()
        {
            var reasons = new List<string>();
            var latestContext = Context(retry: reason =>
            {
                reasons.Add($"latest:{reason}");
                return Task.CompletedTask;
            });
            var olderContext = Context(retry: reason =>
            {
                reasons.Add($"older:{reason}");
                return Task.CompletedTask;
            });
            var batch = Batch(sequence: 3, latestContext, "latest");

            batch.Merge(Batch(sequence: 2, olderContext, "older"));
            await batch.RetryAsync(new InvalidOperationException("projection failed"));

            batch.LatestEvent.SequenceNumber.Should().Be(3);
            batch.SubscriptionContext.Should().BeSameAs(latestContext);
            reasons.Should().BeEquivalentTo("latest:projection failed", "older:projection failed");
        }

        [Test]
        public async Task Retry_WhenExceptionIsNull_UsesDefaultReason()
        {
            string reason = null;
            var batch = Batch(
                sequence: 1,
                Context(retry: value =>
                {
                    reason = value;
                    return Task.CompletedTask;
                }),
                "carrier");

            await batch.RetryAsync(null);

            reason.Should().Be("Projection processing failed.");
        }

        [Test]
        public void MarkFailed_RequiresExceptionAndRecordsIt()
        {
            var batch = Batch(sequence: 1, subscriptionContext: null, "carrier");
            var error = new InvalidOperationException("failed");

            batch.Invoking(value => value.MarkFailed(null)).Should().Throw<ArgumentNullException>();
            batch.MarkFailed(error);

            batch.ProcessingError.Should().BeSameAs(error);
        }

        [Test]
        public async Task SubscriptionContext_AfterSuccessfulSettlement_IgnoresFurtherSettlement()
        {
            var acknowledgements = 0;
            var retries = 0;
            var context = Context(
                acknowledge: () =>
                {
                    acknowledgements++;
                    return Task.CompletedTask;
                },
                retry: _ =>
                {
                    retries++;
                    return Task.CompletedTask;
                });

            await context.AcknowledgeAsync();
            await context.AcknowledgeAsync();
            await context.RetryAsync("ignored");

            acknowledgements.Should().Be(1);
            retries.Should().Be(0);
        }

        [Test]
        public async Task SubscriptionContext_WhenSettlementFails_AllowsRetry()
        {
            var attempts = 0;
            var retries = 0;
            var context = Context(
                acknowledge: () =>
                {
                    attempts++;
                    throw new InvalidOperationException("temporary");
                },
                retry: _ =>
                {
                    retries++;
                    return Task.CompletedTask;
                });

            await context.Invoking(value => value.AcknowledgeAsync()).Should().ThrowAsync<InvalidOperationException>();
            await context.RetryAsync("retry");

            attempts.Should().Be(1);
            retries.Should().Be(1);
        }

        [Test]
        public async Task SubscriptionContext_WhenCallbacksAreMissing_CompletesWithoutFailure()
        {
            var context = Context();

            await context.AcknowledgeAsync();
            await context.RetryAsync("ignored");
        }

        private static ProjectionBatchContext Batch(
            long sequence,
            EventStoreSubscriptionContext subscriptionContext,
            string carrierValue)
        {
            var paymentEvent = new PaymentOperationEvent
            {
                SequenceNumber = sequence,
                Payload = new FinancialTransaction { PaymentAccountId = Guid.NewGuid() }
            };
            var envelope = new ActivityEnvelope<PaymentOperationEvent>(
                paymentEvent,
                new Dictionary<string, string> { ["traceparent"] = carrierValue });
            return ProjectionBatchContext.Create(envelope, subscriptionContext);
        }

        private static EventStoreSubscriptionContext Context(
            Func<Task> acknowledge = null,
            Func<string, Task> retry = null) =>
            new()
            {
                StreamId = "stream",
                Revision = "1",
                Position = "2",
                Acknowledge = acknowledge,
                Retry = retry
            };
    }
}

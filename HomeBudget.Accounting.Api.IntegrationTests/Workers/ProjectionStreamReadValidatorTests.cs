using System;

using FluentAssertions;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Workers.OperationsConsumer.Clients;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Workers
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    public sealed class ProjectionStreamReadValidatorTests
    {
        [Test]
        public void CompleteContiguousSnapshot_CoversRequiredRevision()
        {
            ProjectionStreamReadValidator.IsComplete(BuildEvents(0, 1, 2), 2).Should().BeTrue();
        }

        [TestCase(new long[] { 1, 2 }, 2)]
        [TestCase(new long[] { 0, 2 }, 2)]
        [TestCase(new long[] { 0, 1 }, 2)]
        public void IncompleteSnapshot_IsRejected(long[] revisions, long requiredRevision)
        {
            ProjectionStreamReadValidator.IsComplete(BuildEvents(revisions), requiredRevision).Should().BeFalse();
        }

        private static PaymentOperationEvent[] BuildEvents(params long[] revisions)
        {
            return Array.ConvertAll(revisions, revision => new PaymentOperationEvent
            {
                SequenceNumber = revision
            });
        }
    }
}

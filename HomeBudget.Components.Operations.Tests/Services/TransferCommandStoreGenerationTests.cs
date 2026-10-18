using System;
using System.Threading.Tasks;

using FluentAssertions;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Accounting.Infrastructure.Data.Interfaces;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services;

namespace HomeBudget.Components.Operations.Tests.Services
{
    [TestFixture]
    public sealed class TransferCommandStoreGenerationTests
    {
        [Test]
        public async Task RegisterAsync_WhenIdempotencyKeyIsReusedAcrossGenerations_ScopesLookupAndChildrenToGeneration()
        {
            string capturedSql = null;
            var reader = new Mock<IBaseReadRepository>();
            reader
                .Setup(x => x.SingleAsync<TransferCommandRegistration>(It.IsAny<string>(), It.IsAny<object>()))
                .Callback<string, object>((sql, _) => capturedSql = sql)
                .ReturnsAsync(new TransferCommandRegistration());
            var sut = new TransferCommandStore(reader.Object);

            await sut.RegisterAsync(BuildRequest());

            capturedSql.Should().Contain("LedgerId = @LedgerId");
            capturedSql.Should().Contain(
                "LedgerGenerationId = @LedgerGenerationId",
                "the same transfer idempotency key may be valid in another ledger generation");
            capturedSql.Should().Contain("LedgerId, LedgerGenerationId, EventType");
            capturedSql.Should().Contain("@LedgerId, @LedgerGenerationId, @SenderEventType");
            capturedSql.Should().Contain("@LedgerId, @LedgerGenerationId, @RecipientEventType");
            capturedSql.Should().Contain("[HomeBudget.DataProtection].dbo.ActiveLedgerGenerations");
            capturedSql.Should().Contain("GenerationToken = @GenerationTokenValue");
            capturedSql.Should().Contain("THROW 51001");
        }

        private static TransferCommandRegistrationRequest BuildRequest()
        {
            var now = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
            return new TransferCommandRegistrationRequest
            {
                TransferId = Guid.NewGuid(),
                CommandId = Guid.NewGuid().ToString("N"),
                IdempotencyKeyHash = "same-key-across-generations",
                RequestFingerprint = "fingerprint",
                SenderAccountId = Guid.NewGuid(),
                RecipientAccountId = Guid.NewGuid(),
                SenderAmount = 10m,
                RecipientAmount = 20m,
                SenderCurrency = "USD",
                RecipientCurrency = "BYN",
                OperationDate = new DateOnly(2026, 10, 5),
                SenderOutbox = BuildOutbox(now),
                RecipientOutbox = BuildOutbox(now)
            };
        }

        private static OutboxAccountPaymentsEntity BuildOutbox(DateTime now)
        {
            return new OutboxAccountPaymentsEntity
            {
                AggregateId = Guid.NewGuid().ToString(),
                EventType = "Added",
                MessageId = Guid.NewGuid().ToString("N"),
                PartitionKey = Guid.NewGuid().ToString("N"),
                Payload = "{}",
                CreatedUtc = now
            };
        }
    }
}

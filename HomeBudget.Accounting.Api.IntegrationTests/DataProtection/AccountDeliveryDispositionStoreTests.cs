using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.Data.SqlClient;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Infrastructure.DataProtection;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [NonParallelizable]
    internal sealed class AccountDeliveryDispositionStoreTests : BaseIntegrationTests
    {
        private const string DispositionsTable = "dbo.AccountDeliveryDispositions";
        private const string FirstPayloadHash =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string ChangedPayloadHash =
            "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

        [Test]
        public async Task RecordAsync_WhenDeliveryIsRedelivered_IsDurableIdempotentAndQueryable()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var messageId = $"account-{Guid.NewGuid():N}";
            var store = new SqlAccountDeliveryDispositionStore(TestContainers.AccountingDbConnectionString);
            var first = CreateDisposition(messageId, FirstPayloadHash, 42, DateTime.UtcNow);

            (await store.RecordAsync(first, cancellationToken))
                .Should().Be(AccountDeliveryDispositionResult.Recorded);
            (await store.RecordAsync(
                first with { Offset = 43, DisposedUtc = first.DisposedUtc.AddSeconds(1) },
                cancellationToken)).Should().Be(AccountDeliveryDispositionResult.AlreadyRecorded);

            await using var connection = await DataProtectionControlPlaneTestDatabase.OpenAsync(
                TestContainers,
                cancellationToken,
                DispositionsTable);
            var persisted = await ReadAsync(connection, messageId, cancellationToken);
            persisted.PayloadSha256.Should().Be(FirstPayloadHash);
            persisted.DeliveryCount.Should().Be(2);
            persisted.LastOffset.Should().Be(43);
        }

        [Test]
        public async Task RecordAsync_WhenIdentityHasChangedPayload_RejectsConflictWithoutMutatingDisposition()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var messageId = $"account-{Guid.NewGuid():N}";
            var store = new SqlAccountDeliveryDispositionStore(TestContainers.AccountingDbConnectionString);
            var first = CreateDisposition(messageId, FirstPayloadHash, 42, DateTime.UtcNow);
            await store.RecordAsync(first, cancellationToken);

            var act = async () => await store.RecordAsync(
                first with
                {
                    PayloadSha256 = ChangedPayloadHash,
                    Offset = 43,
                    DisposedUtc = first.DisposedUtc.AddSeconds(1)
                },
                cancellationToken);

            await act.Should().ThrowAsync<AccountDeliveryDispositionConflictException>();
            await using var connection = await DataProtectionControlPlaneTestDatabase.OpenAsync(
                TestContainers,
                cancellationToken,
                DispositionsTable);
            var persisted = await ReadAsync(connection, messageId, cancellationToken);
            persisted.PayloadSha256.Should().Be(FirstPayloadHash);
            persisted.DeliveryCount.Should().Be(1);
            persisted.LastOffset.Should().Be(42);
        }

        private static AccountDeliveryDisposition CreateDisposition(
            string messageId,
            string payloadSha256,
            long offset,
            DateTime disposedUtc)
        {
            return new AccountDeliveryDisposition
            {
                MessageId = messageId,
                LedgerId = "default",
                LedgerGenerationId = "legacy",
                PayloadSha256 = payloadSha256,
                Reason = "stale-generation",
                Topic = "accounting.accounts",
                Partition = 1,
                Offset = offset,
                DisposedUtc = disposedUtc
            };
        }

        private static async Task<PersistedDisposition> ReadAsync(
            SqlConnection connection,
            string messageId,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT PayloadSha256, DeliveryCount, LastOffset
                  FROM dbo.AccountDeliveryDispositions
                 WHERE MessageId = @MessageId
                   AND LedgerId = N'default'
                   AND LedgerGenerationId = N'legacy';";
            command.Parameters.Add("@MessageId", SqlDbType.NVarChar, 256).Value = messageId;
            await using var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SingleRow,
                cancellationToken);
            (await reader.ReadAsync(cancellationToken)).Should().BeTrue();
            return new PersistedDisposition(reader.GetString(0), reader.GetInt32(1), reader.GetInt64(2));
        }

        private sealed record PersistedDisposition(
            string PayloadSha256,
            int DeliveryCount,
            long LastOffset);
    }
}

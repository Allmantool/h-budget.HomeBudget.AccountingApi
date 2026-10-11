using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.Data.SqlClient;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Components.Accounts.Models;
using HomeBudget.Components.Categories.Models;
using HomeBudget.Components.Contractors.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Constants;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [NonParallelizable]
    public sealed class LedgerGenerationCompatibilityTests : BaseIntegrationTests
    {
        private const string LegacyLedgerId = "default";
        private const string LegacyGenerationId = "legacy";
        private const string LegacyGenerationToken = "legacy-token-v1";

        [Test]
        public async Task GenerationZero_WhenControlPlaneIsInitialized_IsConfiguredAsDefaultLegacy()
        {
            await using var connection = await DataProtectionControlPlaneTestDatabase.OpenAsync(
                TestContainers,
                TestContext.CurrentContext.CancellationToken,
                "dbo.ActiveLedgerGenerations");
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT GenerationId, GenerationNumber, GenerationToken
                FROM dbo.ActiveLedgerGenerations
                WHERE LedgerId = @LedgerId;";
            command.Parameters.Add("@LedgerId", SqlDbType.NVarChar, 128).Value = LegacyLedgerId;
            await using var reader = await command.ExecuteReaderAsync(TestContext.CurrentContext.CancellationToken);

            (await reader.ReadAsync(TestContext.CurrentContext.CancellationToken)).Should().BeTrue(
                "generation zero must be configured deterministically instead of inferred from missing scope");
            reader.GetString(0).Should().Be(LegacyGenerationId);
            reader.GetInt64(1).Should().Be(0);
            reader.GetString(2).Should().Be(LegacyGenerationToken);
        }

        [Test]
        public async Task GenerationToken_WhenControlPlaneIsInitialized_IsRequiredUniqueAndHasNoDefault()
        {
            await using var connection = await DataProtectionControlPlaneTestDatabase.OpenAsync(
                TestContainers,
                TestContext.CurrentContext.CancellationToken,
                "dbo.ActiveLedgerGenerations");
            await using var metadataCommand = connection.CreateCommand();
            metadataCommand.CommandText = @"
                SELECT is_nullable, default_object_id
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.ActiveLedgerGenerations')
                  AND name = N'GenerationToken';";
            await using (var reader = await metadataCommand.ExecuteReaderAsync(TestContext.CurrentContext.CancellationToken))
            {
                (await reader.ReadAsync(TestContext.CurrentContext.CancellationToken)).Should().BeTrue();
                reader.GetBoolean(0).Should().BeFalse("every generation must have a server-issued token");
                reader.GetInt32(1).Should().Be(0, "old writers must not receive an implicit generation token");
            }

            await using var duplicateCommand = connection.CreateCommand();
            duplicateCommand.CommandText = @"
                INSERT dbo.ActiveLedgerGenerations
                    (EnvironmentId, LedgerId, GenerationId, GenerationToken, PreviousGenerationId,
                     GenerationNumber, ActivatedByOperationId, ActivatedUtc)
                VALUES
                    (@EnvironmentId, @LedgerId, N'replacement', @GenerationToken, N'legacy',
                     1, NULL, SYSUTCDATETIME());";
            duplicateCommand.Parameters.Add("@EnvironmentId", SqlDbType.NVarChar, 128).Value = $"test-{Guid.NewGuid():N}";
            duplicateCommand.Parameters.Add("@LedgerId", SqlDbType.NVarChar, 128).Value = $"ledger-{Guid.NewGuid():N}";
            duplicateCommand.Parameters.Add("@GenerationToken", SqlDbType.NVarChar, 200).Value = LegacyGenerationToken;

            var duplicate = async () => await duplicateCommand.ExecuteNonQueryAsync(
                TestContext.CurrentContext.CancellationToken);
            await duplicate.Should().ThrowAsync<SqlException>("generation tokens are globally unique opaque identities");
        }

        [Test]
        public async Task LegacyAccountingRows_WhenGenerationColumnsAreAdded_AreBackfilledAndRequired()
        {
            await using var connection = new SqlConnection(TestContainers.AccountingDbConnectionString);
            await connection.OpenAsync(TestContext.CurrentContext.CancellationToken);

            foreach (var table in new[] { "OutboxAccountPayments", "TransferCommands", "PaymentInboxMessages" })
            {
                var columns = await ReadScopeColumnsAsync(connection, table);

                columns.Should().ContainKey(
                    "LedgerId",
                    $"{table} must persist the logical ledger scope");
                columns.Should().ContainKey(
                    "LedgerGenerationId",
                    $"{table} must persist the ledger generation separately from projection GenerationId");
                columns["LedgerId"].Should().BeFalse(
                    $"legacy {table} rows must be backfilled to '{LegacyLedgerId}' before the column becomes required");
                columns["LedgerGenerationId"].Should().BeFalse(
                    $"legacy {table} rows must be backfilled to '{LegacyGenerationId}' before the column becomes required");
            }
        }

        [Test]
        public async Task AccountingWrites_WhenScopeIsOmitted_AreRejectedInsteadOfDefaultingToLegacy()
        {
            await using var connection = new SqlConnection(TestContainers.AccountingDbConnectionString);
            await connection.OpenAsync(TestContext.CurrentContext.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT dbo.PaymentInboxMessages
                    (MessageId, Topic, Partition, [Offset], Status, RetryCount, CreatedUtc, UpdatedUtc)
                VALUES
                    (@MessageId, N'test', 0, 0, N'Processing', 0, SYSUTCDATETIME(), SYSUTCDATETIME());";
            command.Parameters.Add("@MessageId", SqlDbType.NVarChar, 200).Value = Guid.NewGuid().ToString("N");

            var act = async () => await command.ExecuteNonQueryAsync(TestContext.CurrentContext.CancellationToken);
            await act.Should().ThrowAsync<SqlException>("unscoped old writers must fail after generation activation");
        }

        [Test]
        public async Task TransferIdentity_WhenLogicalIdIsReusedInAnotherGeneration_IsolatedByScope()
        {
            await using var connection = new SqlConnection(TestContainers.AccountingDbConnectionString);
            await connection.OpenAsync(TestContext.CurrentContext.CancellationToken);
            var transferId = Guid.NewGuid();

            await InsertTransferAsync(connection, transferId, "legacy", "a");
            var secondInsert = async () => await InsertTransferAsync(connection, transferId, "replacement", "b");

            await secondInsert.Should().NotThrowAsync("logical transfer IDs must be preservable across replacement generations");
        }

        [Test]
        public void DurableProcessingContracts_ExposeLedgerGenerationScope()
        {
            var scopedTypes = new[]
            {
                typeof(OutboxAccountPaymentsEntity),
                typeof(PaymentInboxMessageEntity),
                typeof(TransferCommandRecord)
            };

            AssertGenerationScope(scopedTypes);
        }

        [Test]
        public void MongoContracts_ExposeLedgerGenerationWithoutOverloadingProjectionGeneration()
        {
            var scopedTypes = new[]
            {
                typeof(PaymentAccountDocument),
                typeof(CategoryDocument),
                typeof(ContractorDocument),
                typeof(PaymentHistoryDocument),
                typeof(PaymentHistoryProjectionHeadDocument),
                typeof(PaymentHistoryProjectionAccountDocument),
                typeof(ProjectionAuditDocument)
            };

            AssertGenerationScope(scopedTypes);
            typeof(PaymentHistoryDocument).GetProperty(nameof(PaymentHistoryDocument.GenerationId))
                .Should().NotBeNull("payment-history publication GenerationId remains a distinct concept");
        }

        [Test]
        public void CommandStatusContract_ExposesOpaqueServerSelectedGenerationTokenWithoutClientScopeFields()
        {
            AssertGenerationScope([typeof(PaymentCommandRecord), typeof(PaymentCommandStatusResponse)]);
            typeof(PaymentCommandStatusResponse).GetProperty("GenerationToken")
                .Should().NotBeNull("status clients need a server-issued token for stale-generation detection");
            typeof(CreateOperationRequest).GetProperty("LedgerId").Should().BeNull(
                "ordinary clients cannot choose a ledger scope");
            typeof(CreateOperationRequest).GetProperty("LedgerGenerationId").Should().BeNull(
                "ordinary clients cannot choose a ledger generation");
        }

        [Test]
        public void MessagingContracts_DefineLedgerGenerationHeadersAndEventMetadata()
        {
            ReadStringConstant(typeof(KafkaMessageHeaders), "LedgerId").Should().Be("ledger-id");
            ReadStringConstant(typeof(KafkaMessageHeaders), "LedgerGenerationId").Should().Be("ledger-generation-id");
            ReadStringConstant(typeof(EventMetadataKeys), "LedgerId").Should().Be("ledger-id");
            ReadStringConstant(typeof(EventMetadataKeys), "LedgerGenerationId").Should().Be("ledger-generation-id");
        }

        private async Task<Dictionary<string, bool>> ReadScopeColumnsAsync(SqlConnection connection, string table)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT column_name, is_nullable
                FROM information_schema.columns
                WHERE table_schema = N'dbo'
                  AND table_name = @TableName
                  AND column_name IN (N'LedgerId', N'LedgerGenerationId');";
            command.Parameters.Add("@TableName", SqlDbType.NVarChar, 128).Value = table;

            var result = new Dictionary<string, bool>(StringComparer.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(TestContext.CurrentContext.CancellationToken);
            while (await reader.ReadAsync(TestContext.CurrentContext.CancellationToken))
            {
                result.Add(reader.GetString(0), reader.GetString(1) == "YES");
            }

            return result;
        }

        private static string ReadStringConstant(Type type, string name)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
            field.Should().NotBeNull($"{type.Name}.{name} is part of the generation propagation contract");
            return field!.GetValue(null) as string;
        }

        private static void AssertGenerationScope(IEnumerable<Type> types)
        {
            foreach (var type in types)
            {
                type.GetProperty("LedgerId").Should().NotBeNull($"{type.Name} must expose ledger scope");
                type.GetProperty("LedgerGenerationId").Should().NotBeNull(
                    $"{type.Name} must expose ledger generation scope");
            }
        }

        private static async Task InsertTransferAsync(
            SqlConnection connection,
            Guid transferId,
            string generationId,
            string suffix)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT dbo.TransferCommands
                    (LedgerId, LedgerGenerationId, TransferId, CommandId, IdempotencyKeyHash, RequestFingerprint,
                     SenderAccountId, RecipientAccountId, SenderOperationId, RecipientOperationId,
                     SenderCommandId, RecipientCommandId, SenderAmount, RecipientAmount, SenderCurrency,
                     RecipientCurrency, OperationDate, SourceReference, CreatedUtc, LastSeenUtc)
                VALUES
                    (N'default', @GenerationId, @TransferId, @CommandId, @IdempotencyKey, @Fingerprint,
                     @SenderId, @RecipientId, @SenderOperationId, @RecipientOperationId,
                     @SenderCommandId, @RecipientCommandId, 1, 1, N'USD', N'USD', '2026-10-05',
                     @SourceReference, SYSUTCDATETIME(), SYSUTCDATETIME());";
            command.Parameters.Add("@GenerationId", SqlDbType.NVarChar, 128).Value = generationId;
            command.Parameters.Add("@TransferId", SqlDbType.UniqueIdentifier).Value = transferId;
            command.Parameters.Add("@CommandId", SqlDbType.NVarChar, 100).Value = $"command-{suffix}-{Guid.NewGuid():N}";
            command.Parameters.Add("@IdempotencyKey", SqlDbType.NVarChar, 64).Value = Guid.NewGuid().ToString("N");
            command.Parameters.Add("@Fingerprint", SqlDbType.NVarChar, 64).Value = new string('a', 64);
            command.Parameters.Add("@SenderId", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
            command.Parameters.Add("@RecipientId", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
            command.Parameters.Add("@SenderOperationId", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
            command.Parameters.Add("@RecipientOperationId", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
            command.Parameters.Add("@SenderCommandId", SqlDbType.NVarChar, 100).Value = $"sender-{suffix}-{Guid.NewGuid():N}";
            command.Parameters.Add("@RecipientCommandId", SqlDbType.NVarChar, 100).Value = $"recipient-{suffix}-{Guid.NewGuid():N}";
            command.Parameters.Add("@SourceReference", SqlDbType.NVarChar, 500).Value = $"test:{suffix}:{Guid.NewGuid():N}";
            await command.ExecuteNonQueryAsync(TestContext.CurrentContext.CancellationToken);
        }
    }
}

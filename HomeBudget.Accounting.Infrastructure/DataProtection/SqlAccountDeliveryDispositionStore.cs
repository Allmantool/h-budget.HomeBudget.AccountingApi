using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.SqlClient;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    internal sealed class SqlAccountDeliveryDispositionStore : IAccountDeliveryDispositionStore
    {
        private const int PayloadConflictErrorNumber = 51004;
        private readonly string _connectionString;

        public SqlAccountDeliveryDispositionStore(string connectionString)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = "HomeBudget.DataProtection"
            };
            _connectionString = builder.ConnectionString;
        }

        public async Task<AccountDeliveryDispositionResult> RecordAsync(
            AccountDeliveryDisposition disposition,
            CancellationToken cancellationToken = default)
        {
            Validate(disposition);
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = CreateCommand(connection, disposition);

            try
            {
                var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
                return (AccountDeliveryDispositionResult)result;
            }
            catch (SqlException exception) when (exception.Number == PayloadConflictErrorNumber)
            {
                throw new AccountDeliveryDispositionConflictException(
                    "The account delivery identity is already bound to different content.",
                    exception);
            }
        }

        private static SqlCommand CreateCommand(
            SqlConnection connection,
            AccountDeliveryDisposition disposition)
        {
            var command = connection.CreateCommand();
            command.CommandText = @"
                SET XACT_ABORT ON;
                SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
                BEGIN TRANSACTION;

                DECLARE @ExistingPayloadSha256 CHAR(64);
                SELECT @ExistingPayloadSha256 = PayloadSha256
                  FROM dbo.AccountDeliveryDispositions WITH (UPDLOCK, HOLDLOCK)
                 WHERE MessageId = @MessageId
                   AND LedgerId = @LedgerId
                   AND LedgerGenerationId = @LedgerGenerationId;

                IF @ExistingPayloadSha256 IS NULL
                BEGIN
                    INSERT INTO dbo.AccountDeliveryDispositions
                        (MessageId, LedgerId, LedgerGenerationId, PayloadSha256, Reason,
                         FirstTopic, FirstPartition, FirstOffset, LastTopic, LastPartition,
                         LastOffset, FirstDisposedUtc, LastDisposedUtc, DeliveryCount)
                    VALUES
                        (@MessageId, @LedgerId, @LedgerGenerationId, @PayloadSha256, @Reason,
                         @Topic, @Partition, @Offset, @Topic, @Partition,
                         @Offset, @DisposedUtc, @DisposedUtc, 1);
                    COMMIT TRANSACTION;
                    SELECT CAST(0 AS INT);
                    RETURN;
                END;

                IF @ExistingPayloadSha256 <> @PayloadSha256
                BEGIN
                    ROLLBACK TRANSACTION;
                    THROW 51004, N'Account delivery identity is bound to different content.', 1;
                END;

                UPDATE dbo.AccountDeliveryDispositions
                   SET LastTopic = @Topic,
                       LastPartition = @Partition,
                       LastOffset = @Offset,
                       LastDisposedUtc = CASE
                           WHEN @DisposedUtc > LastDisposedUtc THEN @DisposedUtc
                           ELSE LastDisposedUtc
                       END,
                       DeliveryCount = DeliveryCount + 1
                 WHERE MessageId = @MessageId
                   AND LedgerId = @LedgerId
                   AND LedgerGenerationId = @LedgerGenerationId;
                COMMIT TRANSACTION;
                SELECT CAST(1 AS INT);";
            AddParameter(command, "@MessageId", SqlDbType.NVarChar, disposition.MessageId, 256);
            AddParameter(command, "@LedgerId", SqlDbType.NVarChar, disposition.LedgerId, 128);
            AddParameter(command, "@LedgerGenerationId", SqlDbType.NVarChar, disposition.LedgerGenerationId, 128);
            AddParameter(command, "@PayloadSha256", SqlDbType.Char, disposition.PayloadSha256, 64);
            AddParameter(command, "@Reason", SqlDbType.NVarChar, disposition.Reason, 64);
            AddParameter(command, "@Topic", SqlDbType.NVarChar, disposition.Topic, 249);
            AddParameter(command, "@Partition", SqlDbType.Int, disposition.Partition);
            AddParameter(command, "@Offset", SqlDbType.BigInt, disposition.Offset);
            AddParameter(command, "@DisposedUtc", SqlDbType.DateTime2, disposition.DisposedUtc);
            return command;
        }

        private static void AddParameter(
            SqlCommand command,
            string name,
            SqlDbType type,
            object value,
            int size = 0)
        {
            var parameter = size > 0
                ? command.Parameters.Add(name, type, size)
                : command.Parameters.Add(name, type);
            parameter.Value = value;
        }

        private static void Validate(AccountDeliveryDisposition disposition)
        {
            ArgumentNullException.ThrowIfNull(disposition);
            ArgumentException.ThrowIfNullOrWhiteSpace(disposition.MessageId);
            ArgumentNullException.ThrowIfNull(disposition.LedgerId);
            ArgumentNullException.ThrowIfNull(disposition.LedgerGenerationId);
            ArgumentException.ThrowIfNullOrWhiteSpace(disposition.PayloadSha256);
            ArgumentException.ThrowIfNullOrWhiteSpace(disposition.Reason);

            if (disposition.PayloadSha256.Length != 64)
            {
                throw new ArgumentException(
                    "The payload SHA-256 must contain 64 hexadecimal characters.",
                    nameof(disposition));
            }
        }
    }
}

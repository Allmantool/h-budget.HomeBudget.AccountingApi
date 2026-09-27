using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Factories;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Tools.PaymentHistoryProjectionPublisher
{
    internal static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
            BsonSerializer.TryRegisterSerializer(new DateOnlySerializer());
            if (args.Length != 7)
            {
                Console.Error.WriteLine(
                    "Usage: <mongo-connection> <database> <account-id> <record-count> <revision> <gate-file> <run-id>");
                return 2;
            }

            var accountId = Guid.Parse(args[2]);
            var records = BuildRecords(accountId, int.Parse(args[3], CultureInfo.InvariantCulture));
            var revision = long.Parse(args[4], CultureInfo.InvariantCulture);
            var snapshot = BuildSnapshot(accountId, records, revision);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!File.Exists(args[5]))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
            }

            var client = CreateClient(args[0], args[1]);
            try
            {
                var result = await client.PublishSnapshotAsync(
                    snapshot,
                    Guid.Parse(args[6]),
                    timeout.Token);
                Console.WriteLine($"{result.State}|{result.CommittedRevision}|{result.GenerationId}");
                return 0;
            }
            finally
            {
                (client as IDisposable)?.Dispose();
            }
        }

        private static IPaymentsHistoryDocumentsClient CreateClient(string connectionString, string database)
        {
            var contract = typeof(IPaymentsHistoryDocumentsClient);
            var implementation = contract.Assembly.GetType(
                "HomeBudget.Components.Operations.Clients.PaymentsHistoryDocumentsClient",
                throwOnError: true);
            var options = Options.Create(new MongoDbOptions
            {
                ConnectionString = connectionString,
                PaymentsHistory = database
            });
            return (IPaymentsHistoryDocumentsClient)Activator.CreateInstance(
                implementation,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: [options],
                culture: CultureInfo.InvariantCulture);
        }

        private static PaymentHistoryProjectionSnapshot BuildSnapshot(
            Guid accountId,
            IReadOnlyList<PaymentOperationHistoryRecord> records,
            long revision)
        {
            var period = new DateOnly(2026, 1, 1).ToFinancialPeriod().ToFinancialMonthIdentifier(accountId);
            var fingerprintType = typeof(IPaymentsHistoryDocumentsClient).Assembly.GetType(
                "HomeBudget.Components.Operations.Services.PaymentHistoryProjectionFingerprint",
                throwOnError: true);
            var create = fingerprintType.GetMethod(
                "Create",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var fingerprint = (string)create.Invoke(null, [records]);
            return new PaymentHistoryProjectionSnapshot(
                period,
                accountId,
                $"payment-account-{accountId:D}-2026-01",
                revision,
                null,
                fingerprint,
                records);
        }

        private static IReadOnlyList<PaymentOperationHistoryRecord> BuildRecords(Guid accountId, int count)
        {
            return Enumerable.Range(0, count)
                .Select(index => new PaymentOperationHistoryRecord
                {
                    Balance = 10m * (index + 1),
                    StreamRevision = index,
                    Record = new FinancialTransaction
                    {
                        Key = CreateDeterministicGuid(accountId, index),
                        OperationUnixTime = 1767225600000L + index,
                        PaymentAccountId = accountId,
                        Amount = 10m,
                        OperationDay = new DateOnly(2026, 1, 1).AddDays(index % 28),
                        Comment = $"process-publication-{index}"
                    }
                })
                .ToArray();
        }

        private static Guid CreateDeterministicGuid(Guid accountId, int index)
        {
            var bytes = accountId.ToByteArray();
            var indexBytes = BitConverter.GetBytes(index);
            for (var position = 0; position < indexBytes.Length; position++)
            {
                bytes[position] ^= indexBytes[position];
            }

            return new Guid(bytes);
        }
    }
}

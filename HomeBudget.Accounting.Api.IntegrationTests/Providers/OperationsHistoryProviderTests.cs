using System;
using System.Diagnostics;
using System.Threading.Tasks;

using FluentAssertions;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests;
using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Tests.Constants;

namespace HomeBudget.Accounting.Api.IntegrationTests.Providers
{
    [TestFixture]
    public class OperationsHistoryProviderTests
    {
        private TestContainersService _testContainers;

        [OneTimeSetUp]
        public async Task SetupAsync()
        {
            BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
            BsonSerializer.TryRegisterSerializer(new DateOnlySerializer());

            var maxWait = TimeSpan.FromMinutes(BaseTestContainerOptions.StopTimeoutInMinutes);
            var sw = Stopwatch.StartNew();

            _testContainers = await TestContainersService.InitAsync();

            while (!_testContainers.IsReadyForUse)
            {
                if (sw.Elapsed > maxWait)
                {
                    Assert.Fail(
                        $"TestContainersService did not start within the allowed timeout of {maxWait.TotalSeconds} seconds."
                    );
                }

                await Task.Delay(TimeSpan.FromSeconds(ComponentTestOptions.TestContainersWaitingInSeconds));
            }

            sw.Stop();
        }

        [Test]
        public async Task Should_InsertOneAsync_PaymentRecordsSuccessfully()
        {
            var dbConnection = _testContainers.MongoDbContainer.GetConnectionString();

            using var client = new MongoClient(dbConnection);

            var database = client.GetDatabase("Test-Db");
            var operationsHistoryCollection = database.GetCollection<PaymentHistoryDocument>("PaymentsHistory");

            var paymentAccount = Guid.Parse("7a9b408e-efab-4134-920c-b4734580ce14");

            var historyDocument = new PaymentHistoryDocument
            {
                Payload = new PaymentOperationHistoryRecord
                {
                    Balance = 11.24m,
                    Record = new FinancialTransaction
                    {
                        PaymentAccountId = paymentAccount,
                        Key = Guid.Empty,
                        CategoryId = Guid.Empty,
                        ContractorId = Guid.Empty,
                        Amount = 11.24m,
                        Comment = "Comment test",
                        OperationDay = new DateOnly(2023, 12, 31)
                    }
                }
            };

            await operationsHistoryCollection.InsertOneAsync(historyDocument);

            var operationRecords = await operationsHistoryCollection
                .Find(p => p.Payload.Record.PaymentAccountId.CompareTo(paymentAccount) == 0)
                .ToListAsync();

            operationRecords.Count.Should().Be(1);
        }

        [Test]
        public async Task Should_InsertManyAsync_PaymentRecordsSuccessfully()
        {
            var dbConnection = _testContainers.MongoDbContainer.GetConnectionString();

            using var client = new MongoClient(dbConnection);

            var database = client.GetDatabase("Test-Db");
            var operationsHistoryCollection = database.GetCollection<PaymentHistoryDocument>("PaymentsHistory");

            var paymentAccount = Guid.Parse("1f6d8ce1-b604-4094-9211-634c5f948002");

            var payload = new[]
            {
                    new PaymentHistoryDocument
                    {
                        Payload = new PaymentOperationHistoryRecord
                        {
                            Balance = 11.24m,
                            Record = new FinancialTransaction
                            {
                                PaymentAccountId = paymentAccount,
                                Key = Guid.Empty,
                                CategoryId = Guid.Empty,
                                ContractorId = Guid.Empty,
                                Amount = 11.24m,
                                Comment = "Comment test",
                                OperationDay = new DateOnly(2023, 12, 31)
                            }
                        }
                    },
                    new PaymentHistoryDocument
                    {
                        Payload = new PaymentOperationHistoryRecord
                        {
                            Balance = 111.24m,
                            Record = new FinancialTransaction
                            {
                                PaymentAccountId = paymentAccount,
                                Key = Guid.Empty,
                                CategoryId = Guid.Empty,
                                ContractorId = Guid.Empty,
                                Amount = 100m,
                                Comment = "Comment test 2",
                                OperationDay = new DateOnly(2024, 1, 2)
                            }
                        }
                    }
            };

            await operationsHistoryCollection.InsertManyAsync(payload);

            var operationRecords = await operationsHistoryCollection
                .Find(p => p.Payload.Record.PaymentAccountId.CompareTo(paymentAccount) == 0)
                .ToListAsync();

            operationRecords.Count.Should().Be(payload.Length);
        }
    }
}

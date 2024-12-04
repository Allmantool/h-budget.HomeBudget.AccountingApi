using System;
using System.Threading.Tasks;

using DotNet.Testcontainers.Containers;
using FluentAssertions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Bson.Serialization;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Testcontainers.MongoDb;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Tests.Providers
{
    [TestFixture]
    public class OperationsHistoryProviderTests
    {
        private MongoDbContainer _mongoDbContainer;

        [OneTimeSetUp]
        public void Setup()
        {
            BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

            _mongoDbContainer = new MongoDbBuilder()
                .WithImage("mongo:7.0.5-rc0-jammy")
                .WithName($"{nameof(OperationsHistoryProviderTests)}-container")
                .WithHostname("test-mongo-db-host")
                .WithAutoRemove(true)
                .WithCleanUp(true)
                .WithPortBinding(28017, 28017)
                .Build();
        }

        [OneTimeTearDown]
        public async Task DownAsync()
        {
            await _mongoDbContainer.DisposeAsync();
        }

        [Test]
        public async Task Should_InsertOneAsync_PaymentRecordsSuccessfully()
        {
            if (_mongoDbContainer.State != TestcontainersStates.Running)
            {
                await _mongoDbContainer.StartAsync();
            }

            var dbConnection = _mongoDbContainer.GetConnectionString();

            var client = new MongoClient(dbConnection);

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
            if (_mongoDbContainer.State != TestcontainersStates.Running)
            {
                await _mongoDbContainer.StartAsync();
            }

            var dbConnection = _mongoDbContainer.GetConnectionString();

            var client = new MongoClient(dbConnection);

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

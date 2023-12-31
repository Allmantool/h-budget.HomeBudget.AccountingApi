using System;
using System.Threading.Tasks;

using DotNet.Testcontainers.Containers;
using FluentAssertions;
using MongoDB.Driver;
using NUnit.Framework;
using Testcontainers.MongoDb;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Tests.Models;

namespace HomeBudget.Components.Operations.Tests.Providers
{
    [TestFixture]
    public class OperationsHistoryProviderTests
    {
        private MongoDbContainer _mongoDbContainer;

        [OneTimeSetUp]
        public void Setup()
        {
            _mongoDbContainer = new MongoDbBuilder()
                .WithImage("mongo:7.0.5-rc0-jammy")
                .WithName($"{nameof(OperationsHistoryProviderTests)}-container")
                .WithHostname("test-mongo-db-host")
                .WithAutoRemove(true)
                .WithCleanUp(true)
                .WithPortBinding(28017, 28017)
                .Build();
        }

        [Test]
        public async Task Should_InsertOne_PaymentRecordsSuccessfully()
        {
            await using (_mongoDbContainer)
            {
                if (_mongoDbContainer.State != TestcontainersStates.Running)
                {
                    await _mongoDbContainer.StartAsync();
                }

                var dbConnection = _mongoDbContainer.GetConnectionString();

                var client = new MongoClient(dbConnection);

                var database = client.GetDatabase("Test-Db");
                var operationsHistoryCollection = database.GetCollection<PaymentHistoryDocument>("PaymentsHistory");

                var payload = new PaymentHistoryDocument
                {
                    Balance = 11.24m,
                    Record = new PaymentOperation
                    {
                        PaymentAccountId = Guid.Empty,
                        Key = Guid.Empty,
                        CategoryId = Guid.Empty,
                        ContractorId = Guid.Empty,
                        Amount = 11.24m,
                        Comment = "Comment test",
                        OperationDay = new DateOnly(2023, 12, 31)
                    }
                };

                await operationsHistoryCollection.InsertOneAsync(payload);

                var operationRecords = await operationsHistoryCollection.Find(_ => true).ToListAsync();

                operationRecords.Count.Should().Be(1);
            }
        }
    }
}

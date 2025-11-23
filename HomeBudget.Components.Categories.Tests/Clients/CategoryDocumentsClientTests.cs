using System;
using System.Diagnostics;
using System.Threading.Tasks;

using HomeBudget.Accounting.Api.IntegrationTests;
using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure;
using HomeBudget.Components.Categories.Clients;
using HomeBudget.Components.Categories.Models;
using HomeBudget.Components.Operations.Tests.Constants;
using HomeBudget.Core.Options;

using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using NUnit.Framework;

namespace HomeBudget.Components.Categories.Tests.Clients
{
    [TestFixture]
    public class CategoryDocumentsClientTests
    {
        private CategoryDocumentsClient _client;
        private IMongoDatabase _database;

        [OneTimeSetUp]
        public async Task GlobalSetupAsync()
        {
            MongoEnumerationSerializerRegistration.RegisterAllBaseEnumerations(typeof(CategoryTypes).Assembly);
            BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
            BsonSerializer.TryRegisterSerializer(new DateOnlySerializer());

            var maxWait = TimeSpan.FromMinutes(3);
            var sw = Stopwatch.StartNew();

            while (!TestContainersService.IsStarted)
            {
                _ = Task.Run(() => TestContainersService.UpAndRunningContainersAsync());

                if (sw.Elapsed > maxWait)
                {
                    Assert.Fail(
                        $"TestContainersService did not start within the allowed timeout of {maxWait.TotalSeconds} seconds."
                    );
                }

                await Task.Delay(TimeSpan.FromSeconds(ComponentTestOptions.TestContainersWaitingInSeconds));
            }

            sw.Stop();
            var dbConnection = TestContainersService.MongoDbContainer.GetConnectionString();

            var mongoClient = new MongoClient(dbConnection);

            var mongoOptions = Options.Create(new MongoDbOptions
            {
                ConnectionString = dbConnection,
                PaymentsHistory = "payments_history_test",
                HandBooks = "handbooks_test",
                PaymentAccounts = "payment_accounts_test"
            });

            _database = mongoClient.GetDatabase(mongoOptions.Value.HandBooks);
            _client = new CategoryDocumentsClient(mongoOptions);
        }

        [SetUp]
        public async Task TestSetupAsync()
        {
            await _database.DropCollectionAsync(LedgerDbCollections.Categories);
        }

        [Test]
        public async Task InsertOneAsync_Should_Insert_And_Return_Key()
        {
            var category = new Category(CategoryTypes.Income, ["CAT-001"])
            {
                Key = Guid.NewGuid(),
            };

            var result = await _client.InsertOneAsync(category);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSucceeded, Is.True);
                Assert.That(result.Payload, Is.EqualTo(category.Key));
            });

            var collection = _database.GetCollection<CategoryDocument>(LedgerDbCollections.Categories);

            var insertedAll = await collection.Find(c => true).ToListAsync();
            var inserted = await collection.Find(c => c.Payload.Key == category.Key).FirstOrDefaultAsync();

            Assert.Multiple(() =>
            {
                Assert.That(inserted, Is.Not.Null);
                Assert.That(inserted.Payload.CategoryKey, Is.EqualTo($"{CategoryTypes.Income.Key}-CAT-001"));
            });
        }

        [Test]
        public async Task GetByIdAsync_Should_Return_Correct_Document()
        {
            var category = new Category(CategoryTypes.Income, ["CAT-002"])
            {
                Key = Guid.NewGuid(),
            };

            var collection = _database.GetCollection<CategoryDocument>(LedgerDbCollections.Categories);
            await collection.InsertOneAsync(
                new CategoryDocument
                {
                    Payload = category
                });

            var result = await _client.GetByIdAsync(category.Key);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSucceeded, Is.True);
                Assert.That(result.Payload, Is.Not.Null);
                Assert.That(result.Payload.Payload.Key, Is.EqualTo(category.Key));
            });
        }

        [Test]
        public async Task CheckIfExistsAsync_Should_Return_True_When_Document_Exists()
        {
            var category = new Category(CategoryTypes.Income, ["CAT-003"])
            {
                Key = Guid.NewGuid(),
            };

            var collection = _database.GetCollection<CategoryDocument>(LedgerDbCollections.Categories);
            await collection.InsertOneAsync(
                new CategoryDocument
                {
                    Payload = category
                });

            var exists = await _client.CheckIfExistsAsync($"{CategoryTypes.Income.Key}-CAT-003");

            Assert.That(exists, Is.True);
        }

        [Test]
        public async Task GetByIdsAsync_Should_Return_Matching_Documents()
        {
            var cat1 = new Category(CategoryTypes.Income, ["K1"])
            {
                Key = Guid.NewGuid()
            };
            var cat2 = new Category(CategoryTypes.Income, ["K2"])
            {
                Key = Guid.NewGuid()
            };
            var cat3 = new Category(CategoryTypes.Income, ["K3"])
            {
                Key = Guid.NewGuid()
            };

            var collection = _database.GetCollection<CategoryDocument>(LedgerDbCollections.Categories);
            await collection.InsertManyAsync(
            [
                new CategoryDocument { Payload = cat1 },
                new CategoryDocument { Payload = cat2 },
                new CategoryDocument { Payload = cat3 }
            ]);

            var result = await _client.GetByIdsAsync([cat1.Key, cat3.Key]);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSucceeded, Is.True);
                Assert.That(result.Payload, Has.Count.EqualTo(2));
                Assert.That(result.Payload, Has.Some.Matches<CategoryDocument>(c => c.Payload.Key == cat1.Key));
                Assert.That(result.Payload, Has.Some.Matches<CategoryDocument>(c => c.Payload.Key == cat3.Key));
            });
        }
    }
}

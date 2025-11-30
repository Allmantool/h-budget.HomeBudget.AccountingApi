using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Categories.Clients;
using HomeBudget.Components.Categories.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Tests.Categories
{
    [TestFixture]
    public class CategoryDocumentsClientTests
    {
        private Mock<IMongoDatabase> _databaseMock;
        private Mock<IMongoCollection<CategoryDocument>> _collectionMock;
        private Mock<IAsyncCursor<CategoryDocument>> _cursorMock;
        private Mock<IAsyncCursor<BsonDocument>> _indexCursorMock;
        private Mock<IMongoIndexManager<CategoryDocument>> _indexManagerMock;
        private CategoryDocumentsClient _client;

        [SetUp]
        public void Setup()
        {
            _databaseMock = new Mock<IMongoDatabase>();
            _collectionMock = new Mock<IMongoCollection<CategoryDocument>>();
            _cursorMock = new Mock<IAsyncCursor<CategoryDocument>>();
            _indexCursorMock = new Mock<IAsyncCursor<BsonDocument>>();
            _indexManagerMock = new Mock<IMongoIndexManager<CategoryDocument>>();

            var mongoOptions = Options.Create(
                new MongoDbOptions
                {
                    ConnectionString = "mongodb://localhost:27017",
                    PaymentAccounts = "payment-accounts-tests",
                    PaymentsHistory = "payment-history-tests",
                    LedgerDatabase = "leger-history-tests",
                    HandBooks = "handbook-test"
                });

            // Mock GetCollection<T>()
            _databaseMock
                .Setup(db => db.GetCollection<CategoryDocument>(LedgerDbCollections.Categories, null))
                .Returns(_collectionMock.Object);

            _client = (CategoryDocumentsClient)Activator.CreateInstance(
                typeof(CategoryDocumentsClient),
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                [mongoOptions],
                null);

            typeof(CategoryDocumentsClient).BaseType!
                .GetProperty("MongoDatabase", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_client, _databaseMock.Object);

            // Setup indexes handler
            _collectionMock.SetupGet(c => c.Indexes).Returns(_indexManagerMock.Object);
        }

        private static void SetupCursor<T>(Mock<IAsyncCursor<T>> cursor, IEnumerable<T> items)
        {
            var enumerator = items.ToList();

            cursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>()))
                  .Returns(true)
                  .Returns(false);

            cursor.SetupGet(c => c.Current).Returns(enumerator);
        }

        [Test]
        public async Task GetAsync_Returns_All_Documents()
        {
            var docs = new[]
            {
                new CategoryDocument
                {
                    Payload = new Category(CategoryTypes.Expense, ["AAA"])
                },
                new CategoryDocument
                {
                    Payload = new Category(CategoryTypes.Expense, ["BBB"])
                }
            };

            SetupCursor(_cursorMock, docs);

            _collectionMock
                .Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<CategoryDocument>>(),
                    It.IsAny<FindOptions<CategoryDocument, CategoryDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(_cursorMock.Object);

            SetupCursor(_indexCursorMock, Array.Empty<BsonDocument>());
            _indexManagerMock.Setup(i => i.ListAsync(It.IsAny<CancellationToken>()))
                             .ReturnsAsync(_indexCursorMock.Object);

            var result = await _client.GetAsync();

            Assert.That(result.IsSucceeded, Is.True);
            Assert.That(result.Payload.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task GetByIdAsync_Returns_Matching_Document()
        {
            var id = Guid.NewGuid();
            var doc = new CategoryDocument
            {
                Payload = new Category(CategoryTypes.Expense, ["FOO"])
            };

            SetupCursor(_cursorMock, [doc]);

            _collectionMock
                .Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<CategoryDocument>>(),
                    It.IsAny<FindOptions<CategoryDocument, CategoryDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(_cursorMock.Object);

            SetupCursor(_indexCursorMock, Array.Empty<BsonDocument>());
            _indexManagerMock.Setup(i => i.ListAsync(It.IsAny<CancellationToken>()))
                             .ReturnsAsync(_indexCursorMock.Object);

            var result = await _client.GetByIdAsync(id);

            Assert.That(result.IsSucceeded, Is.True);
            Assert.That(result.Payload.Payload.Key, Is.EqualTo(id));
        }
    }
}

using System;

using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations.Tests
{
    [TestFixture]
    public class CategoryDocumentTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        }

        [Test]
        public void Should_Deserialize_Category_With_OperationUnixTime()
        {
            // Arrange
            var categoryType = CategoryTypes.Expense;
            var nameNodes = new[] { "Node1", "Node2" };
            var expectedKey = $"{categoryType.Id}-{string.Join(',', nameNodes)}";
            var expectedOperationUnixTime = DateTimeOffset.FromUnixTimeSeconds(126).ToUnixTimeMilliseconds();

            var bsonDocument = new BsonDocument
            {
                { "Key", BsonValue.Create("68a704a0-ff00-4b98-8c87-5488ded2684b") },
                { "NameNodes", new BsonArray(nameNodes) },
                { "CategoryKey", expectedKey },
                { "OperationUnixTime", expectedOperationUnixTime }
            };

            // Act
            var deserializedCategory = BsonSerializer.Deserialize<Category>(bsonDocument);

            Assert.Multiple(() =>
            {
                expectedKey.Should().BeEquivalentTo(deserializedCategory.CategoryKey);
                deserializedCategory.NameNodes.Should().NotBeNullOrEmpty();
                deserializedCategory.NameNodes.Should().Contain("Node1");

                deserializedCategory.OperationUnixTime.Should().Be(126000);
            });
        }
    }
}

using System;

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace HomeBudget.Accounting.Infrastructure
{
    public abstract class DocumentEntity<T>
        where T : class
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId Id { get; init; }

        public T Payload { get; init; }

        public string SourceSystem { get; init; }

        public string LegacyId { get; init; }

        public string ImportBatchId { get; init; }

        public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

        public DateTime UpdatedUtc { get; init; } = DateTime.UtcNow;
    }
}

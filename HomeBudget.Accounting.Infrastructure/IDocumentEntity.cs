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
    }
}

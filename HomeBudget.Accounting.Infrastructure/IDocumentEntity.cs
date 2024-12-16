using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace HomeBudget.Accounting.Infrastructure
{
    public abstract class DocumentEntity<T>
        where T : class
    {
        [BsonId]
        public ObjectId Id { get; init; }

        public T Payload { get; init; }
    }
}

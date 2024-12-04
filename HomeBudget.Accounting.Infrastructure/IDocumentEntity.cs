using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace HomeBudget.Accounting.Infrastructure
{
    public abstract class DocumentEntity<T>
        where T : class
    {
        [BsonId]
        public ObjectId Id { get; set; }

        public T Payload { get; set; }
    }
}

using System;

using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

using HomeBudget.Accounting.Domain.Enumerations;

namespace HomeBudget.Accounting.Infrastructure
{
    public class BaseEnumerationSerializer<TEnum, TValue> : SerializerBase<TEnum>
        where TEnum : BaseEnumeration<TEnum, TValue>
        where TValue : notnull, IComparable
    {
        public override TEnum Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            var value = BsonSerializer.Deserialize<TValue>(context.Reader);
            return BaseEnumeration<TEnum, TValue>.FromValue(value);
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, TEnum value)
        {
            BsonSerializer.Serialize(context.Writer, value.Key);
        }
    }
}

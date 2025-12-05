using System;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace HomeBudget.Test.Core.Serializers.MongoDb
{
    public sealed class DateOnlySerializer : StructSerializerBase<DateOnly>
    {
        public override void Serialize(
            BsonSerializationContext context,
            BsonSerializationArgs args,
            DateOnly value)
        {
            // Mongo expects only the VALUE to be written here, NOT the name.
            var date = value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            context.Writer.WriteDateTime(BsonUtils.ToMillisecondsSinceEpoch(date));
        }

        public override DateOnly Deserialize(
            BsonDeserializationContext context,
            BsonDeserializationArgs args)
        {
            var milliseconds = context.Reader.ReadDateTime();
            var date = BsonUtils.ToDateTimeFromMillisecondsSinceEpoch(milliseconds);
            return DateOnly.FromDateTime(date);
        }
    }
}

using System;

using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace HomeBudget.Test.Core.Serializers.MongoDb
{
    public class DateOnlySerializer : StructSerializerBase<DateOnly>
    {
        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, DateOnly value)
        {
            context.Writer.WriteDateTime(value.ToDateTime(TimeOnly.MinValue).ToUniversalTime().Ticks);
        }

        public override DateOnly Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            var ticks = context.Reader.ReadDateTime();
            return DateOnly.FromDateTime(new DateTime(ticks, DateTimeKind.Utc));
        }
    }
}

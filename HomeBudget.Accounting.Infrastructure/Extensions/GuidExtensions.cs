using System;

using MongoDB.Bson;

namespace HomeBudget.Accounting.Infrastructure.Extensions
{
    public static class GuidExtensions
    {
        public static ObjectId ToObjectId(this Guid guid)
        {
            var guidBytes = guid.ToByteArray();
            var objectIdBytes = new byte[12];

            Array.Copy(guidBytes, 0, objectIdBytes, 0, 12);

            return new ObjectId(objectIdBytes);
        }
    }
}

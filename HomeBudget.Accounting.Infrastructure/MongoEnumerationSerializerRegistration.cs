using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using MongoDB.Bson.Serialization;

using HomeBudget.Accounting.Domain.Enumerations;

namespace HomeBudget.Accounting.Infrastructure
{
    public static class MongoEnumerationSerializerRegistration
    {
        private static readonly HashSet<Type> ForbiddenTypes = new()
        {
            typeof(DateOnly),
            typeof(DateTime),
            typeof(Guid),
            typeof(string),
            typeof(int),
            typeof(long),
            typeof(decimal),
            typeof(float),
            typeof(double),
            typeof(bool)
        };

        public static void RegisterAllBaseEnumerations(Assembly assembly)
        {
            var enumTypes = assembly.GetTypes()
                .Where(t =>
                    t.BaseType != null &&
                    t.BaseType.IsGenericType &&
                    t.BaseType.GetGenericTypeDefinition() == typeof(BaseEnumeration<,>)
                )
                .ToList();

            foreach (var enumType in enumTypes)
            {
                var valueType = enumType.BaseType!.GetGenericArguments()[1];

                if (ForbiddenTypes.Contains(enumType) || ForbiddenTypes.Contains(valueType))
                {
                    continue;
                }

                if (BsonSerializer.LookupSerializer(enumType).GetType().Name != "ObjectSerializer")
                {
                    continue;
                }

                var serializerType = typeof(BaseEnumerationSerializer<,>).MakeGenericType(enumType, valueType);
                var serializerInstance = Activator.CreateInstance(serializerType);

                BsonSerializer.TryRegisterSerializer(enumType, (IBsonSerializer)serializerInstance);
            }
        }
    }
}

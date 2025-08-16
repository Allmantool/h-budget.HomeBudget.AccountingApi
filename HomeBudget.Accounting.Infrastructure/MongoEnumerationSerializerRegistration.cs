using System;
using System.Linq;
using System.Reflection;

using MongoDB.Bson.Serialization;

using HomeBudget.Accounting.Domain.Enumerations;

namespace HomeBudget.Accounting.Infrastructure
{
    public static class MongoEnumerationSerializerRegistration
    {
        public static void RegisterAllBaseEnumerations(Assembly assembly)
        {
            var enumTypes = assembly.GetTypes()
                .Where(t => t.BaseType != null &&
                            t.BaseType.IsGenericType &&
                            t.BaseType.GetGenericTypeDefinition() == typeof(BaseEnumeration<,>))
                .ToList();

            foreach (var enumType in enumTypes)
            {
                var genericArgs = enumType.BaseType.GetGenericArguments();
                var valueType = genericArgs[1];

                var serializerType = typeof(BaseEnumerationSerializer<,>)
                    .MakeGenericType(enumType, valueType);

                var serializerInstance = Activator.CreateInstance(serializerType);

                BsonSerializer.TryRegisterSerializer(enumType, (IBsonSerializer)serializerInstance);
            }
        }
    }
}

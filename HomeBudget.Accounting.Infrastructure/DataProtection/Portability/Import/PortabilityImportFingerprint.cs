using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    internal static class PortabilityImportFingerprint
    {
        public static string Entity(JsonElement value)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                WriteCanonical(writer, value);
            }

            return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
        }

        public static string Idempotency(Guid exportId, string path, string entityId)
        {
            var value = $"{exportId:D}:{path}:{entityId}";
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        }

        private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                    {
                        writer.WritePropertyName(property.Name);
                        WriteCanonical(writer, property.Value);
                    }

                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in value.EnumerateArray())
                    {
                        WriteCanonical(writer, item);
                    }

                    writer.WriteEndArray();
                    break;
                default:
                    value.WriteTo(writer);
                    break;
            }
        }
    }
}

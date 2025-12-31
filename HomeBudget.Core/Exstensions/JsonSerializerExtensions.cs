using System;
using System.Text.Json;

namespace HomeBudget.Core.Exstensions
{
    public static class JsonSerializerExtensions
    {
        public static T DeserializeOrNull<T>(
            this ReadOnlySpan<byte> json,
            JsonSerializerOptions options = null)
            where T : class
        {
            if (json.IsEmpty)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(json, options);
            }
            catch (JsonException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }
    }
}

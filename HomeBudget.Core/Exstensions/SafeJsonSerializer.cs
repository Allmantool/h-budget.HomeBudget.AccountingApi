using System;
using System.Text.Json;

namespace HomeBudget.Core.Exstensions
{
    public static class SafeJsonSerializer
    {
        public static T DeserializeOrNull<T>(
           ReadOnlySpan<byte> json,
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

        public static bool TryDeserialize<T>(
            ReadOnlySpan<byte> json,
            out T result,
            JsonSerializerOptions options = null)
        {
            try
            {
                result = JsonSerializer.Deserialize<T>(json, options);
                return result is not null;
            }
            catch (JsonException)
            {
                result = default;
                return false;
            }
            catch (NotSupportedException)
            {
                result = default;
                return false;
            }
        }
    }
}

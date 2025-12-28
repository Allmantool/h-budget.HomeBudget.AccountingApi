using System;
using System.Collections.Generic;

namespace HomeBudget.Core.Exceptions
{
    public static class CollectionExceptions
    {
        public static bool IsNullOrEmpty<T>(this IEnumerable<T> origin)
        {
            if (origin is null)
            {
                return true;
            }

            using var enumerator = origin.GetEnumerator();
            return !enumerator.MoveNext();
        }

        public static string Get(
            this IReadOnlyDictionary<string, string> metadata,
            string key)
        {
            if (metadata.IsNullOrEmpty())
            {
                return null;
            }

            return metadata.TryGetValue(key, out var value)
                ? value
                : null;
        }

        public static string GetRequired(
            this IReadOnlyDictionary<string, string> metadata,
            string key,
            string context)
        {
            if (metadata.IsNullOrEmpty())
            {
                return null;
            }

            if (metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            throw new InvalidOperationException($"Required metadata '{key}' is missing ({context})");
        }

        public static string GetOrCreate(
            this IDictionary<string, string> metadata,
            string key,
            Func<string> factory)
        {
            if (metadata.IsNullOrEmpty() || factory is null)
            {
                return null;
            }

            if (!metadata.TryGetValue(key, out var value))
            {
                value = factory();
                metadata[key] = value;
            }

            return value;
        }
    }
}

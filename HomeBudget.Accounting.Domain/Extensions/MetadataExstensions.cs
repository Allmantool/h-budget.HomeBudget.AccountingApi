using System;
using System.Collections.Generic;

using HomeBudget.Core.Exstensions;

namespace HomeBudget.Accounting.Domain.Extensions
{
    public static class MetadataExstensions
    {
        public static string Get(
            this IReadOnlyDictionary<string, string> metadata,
            string key)
        {
            if (metadata.IsNullOrEmpty())
            {
                return string.Empty;
            }

            return metadata.TryGetValue(key, out var value)
                ? value
                : string.Empty;
        }

        public static string GetRequired(
            this IReadOnlyDictionary<string, string> metadata,
            string key,
            string context)
        {
            if (metadata.IsNullOrEmpty())
            {
                return string.Empty;
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


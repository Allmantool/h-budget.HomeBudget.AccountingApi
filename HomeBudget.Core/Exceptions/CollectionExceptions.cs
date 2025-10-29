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
    }
}

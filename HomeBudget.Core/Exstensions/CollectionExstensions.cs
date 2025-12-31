using System.Collections.Generic;

namespace HomeBudget.Core.Exstensions
{
    public static class CollectionExstensions
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

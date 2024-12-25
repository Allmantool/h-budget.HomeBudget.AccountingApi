using System.Collections.Generic;
using System.Linq;

namespace HomeBudget.Core.Exceptions
{
    public static class CollectionExceptions
    {
        public static bool IsNullOrEmpty<T>(this IEnumerable<T> origin)
        {
            return origin == null || !origin.Any();
        }
    }
}

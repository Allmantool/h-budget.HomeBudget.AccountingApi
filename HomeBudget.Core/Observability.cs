using System.Diagnostics;

namespace HomeBudget.Core
{
    public static class Observability
    {
        public static readonly string ActivitySourceName = "HomeBudget.Accounting";
        public static readonly ActivitySource ActivitySource =
            new(ActivitySourceName);
    }
}

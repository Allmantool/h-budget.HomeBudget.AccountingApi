using System.Diagnostics;

namespace HomeBudget.Accounting.Infrastructure.Logs
{
    internal static class Tracing
    {
        public static readonly ActivitySource Source = new("HomeBudget.Accounting");
    }
}

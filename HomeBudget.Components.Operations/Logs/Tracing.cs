using System.Diagnostics;

namespace HomeBudget.Components.Operations.Logs
{
    internal static class Tracing
    {
        public static readonly ActivitySource Source = new("HomeBudget.Accounting");
    }
}

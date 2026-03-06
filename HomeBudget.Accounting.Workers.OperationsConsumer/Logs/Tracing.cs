using System.Diagnostics;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Logs
{
    internal static class Tracing
    {
        public static readonly ActivitySource Source = new("HomeBudget.Accounting");
    }
}

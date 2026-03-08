using System.Diagnostics;

namespace HomeBudget.Core.Observability;

public static class Telemetry
{
    internal const string ServiceName = "HomeBudget.Accounting";

    public static readonly ActivitySource ActivitySource = new(ServiceName, "1.0.0");
}
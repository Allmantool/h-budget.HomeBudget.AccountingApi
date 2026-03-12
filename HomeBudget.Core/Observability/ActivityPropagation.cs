using System.Diagnostics;

namespace HomeBudget.Core.Observability;

public static class ActivityPropagation
{
    public static Activity StartActivity(
        string name,
        ActivityKind kind,
        string traceParent = null,
        string traceState = null)
    {
        if (Activity.Current != null)
        {
            return Telemetry.ActivitySource.StartActivity(name, kind);
        }

        if (TryParseContext(traceParent, traceState, out var parentContext))
        {
            return Telemetry.ActivitySource.StartActivity(name, kind, parentContext);
        }

        return Telemetry.ActivitySource.StartActivity(name, kind);
    }

    public static bool TryParseContext(
        string traceParent,
        string traceState,
        out ActivityContext activityContext)
    {
        if (!string.IsNullOrWhiteSpace(traceParent) &&
            ActivityContext.TryParse(traceParent, traceState, out activityContext))
        {
            return true;
        }

        activityContext = default;
        return false;
    }
}

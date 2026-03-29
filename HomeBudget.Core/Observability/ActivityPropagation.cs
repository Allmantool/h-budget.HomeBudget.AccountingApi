using System.Collections.Generic;
using System.Diagnostics;

using OpenTelemetry.Context.Propagation;

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

    public static Activity StartActivity(
        string name,
        ActivityKind kind,
        ActivityContext parentContext,
        IEnumerable<ActivityLink> links = null)
    {
        if (Activity.Current != null)
        {
            return Telemetry.ActivitySource.StartActivity(
                name,
                kind,
                Activity.Current.Context,
                links: links);
        }

        if (parentContext != default)
        {
            return Telemetry.ActivitySource.StartActivity(
                name,
                kind,
                parentContext,
                links: links);
        }

        return Telemetry.ActivitySource.StartActivity(
            name,
            kind,
            default(ActivityContext),
            links: links);
    }

    public static Activity StartActivity(
        string name,
        ActivityKind kind,
        PropagationContext propagationContext,
        IEnumerable<ActivityLink> links = null)
        => StartActivity(name, kind, propagationContext.ActivityContext, links);

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

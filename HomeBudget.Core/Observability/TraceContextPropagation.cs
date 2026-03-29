using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace HomeBudget.Core.Observability;

public static class TraceContextPropagation
{
    public static readonly string TraceParent = "traceparent";
    public static readonly string TraceState = "tracestate";
    public static readonly string Baggage = "baggage";

    private static readonly CompositeTextMapPropagator Propagator = new CompositeTextMapPropagator(
    [
        new TraceContextPropagator(),
        new BaggagePropagator()
    ]);

    public static IReadOnlyDictionary<string, string> Capture(Activity activity = null)
    {
        var currentActivity = activity ?? Activity.Current;
        var propagationContext = new PropagationContext(currentActivity?.Context ?? default, OpenTelemetry.Baggage.Current);
        var carrier = new Dictionary<string, string>(3);

        Propagator.Inject(
            propagationContext,
            carrier,
            static (headers, key, value) =>
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    headers[key] = value;
                }
            });

        return carrier;
    }

    public static PropagationContext Extract(IReadOnlyDictionary<string, string> carrier)
    {
        if (carrier is null || carrier.Count == 0)
        {
            return default;
        }

        return Propagator.Extract(
            default,
            carrier,
            static (headers, key) =>
            {
                if (headers.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return [value];
                }

                return [];
            });
    }

    public static IReadOnlyDictionary<string, string> BuildCarrier(
        string traceParent,
        string traceState = null,
        string baggage = null)
    {
        var carrier = new Dictionary<string, string>(3);

        if (!string.IsNullOrWhiteSpace(traceParent))
        {
            carrier[TraceParent] = traceParent;
        }

        if (!string.IsNullOrWhiteSpace(traceState))
        {
            carrier[TraceState] = traceState;
        }

        if (!string.IsNullOrWhiteSpace(baggage))
        {
            carrier[Baggage] = baggage;
        }

        return carrier;
    }

    public static IEnumerable<ActivityLink> CreateLinks(IEnumerable<IReadOnlyDictionary<string, string>> carriers)
    {
        if (carriers is null)
        {
            return [];
        }

        return carriers
            .Select(Extract)
            .Select(pc => pc.ActivityContext)
            .Where(context => context != default)
            .Distinct()
            .Select(context => new ActivityLink(context))
            .ToArray();
    }

    public static ActivityContext[] ExtractContexts(IEnumerable<IReadOnlyDictionary<string, string>> carriers)
    {
        if (carriers is null)
        {
            return [];
        }

        return carriers
            .Select(Extract)
            .Select(pc => pc.ActivityContext)
            .Where(context => context != default)
            .Distinct()
            .ToArray();
    }

    public static (ActivityContext ParentContext, ActivityLink[] Links) ResolveParentAndLinks(
        IEnumerable<IReadOnlyDictionary<string, string>> carriers)
        => ResolveParentAndLinks(ExtractContexts(carriers));

    public static (ActivityContext ParentContext, ActivityLink[] Links) ResolveParentAndLinks(
        IEnumerable<ActivityContext> contexts)
    {
        if (contexts is null)
        {
            return (default, []);
        }

        var orderedContexts = contexts
            .Where(context => context != default)
            .Distinct()
            .ToArray();

        if (orderedContexts.Length == 0)
        {
            return (default, []);
        }

        var parentContext = orderedContexts[0];
        var links = orderedContexts
            .Skip(1)
            .Select(context => new ActivityLink(context))
            .ToArray();

        return (parentContext, links);
    }

    public static BaggageScope UseExtractedBaggage(PropagationContext propagationContext)
    {
        var previous = OpenTelemetry.Baggage.Current;
        OpenTelemetry.Baggage.Current = propagationContext.Baggage;

        return new BaggageScope(previous);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace HomeBudget.Core.Observability;

public sealed class ActivityEnvelope<T>
{
    public ActivityEnvelope(T item, IReadOnlyDictionary<string, string> propagationCarrier)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        PropagationCarrier = propagationCarrier ?? throw new ArgumentNullException(nameof(propagationCarrier));
    }

    public T Item { get; }

    public IReadOnlyDictionary<string, string> PropagationCarrier { get; }

    public static ActivityEnvelope<T> Capture(T item, Activity activity = null)
    {
        var carrier = new Dictionary<string, string>(TraceContextPropagation.Capture(activity));
        return new ActivityEnvelope<T>(item, carrier);
    }
}

using System.Collections.Generic;
using System.Diagnostics;

using FluentAssertions;
using NUnit.Framework;
using OpenTelemetry;

using HomeBudget.Core.Observability;

namespace HomeBudget.Components.Operations.Tests.Observability
{
    [TestFixture]
    public class TraceContextPropagationTests
    {
        [Test]
        public void CaptureAndExtract_WhenActivityAndBaggagePresent_ThenPreservesW3CContext()
        {
            var previousBaggage = OpenTelemetry.Baggage.Current;
            using var activitySource = new ActivitySource("TraceContextPropagationTests");
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == activitySource.Name,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
            };

            ActivitySource.AddActivityListener(listener);

            try
            {
                using var root = activitySource.StartActivity("root", ActivityKind.Internal);
                root.Should().NotBeNull();
                OpenTelemetry.Baggage.Current = previousBaggage.SetBaggage("correlation.id", "corr-42");

                var carrier = TraceContextPropagation.Capture(root);
                var extracted = TraceContextPropagation.Extract(new Dictionary<string, string>(carrier));

                carrier.Should().ContainKey(TraceContextPropagation.TraceParent);
                extracted.ActivityContext.TraceId.Should().Be(root!.TraceId);
                extracted.ActivityContext.SpanId.Should().Be(root.SpanId);
                extracted.Baggage.GetBaggage("correlation.id").Should().Be("corr-42");
            }
            finally
            {
                OpenTelemetry.Baggage.Current = previousBaggage;
            }
        }
    }
}

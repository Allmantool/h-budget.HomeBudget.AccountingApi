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

        [Test]
        public void ResolveParentAndLinks_WhenMultipleCarriersProvided_ThenUsesFirstContextAsParentAndRestAsLinks()
        {
            using var activitySource = new ActivitySource("TraceContextPropagationTests.Resolve");
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == activitySource.Name,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
            };

            ActivitySource.AddActivityListener(listener);

            using var first = activitySource.StartActivity("first", ActivityKind.Internal);
            using var second = activitySource.StartActivity("second", ActivityKind.Internal);

            var firstCarrier = TraceContextPropagation.Capture(first);
            var secondCarrier = TraceContextPropagation.Capture(second);

            var (parentContext, links) = TraceContextPropagation.ResolveParentAndLinks(
                new[]
                {
                    new Dictionary<string, string>(firstCarrier),
                    new Dictionary<string, string>(secondCarrier)
                });

            parentContext.TraceId.Should().Be(first!.TraceId);
            parentContext.SpanId.Should().Be(first.SpanId);
            links.Should().HaveCount(1);
            links[0].Context.TraceId.Should().Be(second!.TraceId);
            links[0].Context.SpanId.Should().Be(second.SpanId);
        }
    }
}

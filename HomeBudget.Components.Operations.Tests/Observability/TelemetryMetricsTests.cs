using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;

using FluentAssertions;
using NUnit.Framework;

using HomeBudget.Core.Observability;

namespace HomeBudget.Components.Operations.Tests.Observability
{
    [TestFixture]
    public class TelemetryMetricsTests
    {
        [Test]
        public void OutboxGaugeSnapshot_ShouldExposeLatestValues()
        {
            TelemetryMetrics.SetOutboxStats(7, 2, 600);

            TelemetryMetrics.GetOutboxPendingCount().Should().Be(7);
            TelemetryMetrics.GetOutboxFailedDeadLetterCount().Should().Be(2);
            TelemetryMetrics.GetOutboxOldestPendingAgeSeconds().Should().Be(600);
        }

        [Test]
        public void ProjectionLagGauge_ShouldClampNegativeLagToZero()
        {
            TelemetryMetrics.SetProjectionLagSeconds(-10);

            TelemetryMetrics.GetProjectionLagSeconds().Should().Be(0);
        }

        [Test]
        public void ProjectionFailuresCounter_ShouldEmitMeasurements()
        {
            var measurements = new List<long>();

            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == TelemetryMetrics.Meter.Name
                    && instrument.Name == "homebudget.projection.failures")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => measurements.Add(measurement));
            listener.Start();

            TelemetryMetrics.ProjectionFailures.Add(1, [new("projection_name", "sync_operations_history")]);

            measurements.Sum().Should().Be(1);
        }
    }
}

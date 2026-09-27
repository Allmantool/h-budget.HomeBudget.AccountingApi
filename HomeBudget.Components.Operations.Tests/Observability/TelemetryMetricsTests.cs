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

        [Test]
        public void WorkerPipelineCounters_ShouldEmitOnlyBoundedStageLabels()
        {
            var measurements = new List<(string Instrument, long Value, string Outcome)>();

            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == TelemetryMetrics.Meter.Name
                    && instrument.Name is "homebudget.kafka.messages.received"
                        or "homebudget.kafka.processing.attempts"
                        or "homebudget.eventstore.write.outcomes"
                        or "homebudget.projection.events.projected")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            {
                var outcome = tags.ToArray()
                    .SingleOrDefault(tag => tag.Key == "outcome")
                    .Value?.ToString();
                measurements.Add((instrument.Name, measurement, outcome));
            });
            listener.Start();

            TelemetryMetrics.KafkaMessagesReceived.Add(1);
            TelemetryMetrics.KafkaProcessingAttempts.Add(1, [new("outcome", "persisted")]);
            TelemetryMetrics.EventStoreWriteOutcomes.Add(1, [new("outcome", "appended")]);
            TelemetryMetrics.ProjectionEventsProjected.Add(
                2,
                [new("projection_name", "sync_operations_history")]);

            measurements.Should().HaveCount(4);
            measurements.Should().OnlyContain(measurement =>
                measurement.Outcome == null
                || measurement.Outcome == "persisted"
                || measurement.Outcome == "appended");
        }

        [TestCase("Added_22222222-2222-2222-2222-222222222222", "added")]
        [TestCase("Removed_22222222-2222-2222-2222-222222222222", "removed")]
        [TestCase("Updated", "updated")]
        [TestCase("unexpected-cardinality-value", "unknown")]
        [TestCase(null, "unknown")]
        public void NormalizePaymentEventType_ShouldReturnOnlyBoundedValues(string eventType, string expected)
        {
            TelemetryMetrics.NormalizePaymentEventType(eventType).Should().Be(expected);
        }
    }
}

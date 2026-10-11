#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;

using FluentAssertions;

using HomeBudget.Accounting.Infrastructure.DataProtection.Telemetry;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.Telemetry
{
    [TestFixture]
    [NonParallelizable]
    internal sealed class DataProtectionTelemetryTests
    {
        private readonly List<MeterListener> _listeners = [];

        [SetUp]
        public void SetUp()
        {
            DataProtectionTelemetry.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var listener in _listeners)
            {
                listener.Dispose();
            }

            _listeners.Clear();
        }

        [Test]
        public void RecordRecoverySetMilestones_UsesOnlyBoundedLifecycleLabels()
        {
            var measurements = ListenForLongMeasurements();

            DataProtectionTelemetry.RecordRecoverySetCreated();
            var occurredUtc = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
            DataProtectionTelemetry.RecordIntegrityVerified(occurredUtc);
            DataProtectionTelemetry.RecordIndependentlyProtected(occurredUtc);
            DataProtectionTelemetry.RecordRestoreDrilled(occurredUtc);

            measurements
                .Where(measurement => measurement.Name == "homebudget.dataprotection.recovery_sets")
                .Select(measurement => measurement.Tags.Single(tag => tag.Key == "state").Value)
                .Should()
                .Equal("created", "integrity_verified", "independently_protected", "restore_drilled");
        }

        [Test]
        public void RecordProtectionFailure_RejectsAnUnboundedStage()
        {
            var act = () => DataProtectionTelemetry.RecordProtectionFailure((ProtectionFailureStage)999);

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void RecordProtectionFailures_UsesOnlyBoundedStageLabels()
        {
            var measurements = ListenForLongMeasurements();

            DataProtectionTelemetry.RecordProtectionFailure(ProtectionFailureStage.Capture);
            DataProtectionTelemetry.RecordProtectionFailure(ProtectionFailureStage.Copy);
            DataProtectionTelemetry.RecordProtectionFailure(ProtectionFailureStage.IntegrityVerification);

            measurements
                .Where(measurement => measurement.Name == "homebudget.dataprotection.failures")
                .Select(measurement => measurement.Tags.Single(tag => tag.Key == "stage").Value)
                .Should()
                .Equal("capture", "copy", "integrity_verification");
        }

        [Test]
        public void RecordOperationFailures_UsesSeparateUnlabelledCounters()
        {
            var measurements = ListenForLongMeasurements();

            DataProtectionTelemetry.RecordOperationFailure();
            DataProtectionTelemetry.RecordReconciliationFailure();

            measurements
                .Where(measurement => measurement.Name.StartsWith(
                    "homebudget.dataprotection.",
                    StringComparison.Ordinal))
                .Select(measurement => measurement.Name)
                .Should()
                .ContainInOrder(
                    "homebudget.dataprotection.operation.failures",
                    "homebudget.dataprotection.reconciliation.failures");
            measurements.Should().OnlyContain(measurement => measurement.Tags.Length == 0);
        }

        [Test]
        public void Snapshot_UsesWarningAndCriticalProtectedCutoffDefaults()
        {
            var now = new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
            DataProtectionTelemetry.SetCapabilityReady(true);

            DataProtectionTelemetry.SetProtectedCutoff(now.AddMinutes(-44).AddSeconds(-59));
            DataProtectionTelemetry.GetSnapshot(now).ProtectedCutoffStatus.Should().Be(ProtectedCutoffStatus.Healthy);

            DataProtectionTelemetry.SetProtectedCutoff(now.AddMinutes(-45));
            DataProtectionTelemetry.GetSnapshot(now).ProtectedCutoffStatus.Should().Be(ProtectedCutoffStatus.Warning);

            DataProtectionTelemetry.SetProtectedCutoff(now.AddMinutes(-60));
            DataProtectionTelemetry.GetSnapshot(now).ProtectedCutoffStatus.Should().Be(ProtectedCutoffStatus.Critical);
        }

        [Test]
        public void LocalCaptureCreation_DoesNotAdvanceProtectedCutoffOrReadiness()
        {
            var now = new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
            DataProtectionTelemetry.SetCapabilityReady(true);
            DataProtectionTelemetry.SetProtectedCutoff(now.AddMinutes(-59));
            DataProtectionTelemetry.RecordRecoverySetCreated();

            var snapshot = DataProtectionTelemetry.GetSnapshot(now.AddMinutes(2));

            snapshot.ProtectedCutoffStatus.Should().Be(ProtectedCutoffStatus.Critical);
            snapshot.ProtectedCutoffAge.Should().Be(TimeSpan.FromMinutes(61));
            snapshot.IsIndependentlyProtected.Should().BeFalse();
        }

        [Test]
        public void ObservableMetrics_ExposeOnlySafeBoundedOperationalState()
        {
            using var listener = new MeterListener();
            var measurements = new Dictionary<string, double>(StringComparer.Ordinal);
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == DataProtectionTelemetry.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => measurements[instrument.Name] = value);
            listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => measurements[instrument.Name] = value);
            listener.Start();

            var now = DateTimeOffset.UtcNow;
            DataProtectionTelemetry.SetCapabilityReady(true);
            DataProtectionTelemetry.SetProtectedCutoff(now.AddMinutes(-10));
            DataProtectionTelemetry.SetIntegrityVerification(now.AddMinutes(-9));
            DataProtectionTelemetry.SetCapacityHeadroomRatio(0.42);
            DataProtectionTelemetry.SetAcceptedPendingWork(7);
            listener.RecordObservableInstruments();

            measurements.Keys.Should().Contain([
                "homebudget.dataprotection.capability.ready",
                "homebudget.dataprotection.protected_cutoff.age",
                "homebudget.dataprotection.integrity_verification.age",
                "homebudget.dataprotection.restore_drill.age",
                "homebudget.dataprotection.capacity.headroom_ratio",
                "homebudget.dataprotection.accepted_pending_work"]);
            measurements["homebudget.dataprotection.capability.ready"].Should().Be(1);
            measurements["homebudget.dataprotection.capacity.headroom_ratio"].Should().Be(0.42);
            measurements["homebudget.dataprotection.accepted_pending_work"].Should().Be(7);
            measurements.Values.Should().OnlyContain(value => !double.IsNaN(value) && !double.IsInfinity(value));
        }

        private List<MetricMeasurement> ListenForLongMeasurements()
        {
            var measurements = new List<MetricMeasurement>();
            var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == DataProtectionTelemetry.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                measurements.Add(new MetricMeasurement(instrument.Name, value, tags.ToArray())));
            listener.Start();
            _listeners.Add(listener);
            return measurements;
        }

        private sealed record MetricMeasurement(
            string Name,
            long Value,
            KeyValuePair<string, object?>[] Tags);
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Telemetry
{
    internal enum ProtectionFailureStage
    {
        Capture,
        Copy,
        IntegrityVerification
    }

    internal enum ProtectedCutoffStatus
    {
        Blocked,
        Unknown,
        Healthy,
        Warning,
        Critical
    }

    internal sealed record DataProtectionTelemetrySnapshot(
        bool CapabilityReady,
        bool IsIndependentlyProtected,
        TimeSpan? ProtectedCutoffAge,
        ProtectedCutoffStatus ProtectedCutoffStatus);

    internal static class DataProtectionTelemetry
    {
        internal const string MeterName = "HomeBudget.DataProtection";
        internal static readonly TimeSpan ProtectedCutoffWarningAge = TimeSpan.FromMinutes(45);
        internal static readonly TimeSpan ProtectedCutoffCriticalAge = TimeSpan.FromMinutes(60);

        private static readonly Meter Meter = new(MeterName, "1.0.0");
        private static readonly Counter<long> RecoverySets = Meter.CreateCounter<long>(
            "homebudget.dataprotection.recovery_sets",
            "{recovery_sets}",
            "Recovery-set lifecycle transitions by bounded state.");
        private static readonly Counter<long> ProtectionFailures = Meter.CreateCounter<long>(
            "homebudget.dataprotection.failures",
            "{failures}",
            "Recovery protection failures by bounded pipeline stage.");
        private static readonly Counter<long> OperationFailures = Meter.CreateCounter<long>(
            "homebudget.dataprotection.operation.failures",
            "{failures}",
            "Durable data-protection operations that failed.");
        private static readonly Counter<long> ReconciliationFailures = Meter.CreateCounter<long>(
            "homebudget.dataprotection.reconciliation.failures",
            "{failures}",
            "Data-protection operations that failed during reconciliation.");

        private static long _capabilityReady;
        private static long _independentlyProtected;
        private static long _protectedCutoffUtcTicks;
        private static long _integrityVerificationUtcTicks;
        private static long _restoreDrillUtcTicks;
        private static long _capacityHeadroomRatioBits;
        private static long _acceptedPendingWork;

        static DataProtectionTelemetry()
        {
            Meter.CreateObservableGauge(
                "homebudget.dataprotection.capability.ready",
                () => Volatile.Read(ref _capabilityReady),
                "{state}",
                "Whether independently protected recovery is configured and validated.");
            Meter.CreateObservableGauge(
                "homebudget.dataprotection.protected_cutoff.available",
                () => Volatile.Read(ref _protectedCutoffUtcTicks) == 0 ? 0 : 1,
                "{state}",
                "Whether a verified independently protected cutoff has been observed.");
            Meter.CreateObservableGauge(
                "homebudget.dataprotection.protected_cutoff.age",
                () => ObserveAgeSeconds(Volatile.Read(ref _protectedCutoffUtcTicks)),
                "s",
                "Age of the latest verified independently protected common cutoff; -1 means unavailable.");
            Meter.CreateObservableGauge(
                "homebudget.dataprotection.integrity_verification.available",
                () => Volatile.Read(ref _integrityVerificationUtcTicks) == 0 ? 0 : 1,
                "{state}",
                "Whether a successful integrity verification has been observed.");
            Meter.CreateObservableGauge(
                "homebudget.dataprotection.integrity_verification.age",
                () => ObserveAgeSeconds(Volatile.Read(ref _integrityVerificationUtcTicks)),
                "s",
                "Age of the latest successful integrity verification; -1 means unavailable.");
            Meter.CreateObservableGauge(
                "homebudget.dataprotection.restore_drill.available",
                () => Volatile.Read(ref _restoreDrillUtcTicks) == 0 ? 0 : 1,
                "{state}",
                "Whether a successful isolated restore drill has been observed.");
            Meter.CreateObservableGauge(
                "homebudget.dataprotection.restore_drill.age",
                () => ObserveAgeSeconds(Volatile.Read(ref _restoreDrillUtcTicks)),
                "s",
                "Age of the latest successful isolated restore drill; -1 means unavailable.");
            Meter.CreateObservableGauge(
                "homebudget.dataprotection.capacity.headroom_ratio",
                () => BitConverter.Int64BitsToDouble(Volatile.Read(ref _capacityHeadroomRatioBits)),
                "1",
                "Bounded recovery-storage capacity headroom ratio from zero through one.");
            Meter.CreateObservableGauge(
                "homebudget.dataprotection.accepted_pending_work",
                () => Volatile.Read(ref _acceptedPendingWork),
                "{commands}",
                "Durably accepted pending commands included in or awaiting a recovery set.");
        }

        internal static void RecordRecoverySetCreated()
        {
            RecordRecoverySet("created");
        }

        internal static void RecordIntegrityVerified(DateTimeOffset verifiedUtc)
        {
            Interlocked.Exchange(ref _integrityVerificationUtcTicks, verifiedUtc.UtcTicks);
            RecordRecoverySet("integrity_verified");
        }

        internal static void RecordIndependentlyProtected(DateTimeOffset protectedCutoffUtc)
        {
            Interlocked.Exchange(ref _protectedCutoffUtcTicks, protectedCutoffUtc.UtcTicks);
            Interlocked.Exchange(ref _independentlyProtected, 1);
            RecordRecoverySet("independently_protected");
        }

        internal static void RecordRestoreDrilled(DateTimeOffset drilledUtc)
        {
            Interlocked.Exchange(ref _restoreDrillUtcTicks, drilledUtc.UtcTicks);
            RecordRecoverySet("restore_drilled");
        }

        internal static void RecordProtectionFailure(ProtectionFailureStage stage)
        {
            var label = stage switch
            {
                ProtectionFailureStage.Capture => "capture",
                ProtectionFailureStage.Copy => "copy",
                ProtectionFailureStage.IntegrityVerification => "integrity_verification",
                _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown protection failure stage.")
            };
            ProtectionFailures.Add(1, new KeyValuePair<string, object?>("stage", label));
        }

        internal static void RecordOperationFailure()
        {
            OperationFailures.Add(1);
        }

        internal static void RecordReconciliationFailure()
        {
            ReconciliationFailures.Add(1);
        }

        internal static void SetCapabilityReady(bool ready)
        {
            Interlocked.Exchange(ref _capabilityReady, ready ? 1 : 0);
        }

        internal static void SetProtectedCutoff(DateTimeOffset cutoffUtc)
        {
            Interlocked.Exchange(ref _protectedCutoffUtcTicks, cutoffUtc.UtcTicks);
        }

        internal static void SetIntegrityVerification(DateTimeOffset verifiedUtc)
        {
            Interlocked.Exchange(ref _integrityVerificationUtcTicks, verifiedUtc.UtcTicks);
        }

        internal static void SetCapacityHeadroomRatio(double ratio)
        {
            if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio is < 0 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(ratio), ratio, "Capacity headroom must be between zero and one.");
            }

            Interlocked.Exchange(ref _capacityHeadroomRatioBits, BitConverter.DoubleToInt64Bits(ratio));
        }

        internal static void SetAcceptedPendingWork(long count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            Interlocked.Exchange(ref _acceptedPendingWork, count);
        }

        internal static DataProtectionTelemetrySnapshot GetSnapshot(DateTimeOffset nowUtc)
        {
            var ready = Volatile.Read(ref _capabilityReady) == 1;
            var cutoffTicks = Volatile.Read(ref _protectedCutoffUtcTicks);
            TimeSpan? age = cutoffTicks == 0 ? null : CalculateAge(nowUtc, cutoffTicks);
            var status = !ready
                ? ProtectedCutoffStatus.Blocked
                : age is null
                    ? ProtectedCutoffStatus.Unknown
                    : age >= ProtectedCutoffCriticalAge
                        ? ProtectedCutoffStatus.Critical
                        : age >= ProtectedCutoffWarningAge
                            ? ProtectedCutoffStatus.Warning
                            : ProtectedCutoffStatus.Healthy;
            return new DataProtectionTelemetrySnapshot(
                ready,
                Volatile.Read(ref _independentlyProtected) == 1,
                age,
                status);
        }

        internal static void ResetForTests()
        {
            Interlocked.Exchange(ref _capabilityReady, 0);
            Interlocked.Exchange(ref _independentlyProtected, 0);
            Interlocked.Exchange(ref _protectedCutoffUtcTicks, 0);
            Interlocked.Exchange(ref _integrityVerificationUtcTicks, 0);
            Interlocked.Exchange(ref _restoreDrillUtcTicks, 0);
            Interlocked.Exchange(ref _capacityHeadroomRatioBits, 0);
            Interlocked.Exchange(ref _acceptedPendingWork, 0);
        }

        private static void RecordRecoverySet(string state)
        {
            RecoverySets.Add(1, new KeyValuePair<string, object?>("state", state));
        }

        private static long ObserveAgeSeconds(long utcTicks)
        {
            return utcTicks == 0
                ? -1
                : checked((long)CalculateAge(DateTimeOffset.UtcNow, utcTicks).TotalSeconds);
        }

        private static TimeSpan CalculateAge(DateTimeOffset nowUtc, long utcTicks)
        {
            var age = nowUtc.ToUniversalTime() - new DateTimeOffset(utcTicks, TimeSpan.Zero);
            return age < TimeSpan.Zero ? TimeSpan.Zero : age;
        }
    }
}

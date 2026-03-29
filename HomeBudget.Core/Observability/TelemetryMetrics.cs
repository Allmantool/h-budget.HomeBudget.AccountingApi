using System.Diagnostics.Metrics;

namespace HomeBudget.Core.Observability;

public static class TelemetryMetrics
{
    public static readonly Meter Meter = new($"{Telemetry.ActivitySource.Name}.Metrics", "1.0.0");

    public static readonly Histogram<double> OutboxWriteDurationMs = Meter.CreateHistogram<double>(
        "homebudget.outbox.write.duration",
        "ms");

    public static readonly Counter<long> OutboxStatusTransitions = Meter.CreateCounter<long>(
        "homebudget.outbox.status.transitions");

    public static readonly Histogram<double> EventStoreWriteDurationMs = Meter.CreateHistogram<double>(
        "homebudget.eventstore.write.duration",
        "ms");

    public static readonly Histogram<double> EventStoreConsumeDurationMs = Meter.CreateHistogram<double>(
        "homebudget.eventstore.consume.duration",
        "ms");

    public static readonly Counter<long> EventStoreRetries = Meter.CreateCounter<long>(
        "homebudget.eventstore.retries");

    public static readonly Histogram<double> ProjectionSyncDurationMs = Meter.CreateHistogram<double>(
        "homebudget.projection.sync.duration",
        "ms");

    public static readonly Histogram<double> ProjectionDelayMs = Meter.CreateHistogram<double>(
        "homebudget.projection.delay",
        "ms");

    public static readonly Histogram<double> MongoCrudDurationMs = Meter.CreateHistogram<double>(
        "homebudget.mongodb.crud.duration",
        "ms");

    public static readonly Counter<long> EventStoreDeadLettered = Meter.CreateCounter<long>(
        "homebudget.eventstore.deadlettered");
}

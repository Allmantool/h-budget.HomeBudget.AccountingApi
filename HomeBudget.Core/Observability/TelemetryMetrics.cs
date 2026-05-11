using System.Diagnostics.Metrics;
using System.Threading;

namespace HomeBudget.Core.Observability;

public static class TelemetryMetrics
{
    public static readonly Meter Meter = new($"{Telemetry.ActivitySource.Name}.Metrics", "1.0.0");

    private static long _outboxPendingCount;
    private static long _outboxFailedDeadLetterCount;
    private static long _outboxOldestPendingAgeSeconds;
    private static long _kafkaConsumerLag;
    private static long _eventStoreDeadLetterCount;
    private static long _projectionLagSeconds;

    static TelemetryMetrics()
    {
        Meter.CreateObservableGauge(
            "homebudget.outbox.pending.count",
            () => Volatile.Read(ref _outboxPendingCount),
            "{rows}",
            "Payment outbox rows waiting to be published.");

        Meter.CreateObservableGauge(
            "homebudget.outbox.failed_deadletter.count",
            () => Volatile.Read(ref _outboxFailedDeadLetterCount),
            "{rows}",
            "Payment outbox rows in failed or dead-letter status.");

        Meter.CreateObservableGauge(
            "homebudget.outbox.oldest_pending.age",
            () => Volatile.Read(ref _outboxOldestPendingAgeSeconds),
            "s",
            "Age of the oldest pending payment outbox row.");

        Meter.CreateObservableGauge(
            "homebudget.kafka.consumer.lag",
            () => Volatile.Read(ref _kafkaConsumerLag),
            "{messages}",
            "Latest observed Kafka consumer lag for the payment pipeline.");

        Meter.CreateObservableGauge(
            "homebudget.eventstore.dlq.count",
            () => Volatile.Read(ref _eventStoreDeadLetterCount),
            "{events}",
            "Dead-letter events written by this service instance.");

        Meter.CreateObservableGauge(
            "homebudget.projection.lag",
            () => Volatile.Read(ref _projectionLagSeconds),
            "s",
            "Latest observed payment projection lag.");
    }

    public static readonly Histogram<double> OutboxWriteDurationMs = Meter.CreateHistogram<double>(
        "homebudget.outbox.write.duration",
        "ms");

    public static readonly Counter<long> OutboxStatusTransitions = Meter.CreateCounter<long>(
        "homebudget.outbox.status.transitions");

    public static readonly Counter<long> PaymentInboxStatusTransitions = Meter.CreateCounter<long>(
        "homebudget.payment_inbox.status.transitions");

    public static readonly Histogram<double> EventStoreWriteDurationMs = Meter.CreateHistogram<double>(
        "homebudget.eventstore.write.duration",
        "ms");

    public static readonly Histogram<double> EventStoreConsumeDurationMs = Meter.CreateHistogram<double>(
        "homebudget.eventstore.consume.duration",
        "ms");

    public static readonly Counter<long> EventStoreRetries = Meter.CreateCounter<long>(
        "homebudget.eventstore.retries");

    public static readonly Counter<long> EventStoreAppendFailures = Meter.CreateCounter<long>(
        "homebudget.eventstore.append.failures");

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

    public static readonly Counter<long> KafkaProcessingFailures = Meter.CreateCounter<long>(
        "homebudget.kafka.processing.failures");

    public static readonly Counter<long> ProjectionFailures = Meter.CreateCounter<long>(
        "homebudget.projection.failures");

    public static readonly Counter<long> ReconciliationFailures = Meter.CreateCounter<long>(
        "homebudget.reconciliation.failures");

    public static void SetOutboxStats(long pendingCount, long failedDeadLetterCount, long oldestPendingAgeSeconds)
    {
        Interlocked.Exchange(ref _outboxPendingCount, pendingCount);
        Interlocked.Exchange(ref _outboxFailedDeadLetterCount, failedDeadLetterCount);
        Interlocked.Exchange(ref _outboxOldestPendingAgeSeconds, oldestPendingAgeSeconds);
    }

    public static void SetKafkaConsumerLag(long lag)
        => Interlocked.Exchange(ref _kafkaConsumerLag, lag < 0 ? 0 : lag);

    public static void IncrementEventStoreDeadLetterCount(long count = 1)
        => Interlocked.Add(ref _eventStoreDeadLetterCount, count);

    public static void SetProjectionLagSeconds(long lagSeconds)
        => Interlocked.Exchange(ref _projectionLagSeconds, lagSeconds < 0 ? 0 : lagSeconds);

    public static long GetOutboxPendingCount() => Volatile.Read(ref _outboxPendingCount);

    public static long GetOutboxFailedDeadLetterCount() => Volatile.Read(ref _outboxFailedDeadLetterCount);

    public static long GetOutboxOldestPendingAgeSeconds() => Volatile.Read(ref _outboxOldestPendingAgeSeconds);

    public static long GetProjectionLagSeconds() => Volatile.Read(ref _projectionLagSeconds);
}

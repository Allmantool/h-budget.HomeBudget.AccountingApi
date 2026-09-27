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

    public static readonly Counter<long> KafkaMessagesReceived = Meter.CreateCounter<long>(
        "homebudget.kafka.messages.received",
        "{messages}",
        "Kafka messages received by the payment consumer.");

    public static readonly Counter<long> KafkaProcessingAttempts = Meter.CreateCounter<long>(
        "homebudget.kafka.processing.attempts",
        "{messages}",
        "Kafka payment message processing attempts by bounded processing outcome.");

    public static readonly Counter<long> KafkaOffsetCommitOutcomes = Meter.CreateCounter<long>(
        "homebudget.kafka.offset.commit.outcomes",
        "{commits}",
        "Kafka offset commit attempts by bounded outcome and processing outcome.");

    public static readonly Counter<long> KafkaMessagesDeadLettered = Meter.CreateCounter<long>(
        "homebudget.kafka.messages.deadlettered",
        "{messages}",
        "Malformed or retry-exhausted Kafka payment messages written to the dead-letter stream.");

    public static readonly Histogram<double> KafkaProcessingDurationMs = Meter.CreateHistogram<double>(
        "homebudget.kafka.processing.duration",
        "ms",
        "Kafka payment message processing duration by bounded outcome.");

    public static readonly Counter<long> EventStoreWriteOutcomes = Meter.CreateCounter<long>(
        "homebudget.eventstore.write.outcomes",
        "{events}",
        "Payment EventStoreDB write outcomes: appended, duplicate, or failed.");

    public static readonly Counter<long> ProjectionFailures = Meter.CreateCounter<long>(
        "homebudget.projection.failures");

    public static readonly Counter<long> ProjectionEventsProjected = Meter.CreateCounter<long>(
        "homebudget.projection.events.projected",
        "{events}",
        "Payment events successfully included in a MongoDB projection update.");

    public static readonly Counter<long> ProjectionBatchAttempts = Meter.CreateCounter<long>(
        "homebudget.projection.batch.attempts",
        "{batches}",
        "Payment projection batch attempts by bounded outcome.");

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

    public static string NormalizePaymentEventType(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return "unknown";
        }

        var separatorIndex = eventType.IndexOf('_', System.StringComparison.Ordinal);
        var candidate = separatorIndex < 0 ? eventType : eventType[..separatorIndex];

        return candidate.ToUpperInvariant() switch
        {
            "ADDED" => "added",
            "REMOVED" => "removed",
            "UPDATED" => "updated",
            "DEADLETTER" => "deadletter",
            _ => "unknown"
        };
    }
}

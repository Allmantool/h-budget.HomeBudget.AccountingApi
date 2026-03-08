using System.Diagnostics;

namespace HomeBudget.Core.Observability;

public static class ActivityEvents
{
    public static ActivityEvent RetryAttempt(int attempt) =>
        new(
            "retry",
            tags: new ActivityTagsCollection
            {
                { "retry.attempt", attempt }
            });

    public static ActivityEvent OutboxStored => new("outbox.stored");
    public static ActivityEvent OutboxAcknowledged => new("outbox.acknowledged");
    public static ActivityEvent KafkaPublished => new("kafka.published");
    public static ActivityEvent KafkaConsumed => new("kafka.Consumed");
    public static ActivityEvent EventStorePersisted => new("eventstore.persisted");
    public static ActivityEvent EventStoreConsumed => new("eventstore.consumed");
    public static ActivityEvent EventStoreSend => new("eventstore.send");
    public static ActivityEvent ProjectionUpdated => new("projection.updated");
    public static ActivityEvent NotificationSent => new("notification.sent");
}
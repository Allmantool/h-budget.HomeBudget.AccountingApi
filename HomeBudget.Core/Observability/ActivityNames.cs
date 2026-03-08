namespace HomeBudget.Core.Observability;

public static class ActivityNames
{
    public static class Http
    {
        public static readonly string Request = "http.request";
    }

    public static class Mediator
    {
        public static readonly string Request = "mediator.request";
    }

    public static class Database
    {
        public static readonly string Query = "db.query";
        public static readonly string OutboxInsert = "db.outbox.insert";
    }

    public static class Kafka
    {
        public static readonly string Produce = "kafka.produce";
        public static readonly string Consume = "kafka.consume";
    }

    public static class EventStore
    {
        public static readonly string Append = "eventstore.append";
        public static readonly string Read = "eventstore.read";
    }

    public static class Mongo
    {
        public static readonly string ProjectionUpdate = "mongo.projection.update";
    }

    public static class SSE
    {
        public static readonly string SendNotification = "sse.notification.send";
    }

    public static class Payment
    {
        public static readonly string Create = "payment.create";
        public static readonly string Update = "payment.update";
        public static readonly string Delete = "payment.delete";
    }

    public static class Account
    {
        public static readonly string Create = "account.create";
        public static readonly string Update = "account.update";
        public static readonly string Delete = "account.delete";
    }
}
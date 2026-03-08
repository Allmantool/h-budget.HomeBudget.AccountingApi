namespace HomeBudget.Core.Observability;

public static class ActivityTags
{
    public static readonly string TraceId = "trace.id";

    public static readonly string CorrelationId = "correlation.id";

    public static readonly string HttpStatusCode = "http.response.status_code";
    public static readonly string HttpRoute = "http.route";

    public static readonly string ExceptionMessage = "exception.message";
    public static readonly string ExceptionType = "exception.type";

    public static readonly string MediatorRequest = "mediator.request";
    public static readonly string MediatorRequestType = "mediator.request.type";

    public static readonly string MessagingSystem = "messaging.system";
    public static readonly string KafkaTopic = "messaging.destination.name";
    public static readonly string MessagingOperation = "messaging.operation";

    public static readonly string DbSystem = "db.system";
    public static readonly string DbStatement = "db.statement";

    public static readonly string EventStoreStream = "eventstore.stream";

    public static readonly string MongoCollection = "mongo.collection";

    public static readonly string SSEChannel = "sse.channel";

    // Domain tags
    public static readonly string AccountId = "ledger.account.id";
    public static readonly string PaymentId = "ledger.payment.id";
    public static readonly string CategoryId = "ledger.category.id";
    public static readonly string ContractorId = "ledger.contractor.id";

    public static readonly string Amount = "ledger.payment.amount";
    public static readonly string Currency = "ledger.payment.currency";
}
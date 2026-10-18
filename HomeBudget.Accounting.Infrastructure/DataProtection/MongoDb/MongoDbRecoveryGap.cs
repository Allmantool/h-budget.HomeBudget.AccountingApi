using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb
{
    public sealed record MongoDbRecoveryGap(
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc,
        string ReasonCode);
}

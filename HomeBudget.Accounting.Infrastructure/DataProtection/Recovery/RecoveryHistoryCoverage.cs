using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    [Flags]
    public enum RecoveryHistoryCoverage
    {
        None = 0,
        PaymentEvents = 1 << 0,
        Accounts = 1 << 1,
        Categories = 1 << 2,
        Contractors = 1 << 3,
        ReferenceUpdatesAndDeletes = 1 << 4,
        BusinessSettings = 1 << 5,
        SettingsUpdatesAndDeletes = 1 << 6,
        Rates = 1 << 7,
        RateUpdatesAndDeletes = 1 << 8,
        TransferRegistrations = 1 << 9,
        ProcessingState = 1 << 10,
        AcceptedPendingCommands = 1 << 11,
        MigrationLineage = 1 << 12
    }
}

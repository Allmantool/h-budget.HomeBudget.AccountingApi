namespace HomeBudget.Accounting.ReleaseVerification
{
    internal sealed record FamilyProExpectedResults(
        int Accounts,
        int Categories,
        int Contractors,
        int OrdinaryPayments,
        int SplitPayments,
        int MigrationAdjustments,
        int NonTransferEffects,
        int Transfers,
        int TransferSides,
        int ExcludedOperationRows,
        int ExcludedDetailRows,
        int UnresolvedRecords,
        decimal AdjustmentSignedTotal,
        decimal AdjustmentAbsoluteTotal,
        int BalancedAccounts);
}

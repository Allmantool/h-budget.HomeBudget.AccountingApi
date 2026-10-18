namespace HomeBudget.Accounting.Infrastructure.DataProtection.SqlServer
{
    public sealed class SqlServerBackupChainValidationException : SqlServerProtectionException
    {
        public SqlServerBackupChainValidationException(string safeMessage)
            : base(safeMessage)
        {
        }
    }
}

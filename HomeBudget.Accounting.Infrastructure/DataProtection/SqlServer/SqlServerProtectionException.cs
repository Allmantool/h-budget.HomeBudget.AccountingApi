using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.SqlServer
{
    public class SqlServerProtectionException : Exception
    {
        public SqlServerProtectionException(string safeMessage, int? providerErrorNumber = null)
            : base(safeMessage)
        {
            ProviderErrorNumber = providerErrorNumber;
        }

        public int? ProviderErrorNumber { get; }
    }
}

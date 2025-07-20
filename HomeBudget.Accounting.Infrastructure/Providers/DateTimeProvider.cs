using System;

using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;

namespace HomeBudget.Accounting.Infrastructure.Providers
{
    internal class DateTimeProvider : IDateTimeProvider
    {
        public DateTime GetNowUtc() => DateTime.UtcNow;
    }
}

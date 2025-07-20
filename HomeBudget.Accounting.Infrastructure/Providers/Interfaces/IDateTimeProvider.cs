using System;

namespace HomeBudget.Accounting.Infrastructure.Providers.Interfaces
{
    public interface IDateTimeProvider
    {
        DateTime GetNowUtc();
    }
}

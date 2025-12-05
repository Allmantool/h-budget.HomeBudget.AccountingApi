using System;

namespace HomeBudget.Test.Core.Helpers
{
    public static class TimeoutHelper
    {
        public static bool IsTimedOut(DateTime start, TimeSpan timeout) =>
            DateTime.UtcNow - start > timeout;
    }
}

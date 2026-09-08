using System;

namespace HomeBudget.Components.Operations.Extensions
{
    public static class OperationDayTimeExtensions
    {
        public static string ToPeriodKey(this DateOnly operationDay)
        {
            return $"{operationDay.Year:D4}-{operationDay.Month:D2}";
        }
    }
}

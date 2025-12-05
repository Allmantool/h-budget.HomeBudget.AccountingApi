using System;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Core.Constants;

namespace HomeBudget.Accounting.Domain.Extensions
{
    public static class FinancialPeriodExtensions
    {
        public static FinancialPeriod ToFinancialPeriod(this DateOnly operationDate)
        {
            var startDate = new DateOnly(operationDate.Year, operationDate.Month, 1);

            return new FinancialPeriod
            {
                StartDate = startDate,
                EndDate = startDate.AddMonths(1).AddDays(-1)
            };
        }

        public static FinancialPeriod ToFinancialPeriod(this DateTime operationDateTime)
        {
            var date = DateOnly.FromDateTime(operationDateTime);

            return date.ToFinancialPeriod();
        }

        public static string ToFinancialMonthIdentifier(this FinancialPeriod period, Guid id)
        {
            return $"{id}" +
                   $"-{period.StartDate.ToString(DateTimeFormats.FinancialPeriod)}" +
                   $"-{period.EndDate.ToString(DateTimeFormats.FinancialPeriod)}";
        }
    }
}

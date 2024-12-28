using System;

namespace HomeBudget.Accounting.Domain.Models
{
    public class FinancialPeriod
    {
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
    }
}

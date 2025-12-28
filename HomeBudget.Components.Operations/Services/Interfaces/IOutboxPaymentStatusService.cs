using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Infrastructure.Data.DbEntries;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    public interface IOutboxPaymentStatusService
    {
        void SetStatus(string partitionKey, OutboxStatus status);

        void WriteRecord(OutboxAccountPaymentsEntity record);
    }
}

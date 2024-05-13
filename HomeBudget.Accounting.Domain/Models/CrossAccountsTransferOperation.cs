using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Domain.Models
{
    public class CrossAccountsTransferOperation : DomainEntity
    {
        public CrossAccountsTransferOperation(Guid key)
        {
            Key = key;
        }

        public CrossAccountsTransferOperation()
        {
            Key = Guid.NewGuid();
        }

        public IReadOnlyCollection<FinancialTransaction> PaymentOperations { get; set; }
    }
}

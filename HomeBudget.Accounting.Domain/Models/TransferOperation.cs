using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Domain.Models
{
    public class TransferOperation : DomainEntity
    {
        public TransferOperation(Guid key)
        {
            Key = key;
        }

        public TransferOperation()
        {
            Key = Guid.NewGuid();
        }

        public IReadOnlyCollection<PaymentOperation> PaymentOperations { get; set; }
    }
}

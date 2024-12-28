using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Api.Models.Operations.Responses
{
    public record CrossAccountsTransferResponse
    {
        public Guid PaymentOperationId { get; init; }
        public IEnumerable<Guid> PaymentAccountIds { get; set; }
    }
}

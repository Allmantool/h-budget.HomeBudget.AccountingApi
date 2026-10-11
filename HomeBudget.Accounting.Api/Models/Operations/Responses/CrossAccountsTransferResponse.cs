using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Api.Models.Operations.Responses
{
    public record CrossAccountsTransferResponse
    {
        public Guid PaymentOperationId { get; init; }
        public IEnumerable<Guid> PaymentAccountIds { get; set; }
        public string CommandId { get; init; }
        public string SenderCommandId { get; init; }
        public string RecipientCommandId { get; init; }
        public string Status { get; init; }
        public bool IsDuplicate { get; init; }
        public string LedgerId { get; init; }
        public string LedgerGenerationId { get; init; }
        public string GenerationToken { get; init; }
    }
}

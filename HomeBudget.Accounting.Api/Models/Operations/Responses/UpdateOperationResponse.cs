using System;

using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Accounting.Api.Models.Operations.Responses
{
    public sealed record UpdateOperationResponse
    {
        public string PaymentAccountId { get; set; }
        public string PaymentOperationId { get; set; }
        public string CommandId { get; set; }
        public string Status { get; set; }
        public bool IsDuplicate { get; set; }
        public string LedgerId { get; set; }
        public string LedgerGenerationId { get; set; }
        public string GenerationToken { get; set; }

        public static UpdateOperationResponse From(
            string paymentAccountId,
            string operationId,
            PaymentCommandRecord command,
            bool isDuplicate,
            LedgerGenerationScope generation)
        {
            return new UpdateOperationResponse
            {
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = command.PaymentOperationId == Guid.Empty ? operationId : command.PaymentOperationId.ToString(),
                CommandId = command.CommandId,
                Status = command.Status.ToString(),
                IsDuplicate = isDuplicate,
                LedgerId = command.LedgerId,
                LedgerGenerationId = command.LedgerGenerationId,
                GenerationToken = generation.GenerationToken
            };
        }
    }
}

using System;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HomeBudget.Accounting.Api.Models.Operations.Requests
{
    public record UpdateTransferRequest : IValidatableObject
    {
        public Guid TransferOperationId { get; set; }
        public Guid Sender { get; set; }
        public Guid Recipient { get; set; }
        public decimal Amount { get; set; }
        public decimal Multiplier { get; set; }
        public DateOnly OperationAt { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (TransferOperationId == Guid.Empty)
            {
                yield return new ValidationResult("Transfer operation id is required", [nameof(TransferOperationId)]);
            }

            foreach (var validationResult in new CrossAccountsTransferRequest
            {
                Sender = Sender,
                Recipient = Recipient,
                Amount = Amount,
                Multiplier = Multiplier,
                OperationAt = OperationAt
            }.Validate(validationContext))
            {
                yield return validationResult;
            }
        }
    }
}

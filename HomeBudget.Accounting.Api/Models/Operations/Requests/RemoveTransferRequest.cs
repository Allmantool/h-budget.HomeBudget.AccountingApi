using System;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HomeBudget.Accounting.Api.Models.Operations.Requests
{
    public record RemoveTransferRequest : IValidatableObject
    {
        public Guid PaymentAccountId { get; set; }
        public Guid TransferOperationId { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (PaymentAccountId == Guid.Empty)
            {
                yield return new ValidationResult("Payment account id is required", [nameof(PaymentAccountId)]);
            }

            if (TransferOperationId == Guid.Empty)
            {
                yield return new ValidationResult("Transfer operation id is required", [nameof(TransferOperationId)]);
            }
        }
    }
}

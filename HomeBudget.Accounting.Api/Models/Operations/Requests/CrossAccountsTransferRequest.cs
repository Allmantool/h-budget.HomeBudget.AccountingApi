using System;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HomeBudget.Accounting.Api.Models.Operations.Requests
{
    public record CrossAccountsTransferRequest : IValidatableObject
    {
        public Guid Sender { get; set; }
        public Guid Recipient { get; set; }
        public decimal Amount { get; set; }
        public decimal Multiplier { get; set; }
        public DateOnly OperationAt { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Sender == Guid.Empty)
            {
                yield return new ValidationResult("Sender account id is required", [nameof(Sender)]);
            }

            if (Recipient == Guid.Empty)
            {
                yield return new ValidationResult("Recipient account id is required", [nameof(Recipient)]);
            }

            if (Sender != Guid.Empty && Sender == Recipient)
            {
                yield return new ValidationResult("Sender and recipient accounts must be different", [nameof(Sender), nameof(Recipient)]);
            }

            if (Amount <= 0m)
            {
                yield return new ValidationResult("Amount must be greater than zero", [nameof(Amount)]);
            }

            if (Multiplier <= 0m)
            {
                yield return new ValidationResult("Multiplier must be greater than zero", [nameof(Multiplier)]);
            }

            if (OperationAt == default)
            {
                yield return new ValidationResult("Operation date is required", [nameof(OperationAt)]);
            }
        }
    }
}

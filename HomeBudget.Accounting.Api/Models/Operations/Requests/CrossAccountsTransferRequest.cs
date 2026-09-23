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
        public decimal? CustomConversionMultiplier { get; set; }
        public decimal? SenderAmount { get; set; }
        public decimal? RecipientAmount { get; set; }
        public string SenderCurrency { get; set; }
        public string RecipientCurrency { get; set; }
        public string SourceReference { get; set; }
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

            var exactAmountsProvided = SenderAmount.HasValue || RecipientAmount.HasValue;
            if (exactAmountsProvided && (SenderAmount is null or <= 0m || RecipientAmount is null or <= 0m))
            {
                yield return new ValidationResult(
                    "Both exact transfer-side amounts must be greater than zero",
                    [nameof(SenderAmount), nameof(RecipientAmount)]);
            }

            if (!exactAmountsProvided && Amount <= 0m)
            {
                yield return new ValidationResult("Amount must be greater than zero", [nameof(Amount)]);
            }

            if (!exactAmountsProvided && Multiplier <= 0m)
            {
                yield return new ValidationResult("Multiplier must be greater than zero", [nameof(Multiplier)]);
            }

            if (exactAmountsProvided && (string.IsNullOrWhiteSpace(SenderCurrency) || string.IsNullOrWhiteSpace(RecipientCurrency)))
            {
                yield return new ValidationResult(
                    "Both transfer-side currencies are required with exact amounts",
                    [nameof(SenderCurrency), nameof(RecipientCurrency)]);
            }

            if (CustomConversionMultiplier is <= 0m)
            {
                yield return new ValidationResult(
                    "Custom conversion multiplier must be greater than zero",
                    [nameof(CustomConversionMultiplier)]);
            }

            if (OperationAt == default)
            {
                yield return new ValidationResult("Operation date is required", [nameof(OperationAt)]);
            }
        }
    }
}

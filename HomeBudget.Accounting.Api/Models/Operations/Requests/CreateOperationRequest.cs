using System;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HomeBudget.Accounting.Api.Models.Operations.Requests
{
    public record CreateOperationRequest : IValidatableObject
    {
        public int ScopeOperationId { get; init; }
        public decimal Amount { get; init; }
        public string Comment { get; init; }
        public string ContractorId { get; init; }
        public string CategoryId { get; init; }
        public DateOnly OperationDate { get; init; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Amount == 0m)
            {
                yield return new ValidationResult(
                    "Amount must not be zero",
                    [nameof(Amount)]);
            }

            if (OperationDate == default)
            {
                yield return new ValidationResult(
                    "Operation date is required",
                    [nameof(OperationDate)]);
            }

            foreach (var validationResult in ValidateOptionalGuid(CategoryId, nameof(CategoryId)))
            {
                yield return validationResult;
            }

            foreach (var validationResult in ValidateOptionalGuid(ContractorId, nameof(ContractorId)))
            {
                yield return validationResult;
            }
        }

        public static IEnumerable<ValidationResult> ValidateOptionalGuid(string value, string memberName)
        {
            if (!string.IsNullOrWhiteSpace(value) && !Guid.TryParse(value, out _))
            {
                yield return new ValidationResult(
                    $"{memberName} must be a valid GUID",
                    [memberName]);
            }
        }
    }
}

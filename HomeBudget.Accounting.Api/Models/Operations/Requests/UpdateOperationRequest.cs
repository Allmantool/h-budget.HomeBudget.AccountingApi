using System;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HomeBudget.Accounting.Api.Models.Operations.Requests
{
    public record UpdateOperationRequest : IValidatableObject
    {
        public decimal Amount { get; init; }
        public string Comment { get; init; }
        public string ContractorId { get; init; }
        public string CategoryId { get; init; }
        public DateOnly OperationDate { get; init; }
        public string ScopeOperationId { get; init; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Amount == 0m)
            {
                yield return new ValidationResult(
                    "Amount must not be zero",
                    [nameof(Amount)]);
            }

            foreach (var validationResult in CreateOperationRequest.ValidateOptionalGuid(CategoryId, nameof(CategoryId)))
            {
                yield return validationResult;
            }

            foreach (var validationResult in CreateOperationRequest.ValidateOptionalGuid(ContractorId, nameof(ContractorId)))
            {
                yield return validationResult;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HomeBudget.Accounting.Api.Models.History
{
    public sealed record PaymentHistoryQueryRequest : IValidatableObject
    {
        public const int DefaultPageSize = 25;
        public const int MaximumPageSize = 100;
        public const int MaximumPage = 10000;
        public const int MaximumSortValueLength = 16;

        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = DefaultPageSize;
        public string SortBy { get; init; } = "date";
        public string SortDirection { get; init; } = "desc";
        public DateOnly? DateFrom { get; init; }
        public DateOnly? DateTo { get; init; }
        public string Type { get; init; }
        public string CategoryId { get; init; }
        public string ContractorId { get; init; }
        public decimal? AmountMin { get; init; }
        public decimal? AmountMax { get; init; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Page < 1 || Page > MaximumPage)
            {
                yield return new ValidationResult($"Page must be between 1 and {MaximumPage}", [nameof(Page)]);
            }

            if (PageSize < 1 || PageSize > MaximumPageSize)
            {
                yield return new ValidationResult($"PageSize must be between 1 and {MaximumPageSize}", [nameof(PageSize)]);
            }

            if (DateFrom > DateTo)
            {
                yield return new ValidationResult("DateFrom must be before or equal to DateTo", [nameof(DateFrom), nameof(DateTo)]);
            }

            if (AmountMin > AmountMax)
            {
                yield return new ValidationResult("AmountMin must be less than or equal to AmountMax", [nameof(AmountMin), nameof(AmountMax)]);
            }

            if (!IsOneOf(SortBy, "date", "amount"))
            {
                yield return new ValidationResult("SortBy must be date or amount", [nameof(SortBy)]);
            }

            if (!IsOneOf(SortDirection, "asc", "desc"))
            {
                yield return new ValidationResult("SortDirection must be asc or desc", [nameof(SortDirection)]);
            }

            if (!string.IsNullOrWhiteSpace(Type) && !IsOneOf(Type, "income", "expense"))
            {
                yield return new ValidationResult("Type must be income or expense", [nameof(Type)]);
            }

            foreach (var result in ValidateOptionalGuid(CategoryId, nameof(CategoryId)))
            {
                yield return result;
            }

            foreach (var result in ValidateOptionalGuid(ContractorId, nameof(ContractorId)))
            {
                yield return result;
            }
        }

        private static bool IsOneOf(string value, params string[] values)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.Length <= MaximumSortValueLength &&
                   Array.Exists(values, candidate => string.Equals(candidate, value.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<ValidationResult> ValidateOptionalGuid(string value, string memberName)
        {
            if (!string.IsNullOrWhiteSpace(value) && !Guid.TryParse(value, out _))
            {
                yield return new ValidationResult($"{memberName} must be a valid GUID", [memberName]);
            }
        }
    }
}

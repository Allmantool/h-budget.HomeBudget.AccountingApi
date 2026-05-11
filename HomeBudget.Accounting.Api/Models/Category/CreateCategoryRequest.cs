using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

using HomeBudget.Accounting.Domain.Enumerations;

namespace HomeBudget.Accounting.Api.Models.Category
{
    public record CreateCategoryRequest : IValidatableObject
    {
        public IEnumerable<string> NameNodes { get; set; }

        public int CategoryType { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (NameNodes == null || !NameNodes.Any(node => !string.IsNullOrWhiteSpace(node)))
            {
                yield return new ValidationResult("Name nodes are required", [nameof(NameNodes)]);
            }

            if (!BaseEnumeration<CategoryTypes, int>.TryFromValue(CategoryType, out _))
            {
                yield return new ValidationResult("Category type is invalid", [nameof(CategoryType)]);
            }
        }
    }
}

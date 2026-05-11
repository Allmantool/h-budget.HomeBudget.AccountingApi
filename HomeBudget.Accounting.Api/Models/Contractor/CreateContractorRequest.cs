using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace HomeBudget.Accounting.Api.Models.Contractor
{
    public record CreateContractorRequest : IValidatableObject
    {
        public IEnumerable<string> NameNodes { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (NameNodes == null || !NameNodes.Any(node => !string.IsNullOrWhiteSpace(node)))
            {
                yield return new ValidationResult("Name nodes are required", [nameof(NameNodes)]);
            }
        }
    }
}

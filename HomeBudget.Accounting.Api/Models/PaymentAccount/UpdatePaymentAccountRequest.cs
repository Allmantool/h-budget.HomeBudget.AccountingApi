using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

using HomeBudget.Accounting.Domain.Enumerations;

namespace HomeBudget.Accounting.Api.Models.PaymentAccount
{
    public class UpdatePaymentAccountRequest : IValidatableObject
    {
        public string Agent { get; set; }
        public decimal Balance { get; set; }
        public string Currency { get; set; }
        public string Description { get; set; }
        public int AccountType { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(Agent))
            {
                yield return new ValidationResult("Agent is required", [nameof(Agent)]);
            }

            if (string.IsNullOrWhiteSpace(Currency))
            {
                yield return new ValidationResult("Currency is required", [nameof(Currency)]);
            }

            if (!BaseEnumeration<AccountTypes, int>.TryFromValue(AccountType, out _))
            {
                yield return new ValidationResult("Account type is invalid", [nameof(AccountType)]);
            }
        }
    }
}

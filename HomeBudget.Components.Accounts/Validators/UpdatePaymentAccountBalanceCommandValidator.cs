using System;
using System.Collections.Generic;

using HomeBudget.Components.Accounts.Commands.Models;
using HomeBudget.Core.Validation;

namespace HomeBudget.Components.Accounts.Validators
{
    internal sealed class UpdatePaymentAccountBalanceCommandValidator
        : IRequestValidator<UpdatePaymentAccountBalanceCommand>
    {
        public IReadOnlyCollection<string> Validate(UpdatePaymentAccountBalanceCommand request)
        {
            if (request.PaymentAccountId == Guid.Empty)
            {
                return ["Payment account id is required"];
            }

            return [];
        }
    }
}

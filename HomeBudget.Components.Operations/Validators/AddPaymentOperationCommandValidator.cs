using System.Collections.Generic;

using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Core.Validation;

namespace HomeBudget.Components.Operations.Validators
{
    internal sealed class AddPaymentOperationCommandValidator
      : PaymentOperationCommandValidatorBase,
          IRequestValidator<AddPaymentOperationCommand>
    {
        public IReadOnlyCollection<string> Validate(AddPaymentOperationCommand request)
        {
            return ValidatePaymentOperation(request.OperationForAdd, "Payment operation");
        }
    }
}

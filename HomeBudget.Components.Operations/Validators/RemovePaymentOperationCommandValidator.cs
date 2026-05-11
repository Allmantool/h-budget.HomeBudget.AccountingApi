using System.Collections.Generic;

using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Core.Validation;

namespace HomeBudget.Components.Operations.Validators
{
    internal sealed class RemovePaymentOperationCommandValidator
        : PaymentOperationCommandValidatorBase, IRequestValidator<RemovePaymentOperationCommand>
    {
        public IReadOnlyCollection<string> Validate(RemovePaymentOperationCommand request)
        {
            return ValidatePaymentOperation(request.OperationForDelete, "Payment operation");
        }
    }
}

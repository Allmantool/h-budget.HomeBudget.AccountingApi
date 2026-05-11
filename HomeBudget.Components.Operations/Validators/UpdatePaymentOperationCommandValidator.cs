using System.Collections.Generic;

using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Core.Validation;

namespace HomeBudget.Components.Operations.Validators
{
    internal sealed class UpdatePaymentOperationCommandValidator
     : PaymentOperationCommandValidatorBase, IRequestValidator<UpdatePaymentOperationCommand>
    {
        public IReadOnlyCollection<string> Validate(UpdatePaymentOperationCommand request)
        {
            return ValidatePaymentOperation(request.OperationForUpdate, "Payment operation");
        }
    }
}

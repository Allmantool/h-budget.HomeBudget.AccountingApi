using System.Collections.Generic;

using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Core.Validation;

namespace HomeBudget.Components.Operations.Validators
{
    internal sealed class UpdateTransferCommandValidator
        : PaymentOperationCommandValidatorBase, IRequestValidator<UpdateTransferCommand>
    {
        public IReadOnlyCollection<string> Validate(UpdateTransferCommand request)
        {
            return ValidateTransferOperations(request.Key, request.PaymentOperations);
        }
    }
}

using System.Collections.Generic;

using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Core.Validation;

namespace HomeBudget.Components.Operations.Validators
{
    internal sealed class RemoveTransferCommandValidator
         : PaymentOperationCommandValidatorBase, IRequestValidator<RemoveTransferCommand>
    {
        public IReadOnlyCollection<string> Validate(RemoveTransferCommand request)
        {
            return ValidateTransferOperations(request.Key, request.PaymentOperations);
        }
    }
}

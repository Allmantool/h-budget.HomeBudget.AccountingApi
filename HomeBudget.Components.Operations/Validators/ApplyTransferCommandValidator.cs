using System.Collections.Generic;

using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Core.Validation;

namespace HomeBudget.Components.Operations.Validators
{
    internal sealed class ApplyTransferCommandValidator
        : PaymentOperationCommandValidatorBase, IRequestValidator<ApplyTransferCommand>
    {
        public IReadOnlyCollection<string> Validate(ApplyTransferCommand request)
        {
            return ValidateTransferOperations(request.Key, request.PaymentOperations);
        }
    }
}

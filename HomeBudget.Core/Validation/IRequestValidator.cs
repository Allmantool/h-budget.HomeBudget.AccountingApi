using System.Collections.Generic;

namespace HomeBudget.Core.Validation
{
    public interface IRequestValidator<in TRequest>
    {
        IReadOnlyCollection<string> Validate(TRequest request);
    }
}

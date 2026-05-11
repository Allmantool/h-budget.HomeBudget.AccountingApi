using System;

namespace HomeBudget.Core.Validation
{
    public sealed class RequestValidationException : Exception
    {
        public RequestValidationException(string message)
            : base(message)
        {
        }
    }
}

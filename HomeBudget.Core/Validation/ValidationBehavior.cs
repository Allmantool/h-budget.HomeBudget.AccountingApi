using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using MediatR;

using HomeBudget.Core.Models;

namespace HomeBudget.Core.Validation
{
    public sealed class ValidationBehavior<TRequest, TResponse>(
        IEnumerable<IRequestValidator<TRequest>> validators)
        : IPipelineBehavior<TRequest, TResponse>
    {
        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            var failures = validators
                .SelectMany(validator => validator.Validate(request))
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct()
                .ToList();

            if (failures.Count == 0)
            {
                return await next(cancellationToken);
            }

            var message = $"Validation failed: {string.Join("; ", failures)}";
            var responseType = typeof(TResponse);

            if (responseType.IsGenericType
                && responseType.GetGenericTypeDefinition() == typeof(Result<>))
            {
                var failureMethod = responseType.GetMethod(
                    nameof(Result<object>.Failure),
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    [typeof(string)],
                    null);

                return (TResponse)failureMethod.Invoke(null, [message]);
            }

            throw new RequestValidationException(message);
        }
    }
}

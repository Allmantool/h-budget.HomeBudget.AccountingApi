using System.Threading;
using System.Threading.Tasks;

using MediatR;
using Microsoft.AspNetCore.Http;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Core.Commands;

namespace HomeBudget.Components.Operations.PIpelines
{
    internal sealed class CorrelationIdBehavior<TRequest, TResponse>
        : IPipelineBehavior<TRequest, TResponse>
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CorrelationIdBehavior(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            if (request is ICorrelatedCommand correlated)
            {
                var correlationId =
                    _httpContextAccessor.HttpContext?
                        .Items[HttpHeaderKeys.CorrelationId]
                        ?.ToString();

                if (!string.IsNullOrWhiteSpace(correlationId) &&
                    string.IsNullOrWhiteSpace(correlated.CorrelationId))
                {
                    correlated.CorrelationId = correlationId;
                }
            }

            return await next(cancellationToken);
        }
    }
}

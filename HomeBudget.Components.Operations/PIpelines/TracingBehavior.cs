using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using MediatR;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Core.Commands;
using HomeBudget.Core.Observability;

namespace HomeBudget.Components.Operations.PIpelines
{
    internal sealed class TracingBehavior<TRequest, TResponse>
        : IPipelineBehavior<TRequest, TResponse>
    {
        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            using var activity = ActivityPropagation.StartActivity(
                $"mediatr.{typeof(TRequest).Name}",
                ActivityKind.Internal);

            if (activity != null && request is ICorrelatedCommand correlatedCommand)
            {
                activity.SetCorrelationId(correlatedCommand.CorrelationId);
            }

            if (activity != null && Activity.Current?.TraceId != default)
            {
                activity.SetTag(HttpHeaderKeys.TraceId, Activity.Current.TraceId.ToString());
            }

            try
            {
                var response = await next(cancellationToken);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return response;
            }
            catch (System.Exception ex)
            {
                activity?.RecordException(ex);
                throw;
            }
        }
    }
}

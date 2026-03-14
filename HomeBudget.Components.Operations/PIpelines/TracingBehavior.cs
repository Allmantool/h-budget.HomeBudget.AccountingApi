using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using MediatR;

using HomeBudget.Core.Commands;
using HomeBudget.Core.Observability;

namespace HomeBudget.Components.Operations.PIpelines
{
    public sealed class TracingBehavior<TRequest, TResponse>
        : IPipelineBehavior<TRequest, TResponse>
    {
        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            using var activity = ActivityPropagation.StartActivity(
                ActivityNames.Mediator.Request,
                ActivityKind.Internal);

            if (activity != null)
            {
                activity.SetTag(ActivityTags.MediatorRequestType, typeof(TRequest).FullName);
            }

            if (activity != null && request is ICorrelatedCommand correlatedCommand)
            {
                activity.SetCorrelationId(correlatedCommand.CorrelationId);
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


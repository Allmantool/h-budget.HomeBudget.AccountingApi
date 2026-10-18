using System.Threading;
using System.Threading.Tasks;

using MediatR;

using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Accounting.Infrastructure.DataProtection.Capture;
using HomeBudget.Components.Operations.Commands.Models;

namespace HomeBudget.Components.Operations.PIpelines
{
    public sealed class MaintenanceAdmissionBehavior<TRequest, TResponse>(
        IMaintenanceAdmissionFence admissionFence,
        IActiveLedgerGenerationResolver generationResolver)
        : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            if (request is not ILedgerGenerationBoundCommand command)
            {
                return await next(cancellationToken).ConfigureAwait(false);
            }

            var generation = command.ExpectedGeneration
                ?? await generationResolver.GetActiveAsync(cancellationToken).ConfigureAwait(false);
            await using var permit = await admissionFence.AcquireAdmissionPermitAsync(
                MaintenanceScopeKey.ForLedger(generation),
                generation.LedgerGenerationId,
                cancellationToken).ConfigureAwait(false);
            return await next(cancellationToken).ConfigureAwait(false);
        }
    }
}

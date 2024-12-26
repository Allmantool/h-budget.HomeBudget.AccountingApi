using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediatR;
using Microsoft.Extensions.Logging;

using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Core.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Components.Accounts.Commands.Models;
using HomeBudget.Core;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal class SyncOperationsHistoryCommandHandler(
        ISender sender,
        ILogger<SyncOperationsHistoryCommandHandler> logger,
        IPaymentOperationsHistoryService operationsHistoryService)
        : IRequestHandler<SyncOperationsHistoryCommand, Result<decimal>>
    {
        public async Task<Result<decimal>> Handle(SyncOperationsHistoryCommand request, CancellationToken cancellationToken)
        {
            var accountId = request.PaymentAccountId;
            var events = request.EventsForAccount;

            var upToDateBalanceResult = await BenchmarkService.WithBenchmarkAsync(
                async () => await operationsHistoryService.SyncHistoryAsync(accountId, events),
                $"Execute SyncHistoryAsync for '{events.Count()}' events",
                logger,
                new { PaymentAccountId = accountId });

            await BenchmarkService.WithBenchmarkAsync(
                async () => await sender.Send(
                    new UpdatePaymentAccountBalanceCommand(
                        accountId,
                        upToDateBalanceResult.Payload),
                    cancellationToken),
                "Sending UpdatePaymentAccountBalanceCommand",
                logger,
                new { PaymentAccountId = accountId });

            return upToDateBalanceResult;
        }
    }
}

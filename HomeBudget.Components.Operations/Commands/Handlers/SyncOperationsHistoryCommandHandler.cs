using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediatR;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Components.Accounts.Commands.Models;
using HomeBudget.Components.Accounts.Services.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Core;
using HomeBudget.Core.Exceptions;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal class SyncOperationsHistoryCommandHandler(
        ISender sender,
        ILogger<SyncOperationsHistoryCommandHandler> logger,
        IPaymentAccountService paymentAccountService,

        IPaymentsHistoryDocumentsClient historyDocumentsClient,
        IPaymentOperationsHistoryService operationsHistoryService)
        : IRequestHandler<SyncOperationsHistoryCommand, Result<decimal>>
    {
        public async Task<Result<decimal>> Handle(SyncOperationsHistoryCommand request, CancellationToken cancellationToken)
        {
            var accountId = request.PaymentAccountId;
            var events = request.Events;

            if (events.IsNullOrEmpty())
            {
                return default;
            }

            var financialTransaction = events.First().Payload;
            var monthPeriodIdentifier = financialTransaction.GetMonthPeriodIdentifier();

            await BenchmarkService.WithBenchmarkAsync(
                async () => await operationsHistoryService.SyncHistoryAsync(monthPeriodIdentifier, events),
                $"Execute SyncHistoryAsync for '{events.Count()}' events",
                logger,
                new { PaymentAccountId = accountId });

            var periodBalancesPaymentDocuments = await BenchmarkService.WithBenchmarkAsync(
               async () => await historyDocumentsClient.GetAllPeriodBalancesForAccountAsync(accountId),
               $"Retrieve balance for account '{accountId}'",
               logger,
               new { PaymentAccountId = accountId });

            if (periodBalancesPaymentDocuments.IsNullOrEmpty())
            {
                return default;
            }

            var monthBalanceHistoryRecords = periodBalancesPaymentDocuments.Where(d => d != null).Select(d => d.Payload);

            var syncedStateRecords = monthBalanceHistoryRecords
                .GroupBy(i => i.Record.Key)
                .Select(gr => gr
                    .OrderBy(i => i.Record.OperationDay)
                    .ThenBy(i => i.Record.OperationUnixTime)
                    .Last())
                .ToList();

            var totalBalanceForAccount = syncedStateRecords.Sum(r => r.Balance);

            var finalBalance = await paymentAccountService.GetInitialBalanceAsync(accountId.ToString()) + totalBalanceForAccount;

            await BenchmarkService.WithBenchmarkAsync(
                async () => await sender.Send(new UpdatePaymentAccountBalanceCommand(accountId, finalBalance), cancellationToken),
                "Sending UpdatePaymentAccountBalanceCommand",
                logger,
                new { PaymentAccountId = accountId });

            return Result<decimal>.Succeeded(finalBalance);
        }
    }
}

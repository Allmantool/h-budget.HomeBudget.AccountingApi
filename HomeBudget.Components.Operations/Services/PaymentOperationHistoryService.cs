using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts.Services.Interfaces;
using HomeBudget.Components.Categories.Clients.Interfaces;
using HomeBudget.Components.Categories.Models;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Extensions;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Services
{
    internal class PaymentOperationHistoryService(
        IPaymentsHistoryDocumentsClient paymentsHistoryDocumentsClient,
        IPaymentAccountService paymentAccountService,
        ICategoryDocumentsClient categoryDocumentsClient)
        : IPaymentOperationHistoryService
    {
        public async Task<IReadOnlyCollection<PaymentOperationHistoryRecord>> GetHistoryWithRunningBalancesAsync(Guid paymentAccountId)
        {
            var documents = await paymentsHistoryDocumentsClient.GetAsync(paymentAccountId);
            var initialBalance = await paymentAccountService.GetInitialBalanceAsync(paymentAccountId.ToString());
            var categoriesResult = await categoryDocumentsClient.GetAsync();
            var categories = categoriesResult.Payload ?? Array.Empty<CategoryDocument>();
            var categoryMap = categories
                .Where(category => category?.Payload != null)
                .GroupBy(category => category.Payload.Key)
                .ToDictionary(group => group.Key, group => group.Last().Payload);

            var paymentAccountOperations = documents
                .Select(document => document.Payload)
                .OrderByHistoryOrder()
                .ToArray();

            var runningBalance = initialBalance;

            foreach (var operation in paymentAccountOperations)
            {
                runningBalance += operation.Record.CalculateIncrement(categoryMap);
                operation.Balance = runningBalance;
            }

            return paymentAccountOperations;
        }
    }
}

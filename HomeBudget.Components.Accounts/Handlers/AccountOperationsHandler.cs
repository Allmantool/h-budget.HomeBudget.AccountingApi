using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using HomeBudget.Components.Accounts.Models;

namespace HomeBudget.Components.Accounts.Handlers
{
    internal class AccountOperationsHandler(ILogger<AccountOperationsHandler> logger)
        : IAccountOperationsHandler
    {
        public async Task HandleAsync(AccountOperationEvent accountEvent, CancellationToken cancellationToken)
        {
            try
            {
                var eventPayload = accountEvent.Payload;
                var accountId = eventPayload.Key;

                var eventTypeTitle = $"{accountEvent.EventType}_{accountId}";
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "{Handler} with error: {ExceptionMessage}",
                    nameof(AccountOperationsHandler),
                    ex.Message);
            }
        }
    }
}

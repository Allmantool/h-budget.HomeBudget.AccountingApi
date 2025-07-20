using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Components.Accounts.Models;

namespace HomeBudget.Components.Accounts.Handlers
{
    internal interface IAccountOperationsHandler
    {
        Task HandleAsync(AccountOperationEvent paymentEvent, CancellationToken cancellationToken);
    }
}

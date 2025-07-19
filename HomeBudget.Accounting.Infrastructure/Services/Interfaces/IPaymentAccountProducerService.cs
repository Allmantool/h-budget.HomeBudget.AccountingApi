using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Infrastructure.Services.Interfaces
{
    public interface IPaymentAccountProducerService
    {
        Task SendAsync(AccountRecord topic, CancellationToken token);
    }
}

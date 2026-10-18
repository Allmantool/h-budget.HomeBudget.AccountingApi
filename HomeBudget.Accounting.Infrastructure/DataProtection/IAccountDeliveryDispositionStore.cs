using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    public interface IAccountDeliveryDispositionStore
    {
        Task<AccountDeliveryDispositionResult> RecordAsync(
            AccountDeliveryDisposition disposition,
            CancellationToken cancellationToken = default);
    }
}

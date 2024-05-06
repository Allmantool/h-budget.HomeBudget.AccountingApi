using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Handlers;

internal interface IPaymentOperationsDeliveryHandler
{
    Task HandleAsync(PaymentOperationEvent paymentEvent, CancellationToken cancellationToken);
}
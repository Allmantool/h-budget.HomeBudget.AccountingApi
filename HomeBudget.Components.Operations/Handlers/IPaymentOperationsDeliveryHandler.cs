using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Handlers;

internal interface IPaymentOperationsDeliveryHandler
{
    Task HandleAsync(IEnumerable<PaymentOperationEvent> paymentEvent, CancellationToken cancellationToken);
}
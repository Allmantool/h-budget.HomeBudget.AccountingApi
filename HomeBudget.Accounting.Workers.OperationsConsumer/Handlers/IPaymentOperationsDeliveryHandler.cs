using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Observability;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Handlers;

internal interface IPaymentOperationsDeliveryHandler
{
    Task HandleAsync(IEnumerable<ActivityEnvelope<PaymentOperationEvent>> paymentEvent, CancellationToken cancellationToken);
}

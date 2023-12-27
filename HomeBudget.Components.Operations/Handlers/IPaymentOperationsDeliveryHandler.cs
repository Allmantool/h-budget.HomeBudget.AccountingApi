using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;

namespace HomeBudget.Components.Operations.Handlers;

internal interface IPaymentOperationsDeliveryHandler
{
    Task HandleAsync(DeliveryResult<string, string> deliveryResult, CancellationToken cancellationToken);
}
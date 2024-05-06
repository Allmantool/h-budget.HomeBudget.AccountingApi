using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;

namespace HomeBudget.Components.Operations.Clients
{
    internal class PaymentOperationsProducer(IKafkaClientHandler handle)
        : BaseKafkaProducer<string, string>(handle)
    {
    }
}

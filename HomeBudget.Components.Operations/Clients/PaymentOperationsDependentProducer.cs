using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;

namespace HomeBudget.Components.Operations.Clients
{
    internal class PaymentOperationsDependentProducer(IKafkaClientHandler handle)
        : BaseKafkaDependentProducer<string, string>(handle)
    {
    }
}

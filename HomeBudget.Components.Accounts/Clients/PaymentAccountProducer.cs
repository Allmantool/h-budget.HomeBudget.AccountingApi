using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;

namespace HomeBudget.Components.Accounts.Clients
{
    internal class PaymentAccountProducer(IKafkaClientHandler handle)
        : BaseKafkaProducer<string, string>(handle)
    {
    }
}

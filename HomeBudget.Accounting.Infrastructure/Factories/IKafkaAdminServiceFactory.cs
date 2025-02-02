using HomeBudget.Accounting.Infrastructure.Services;

namespace HomeBudget.Accounting.Infrastructure.Factories
{
    public interface IKafkaAdminServiceFactory
    {
        IAdminKafkaService Build();
    }
}

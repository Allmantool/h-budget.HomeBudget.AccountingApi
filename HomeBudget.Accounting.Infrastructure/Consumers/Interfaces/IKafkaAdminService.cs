namespace HomeBudget.Accounting.Infrastructure.Consumers.Interfaces
{
    public interface IKafkaAdminService
    {
        void CreateTopic(string topic);
    }
}

using Confluent.Kafka;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Infrastructure.Services;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.Factories
{
    internal class KafkaAdminServiceFactory(ILogger<AdminKafkaService> logger, IOptions<KafkaOptions> options)
        : IKafkaAdminServiceFactory
    {
        private readonly AdminSettings _adminSettings = options.Value.AdminSettings;

        public IAdminKafkaService Build()
        {
            var config = new AdminClientConfig
            {
                BootstrapServers = _adminSettings.BootstrapServers,
                SocketTimeoutMs = 60000,
                Debug = "all"
            };

            try
            {
                var adminClient = new AdminClientBuilder(config).Build();

                return new AdminKafkaService(adminClient, logger);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during kafka admin client creation: {ExceptionMessage}", ex.Message);
                throw;
            }
        }
    }
}

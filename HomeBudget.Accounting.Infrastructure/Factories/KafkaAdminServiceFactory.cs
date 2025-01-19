using Confluent.Kafka;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Infrastructure.Services;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.Factories
{
    internal class KafkaAdminServiceFactory : IKafkaAdminServiceFactory
    {
        private readonly ILogger<AdminKafkaService> _logger;
        private readonly AdminSettings _adminSettings;

        public KafkaAdminServiceFactory(ILogger<AdminKafkaService> logger, IOptions<KafkaOptions> options)
        {
            _logger = logger;
            var kafkaOptions = options.Value;
            _adminSettings = kafkaOptions.AdminSettings;
        }

        public IAdminKafkaService Build()
        {
            var config = new AdminClientConfig
            {
                BootstrapServers = _adminSettings.BootstrapServers,
                SocketTimeoutMs = 60000,
                Debug = "all"
            };

            var adminClient = new AdminClientBuilder(config).Build();

            return new AdminKafkaService(adminClient, _logger);
        }
    }
}

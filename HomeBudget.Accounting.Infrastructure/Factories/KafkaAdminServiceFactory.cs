using System;

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
        private readonly AdminSettings? _adminSettings = options?.Value?.AdminSettings;

        public IAdminKafkaService Build()
        {
            if (_adminSettings == null)
            {
                logger.LogError("KafkaAdminServiceFactory: AdminSettings is null. Ensure KafkaOptions are configured correctly.");
                throw new InvalidOperationException("KafkaAdminServiceFactory: AdminSettings cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(_adminSettings.BootstrapServers))
            {
                logger.LogError("KafkaAdminServiceFactory: BootstrapServers is null or empty. Check configuration.");
                throw new InvalidOperationException("KafkaAdminServiceFactory: BootstrapServers must be set.");
            }

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
                logger.LogError(ex, "KafkaAdminServiceFactory: Error during Kafka admin client creation: {ExceptionMessage}", ex.Message);
                throw;
            }
        }
    }
}

using System;
using System.Threading.Tasks;

using EventStore.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using HomeBudget.Accounting.Api.IntegrationTests.Models;
using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.IntegrationTests
{
    public class IntegrationTestWorkerFactory<TProgram>
        where TProgram : class
    {
        private readonly Func<TestContainersConnections> _workerHostInitializationCallback;
        private TestContainersConnections _containersConnections;

        public IConfiguration Configuration { get; private set; }
        public IHost WorkerHost { get; private set; }

        public IntegrationTestWorkerFactory(Func<TestContainersConnections> workerHostInitializationCallback)
        {
            _workerHostInitializationCallback = workerHostInitializationCallback;
        }

        public async Task StartAsync()
        {
            _containersConnections = _workerHostInitializationCallback.Invoke();

            WorkerHost = Workers.OperationsConsumer.Program.CreateHost(
                environmentName: HostEnvironments.Integration,
                configureServices: services =>
                {
                    services.Configure<KafkaOptions>(options =>
                    {
                        options.ProducerSettings = new ProducerSettings
                        {
                            BootstrapServers = _containersConnections.KafkaContainer
                        };
                        options.ConsumerSettings = new ConsumerSettings
                        {
                            BootstrapServers = _containersConnections.KafkaContainer
                        };
                        options.AdminSettings = new AdminSettings
                        {
                            BootstrapServers = _containersConnections.KafkaContainer
                        };
                    });

                    services.Configure<MongoDbOptions>(options =>
                    {
                        options.ConnectionString = _containersConnections.MongoDbContainer;
                        options.PaymentsHistory = "payments_history_test";
                        options.HandBooks = "handbooks_test";
                        options.PaymentAccounts = "payment_accounts_test";
                        options.LedgerDatabase = "ledger_test";
                    });

                    var eventStoreDbOptions = new EventStoreDbOptions();
                    services.AddEventStoreClient(
                        _containersConnections.EventSourceDbContainer,
                        settings =>
                        {
                            var esSettings = EventStoreClientSettings.Create(_containersConnections.EventSourceDbContainer);
                            esSettings.OperationOptions.ThrowOnAppendFailure = true;
                            esSettings.DefaultDeadline =
                                TimeSpan.FromSeconds(eventStoreDbOptions.TimeoutInSeconds *
                                                     (eventStoreDbOptions.RetryAttempts + 1));
                            esSettings.ConnectivitySettings = new EventStoreClientConnectivitySettings
                            {
                                KeepAliveInterval = TimeSpan.FromSeconds(eventStoreDbOptions.KeepAliveInterval),
                                GossipTimeout = TimeSpan.FromSeconds(eventStoreDbOptions.GossipTimeout),
                                DiscoveryInterval = TimeSpan.FromSeconds(eventStoreDbOptions.DiscoveryInterval),
                                MaxDiscoverAttempts = eventStoreDbOptions.MaxDiscoverAttempts
                            };
                        });
                });

            var configBuilder = new ConfigurationBuilder()
                .AddConfiguration(WorkerHost.Services.GetRequiredService<IConfiguration>());

            Configuration = configBuilder.Build();

            await WorkerHost.StartAsync();
        }

        public Task StopAsync()
        {
            return WorkerHost != null ? WorkerHost.StopAsync() : Task.CompletedTask;
        }
    }
}

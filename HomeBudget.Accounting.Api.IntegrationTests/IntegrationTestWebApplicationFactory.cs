﻿using System;
using System.IO;

using EventStore.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Api.IntegrationTests.Models;
using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.IntegrationTests
{
    public class IntegrationTestWebApplicationFactory<TStartup>
        (Func<TestContainersConnections> webHostInitializationCallback) : WebApplicationFactory<TStartup>
        where TStartup : class
    {
        private TestContainersConnections _containersConnections;

        internal IConfiguration Configuration { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, conf) =>
            {
                conf.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), $"appsettings.{HostEnvironments.Integration}.json"));
                conf.AddEnvironmentVariables();

                Configuration = conf.Build();

                _containersConnections = webHostInitializationCallback?.Invoke();
            });

            builder.ConfigureTestServices(services =>
            {
                var kafkaOptions = new KafkaOptions
                {
                    ProducerSettings = new ProducerSettings
                    {
                        BootstrapServers = _containersConnections.KafkaContainer,
                    },
                    ConsumerSettings = new ConsumerSettings
                    {
                        BootstrapServers = _containersConnections.KafkaContainer
                    },
                    AdminSettings = new AdminSettings
                    {
                        BootstrapServers = _containersConnections.KafkaContainer
                    }
                };

                var mongoDbOptions = new MongoDbOptions
                {
                    ConnectionString = _containersConnections.MongoDbContainer
                };

                services.AddOptions<KafkaOptions>().Configure(options =>
                {
                    options.AdminSettings = kafkaOptions.AdminSettings;
                    options.ProducerSettings = kafkaOptions.ProducerSettings;
                    options.ConsumerSettings = kafkaOptions.ConsumerSettings;
                });
                services.AddOptions<MongoDbOptions>().Configure(options =>
                {
                    options.ConnectionString = mongoDbOptions.ConnectionString;
                    options.PaymentsHistory = "payments_history_test";
                    options.HandBooks = "handbooks_test";
                    options.PaymentAccounts = "payment_accounts_test";
                });

                var eventStoreConnectionSettings = EventStoreClientSettings.Create(_containersConnections.EventSourceDbContainer);
                var eventStoreDbOptions = new EventStoreDbOptions();

                services.AddEventStoreClient(
                    _containersConnections.EventSourceDbContainer,
                    settings =>
                    {
                        settings = EventStoreClientSettings.Create(_containersConnections.EventSourceDbContainer);
                        settings.OperationOptions = new EventStoreClientOperationOptions
                        {
                            ThrowOnAppendFailure = true,
                        };
                        settings.DefaultDeadline = TimeSpan.FromSeconds(eventStoreDbOptions.TimeoutInSeconds * (eventStoreDbOptions.RetryAttempts + 1));
                        settings.ConnectivitySettings = new EventStoreClientConnectivitySettings
                        {
                            KeepAliveInterval = TimeSpan.FromSeconds(eventStoreDbOptions.KeepAliveInterval),
                            GossipTimeout = TimeSpan.FromSeconds(eventStoreDbOptions.GossipTimeout),
                            DiscoveryInterval = TimeSpan.FromSeconds(eventStoreDbOptions.DiscoveryInterval),
                            MaxDiscoverAttempts = eventStoreDbOptions.MaxDiscoverAttempts
                        };
                    });
            });

            base.ConfigureWebHost(builder);
        }
    }
}

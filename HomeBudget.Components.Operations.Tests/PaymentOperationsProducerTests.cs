using System;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Testcontainers.Kafka;

using HomeBudget.Accounting.Api.IntegrationTests.Factories;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;
using HomeBudget.Test.Core.WaitStrategies;

namespace HomeBudget.Components.Operations.Tests
{
    [TestFixture]
    [Ignore("temproraly disabled")]
    public class PaymentOperationsProducerTests
    {
        private IContainer _zookeperKafkaContainer;
        private KafkaContainer _kafkaContainer;
        private PaymentOperationsProducer _sut;

        [OneTimeSetUp]
        public async Task SetupAsync()
        {
            var testKafkaNetwork = await DockerNetworkFactory.GetOrCreateDockerNetworkAsync("test-kafka-net");

            _zookeperKafkaContainer = await DockerContainerFactory.GetOrCreateDockerContainerAsync(
                    $"test-zookeper",
                    cb => cb
                    .WithImage("confluentinc/cp-zookeeper:7.9.0")
                    .WithName($"{nameof(PaymentOperationsProducerTests)}-test-zookeper")
                    .WithHostname($"{nameof(PaymentOperationsProducerTests)}-test-zookeper")
                    .WithEnvironment("ZOOKEEPER_CLIENT_PORT", "2181")
                    .WithEnvironment("ZOOKEEPER_TICK_TIME", "2000")
                    .WithWaitStrategy(Wait.ForUnixContainer())
                    .WithNetwork(testKafkaNetwork)
                    .WithAutoRemove(true)
                    .WithCleanUp(true)
                    .Build());

            _kafkaContainer = new KafkaBuilder()
                    .WithImage("confluentinc/cp-kafka:7.9.0")
                    .WithName($"{nameof(PaymentOperationsProducerTests)}-test-kafka")
                    .WithHostname($"{nameof(PaymentOperationsProducerTests)}-test-kafka")
                    .WithNetwork(testKafkaNetwork)
                    .WithPortBinding(19092, 9092)
                    .WithEnvironment("KAFKA_BROKER_ID", "1")
                    .WithEnvironment(
                        "KAFKA_ZOOKEEPER_CONNECT",
                        $"{nameof(PaymentOperationsProducerTests)}-test-zookeper:2181")
                    .WithEnvironment(
                        "KAFKA_LISTENERS",
                        "PLAINTEXT://0.0.0.0:9092,BROKER://0.0.0.0:9093")
                    .WithEnvironment(
                        "KAFKA_ADVERTISED_LISTENERS",
                        $"PLAINTEXT://localhost:9092,BROKER://{nameof(PaymentOperationsProducerTests)}-test-kafka:9093")
                    .WithEnvironment(
                        "KAFKA_LISTENER_SECURITY_PROTOCOL_MAP",
                        "PLAINTEXT:PLAINTEXT,BROKER:PLAINTEXT")
                    .WithEnvironment(
                        "KAFKA_INTER_BROKER_LISTENER_NAME",
                        "BROKER")
                    .WithEnvironment("KAFKA_LOG_RETENTION_BYTES", "1073741824")
                    .WithEnvironment("KAFKA_LOG_CLEANUP_POLICY", "delete")
                    .WithEnvironment("KAFKA_DELETE_TOPIC_ENABLE", "true")
                    .WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "true")
                    .WithEnvironment("KAFKA_OFFSETS_TOPIC_NUM_PARTITIONS", "30")
                    .WithEnvironment("KAFKA_LOG_RETENTION_HOURS", "168")
                    .WithEnvironment("KAFKA_LOG_SEGMENT_BYTES", "1073741824")
                    .WithEnvironment("KAFKA_LOG_RETENTION_CHECK_INTERVAL_MS", "300000")
                    .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
                    .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
                        .WithWaitStrategy(Wait
                        .ForUnixContainer()
                        .UntilMessageIsLogged("started (kafka.server.KafkaServer)"))
                    .WithStartupCallback((kc, ct) => KafkaReadyStrategy.WaitUntilKafkaReadyAsync(kc, ct))
                    .WithCreateParameterModifier(config =>
                    {
                        config.HostConfig.NanoCPUs = 1500000000;
                    })

                    .WithAutoRemove(true)
                    .WithCleanUp(true)
                    .Build();

            await Task.WhenAll(
                _kafkaContainer.StartAsync(),
                _zookeperKafkaContainer.StartAsync());

            await Task.Delay(TimeSpan.FromSeconds(60));

            var config = new AdminClientConfig { BootstrapServers = _kafkaContainer.GetBootstrapAddress() };

            using var admin = new AdminClientBuilder(config).Build();

            await admin.CreateTopicsAsync(
            [
                new TopicSpecification { Name = BaseTopics.AccountingAccounts, NumPartitions = 1, ReplicationFactor = 1 },
                new TopicSpecification { Name = BaseTopics.AccountingPayments, NumPartitions = 1, ReplicationFactor = 1 }
            ]);
        }

        [Test]
        public async Task ProduceAsync_WhenProduceMessage_ThenDeliveryStatusShouldBePersisted()
        {
            var kafkaOptions = Options.Create(
                new KafkaOptions
                {
                    AdminSettings = new AdminSettings
                    {
                        BootstrapServers = _kafkaContainer.GetBootstrapAddress()
                    },
                    ProducerSettings = new ProducerSettings
                    {
                        BootstrapServers = _kafkaContainer.GetBootstrapAddress()
                    }
                });

            using var handler = new PaymentOperationsClientHandler(kafkaOptions);

            _sut = new PaymentOperationsProducer(handler);

            var paymentEvent = new PaymentOperationEvent
            {
                EventType = PaymentEventTypes.Added,
                Payload = new FinancialTransaction
                {
                    PaymentAccountId = Guid.Parse("3605a215-8100-4bb3-804a-6ae2b39b2e43"),
                    Key = Guid.Parse("7683a5d4-ba29-4274-8e9a-50de5361d46c"),
                    Amount = 112.78m,
                    CategoryId = Guid.Empty,
                    ContractorId = Guid.Empty,
                    Comment = "Test comment",
                    OperationDay = new DateOnly(2023, 12, 30)
                }
            };

            var messagePayloadResult = PaymentEventToMessageConverter.Convert(paymentEvent);

            var deliveryResult = await _sut.ProduceAsync(BaseTopics.AccountingAccounts, messagePayloadResult.Payload, CancellationToken.None);

            deliveryResult.Status.Should().Be(PersistenceStatus.Persisted);
        }
    }
}

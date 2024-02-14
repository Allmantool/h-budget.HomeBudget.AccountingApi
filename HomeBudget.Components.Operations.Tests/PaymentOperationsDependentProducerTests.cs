﻿using System;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Testcontainers.Kafka;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Tests
{
    [TestFixture]
    public class PaymentOperationsDependentProducerTests
    {
        private KafkaContainer _kafkaContainer;
        private PaymentOperationsDependentProducer _sut;

        [OneTimeSetUp]
        public void Setup()
        {
            _kafkaContainer = new KafkaBuilder()
                .WithImage("confluentinc/cp-kafka:7.4.3")
                .WithName($"{nameof(PaymentOperationsDependentProducerTests)}-container")
                .WithHostname("test-kafka-host")
                .WithAutoRemove(true)
                .WithCleanUp(true)
                .WithPortBinding(9192, 9192)
                .Build();
        }

        [Test]
        public async Task ProduceAsync_WhenProduceMessage_ThenDeliveryStatusShouldBePersisted()
        {
            await using (_kafkaContainer)
            {
                if (_kafkaContainer.State != TestcontainersStates.Running)
                {
                    await _kafkaContainer.StartAsync();
                }

                var kafkaOptions = Options.Create(
                    new KafkaOptions
                    {
                        ProducerSettings = new ProducerSettings
                        {
                            BootstrapServers = _kafkaContainer.GetBootstrapAddress()
                        }
                    });

                var handler = new PaymentOperationsClientHandlerHandler(kafkaOptions);

                _sut = new PaymentOperationsDependentProducer(handler);

                var paymentEvent = new PaymentOperationEvent
                {
                    PaymentEventType = PaymentEventTypes.Added,
                    Payload = new PaymentOperation
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

                var deliveryResult = await _sut.ProduceAsync("test-topic", messagePayloadResult.Payload, CancellationToken.None);

                deliveryResult.Status.Should().Be(PersistenceStatus.Persisted);
            }
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.IntegrationTests.Clients
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [Order(IntegrationTestOrderIndex.PaymentOperationsProducerTests)]
    public class PaymentOperationsProducerTests : BaseIntegrationTests
    {
        private PaymentOperationsProducer _sut;
        private string _kafkaContainerConnection;

        [OneTimeSetUp]
        public override async Task SetupAsync()
        {
            await base.SetupAsync();
            _kafkaContainerConnection = TestContainers.KafkaContainer.GetBootstrapAddress();
        }

        [Test]
        public async Task ProduceAsync_WhenProduceMessage_ThenDeliveryStatusShouldBePersisted()
        {
            var kafkaOptions = Options.Create(
                new KafkaOptions
                {
                    AdminSettings = new AdminSettings
                    {
                        BootstrapServers = _kafkaContainerConnection
                    },
                    ProducerSettings = new ProducerSettings
                    {
                        BootstrapServers = _kafkaContainerConnection
                    }
                });

            using var handler = new PaymentOperationsClientHandler(kafkaOptions, Mock.Of<ILogger<PaymentOperationsClientHandler>>());

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

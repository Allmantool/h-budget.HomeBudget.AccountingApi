using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;
using HomeBudget.Accounting.Infrastructure.Factories;
using HomeBudget.Accounting.Infrastructure.Helpers;
using HomeBudget.Accounting.Workers.OperationsConsumer.Services;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.IntegrationTests.Services
{
    [TestFixture]
    public class KafkaConsumerServiceTests
    {
        private KafkaConsumerService _service;
        private Mock<ILogger<KafkaConsumerService>> _loggerMock;
        private Mock<IKafkaConsumersFactory> _factoryMock;
        private KafkaOptions _options;

        [SetUp]
        public void SetUp()
        {
            _loggerMock = new Mock<ILogger<KafkaConsumerService>>();
            _factoryMock = new Mock<IKafkaConsumersFactory>();

            _options = new KafkaOptions
            {
                ConsumerSettings = new ConsumerSettings
                {
                    ConsumeDelayInMilliseconds = 10
                }
            };

            var optionsMock = Mock.Of<IOptions<KafkaOptions>>(o => o.Value == _options);
            _service = new KafkaConsumerService(_loggerMock.Object, optionsMock, _factoryMock.Object);

            ConsumersStore.Consumers.Clear();
        }

        [Test]
        public void CreateAndSubscribe_ShouldAddConsumerToStore_And_Subscribe()
        {
            var topic = new SubscriptionTopic
            {
                Title = "some-topic",
                ConsumerType = "type1"
            };

            var consumerMock = new Mock<IKafkaConsumer>();
            _factoryMock.Setup(f => f.WithTopic("some-topic")).Returns(_factoryMock.Object);
            _factoryMock.Setup(f => f.Build("type1")).Returns(consumerMock.Object);

            var consumer = _service.CreateAndSubscribe(topic);

            Assert.That(consumer, Is.Not.Null);

            consumerMock.Verify(c => c.Subscribe("some-topic"), Times.Once);
        }

        [Test]
        public void CreateAndSubscribe_ReturnsNull_WhenFactoryBuildReturnsNull()
        {
            var topic = new SubscriptionTopic
            {
                Title = "null-topic",
                ConsumerType = "type-null"
            };

            _factoryMock.Setup(f => f.WithTopic("null-topic")).Returns(_factoryMock.Object);
            _factoryMock.Setup(f => f.Build("type-null")).Returns<IKafkaConsumer>(null);

            var consumer = _service.CreateAndSubscribe(topic);

            Assert.That(consumer, Is.Null);
            Assert.That(ConsumersStore.Consumers.ContainsKey("null-topic"), Is.False);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Infrastructure;
using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;
using HomeBudget.Accounting.Infrastructure.Factories;
using HomeBudget.Accounting.Infrastructure.Services;
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
        public async Task ConsumeKafkaMessagesLoopAsync_StopsOnCancellation()
        {
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await _service.ConsumeKafkaMessagesLoopAsync(cts.Token);

            _loggerMock.Verify(
                l => l.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Consume loop has been stopped")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)
                )
            );
        }

        [Test]
        public async Task ConsumeKafkaMessagesLoopAsync_ConsumesMessages()
        {
            const string topic = "test-topic";
            var consumerMock = new Mock<IKafkaConsumer>();
            consumerMock
                .Setup(c => c.ConsumeAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            consumerMock.SetupGet(c => c.Subscriptions).Returns(new List<string> { topic });

            ConsumersStore.Consumers[topic] = new List<IKafkaConsumer> { consumerMock.Object };

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(50);

            await _service.ConsumeKafkaMessagesLoopAsync(cts.Token);

            consumerMock.Verify(c => c.ConsumeAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Test]
        public void CreateAndSubscribe_ShouldAddConsumerToStore_AndSubscribe()
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
            Assert.That(ConsumersStore.Consumers.ContainsKey("some-topic"), Is.True);

            var list = ConsumersStore.Consumers["some-topic"];
            Assert.That(list.Contains(consumer), Is.True);

            consumerMock.Verify(c => c.Subscribe("some-topic"), Times.Once);
        }
    }
}

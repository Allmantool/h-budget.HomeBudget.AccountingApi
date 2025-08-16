﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EventStore.Client;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Testcontainers.EventStoreDb;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Tests
{
    [TestFixture]
    public class PaymentOperationsEventStoreClientTests : IAsyncDisposable
    {
        private readonly Mock<IServiceScope> _serviceScopeMock = new();
        private readonly Mock<IServiceScopeFactory> _serviceScopeFactoryMock = new();
        private readonly Mock<IServiceProvider> _serviceProviderMock = new();
        private readonly Mock<IPaymentOperationsHistoryService> _paymentOperationsHistoryServiceMock = new();

        private EventStoreDbContainer _eventSourceDbContainer;
        private PaymentOperationsEventStoreClient _sut;

        [OneTimeSetUp]
        public async Task SetupAsync()
        {
            _eventSourceDbContainer = new EventStoreDbBuilder()
                .WithImage("eventstore/eventstore:24.10.6-jammy")
                .WithName($"{nameof(PaymentOperationsEventStoreClientTests)}-container")
                .WithHostname("test-event-store-db-host")
                .WithAutoRemove(true)
                .WithCleanUp(true)
                .WithPortBinding(3113, 2113)
                .Build();

            _paymentOperationsHistoryServiceMock
                .Setup(s => s.SyncHistoryAsync(It.IsAny<string>(), It.IsAny<IEnumerable<PaymentOperationEvent>>()))
                .ReturnsAsync(() => Result<decimal>.Succeeded(15));

            _serviceScopeMock
                .Setup(s => s.ServiceProvider)
                .Returns(_serviceProviderMock.Object);

            _serviceScopeFactoryMock
                .Setup(f => f.CreateScope())
                .Returns(_serviceScopeMock.Object);

            _serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
                .Returns(_serviceScopeFactoryMock.Object);

            _serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IPaymentOperationsHistoryService)))
                .Returns(_paymentOperationsHistoryServiceMock.Object);

            await _eventSourceDbContainer.StartAsync();
        }

        [Test]
        public async Task SendAsync_WithSeveralEventsUnderDifferentPaymentAccounts_ThenCommitExpectedEventsAtStore()
        {
            var paymentAccountIdA = Guid.Parse("3605a215-8100-4bb3-804a-6ae2b39b2e43");
            var paymentAccountIdB = Guid.Parse("91c3d1bc-ce45-415a-a97d-2a9d834c7e02");

            var dbConnectionString = _eventSourceDbContainer.GetConnectionString();

            using var client = new EventStoreClient(EventStoreClientSettings.Create(dbConnectionString));

            _sut = new PaymentOperationsEventStoreClient(
                Mock.Of<ILogger<PaymentOperationsEventStoreClient>>(),
                _serviceScopeFactoryMock.Object,
                Mock.Of<IDateTimeProvider>(),
                client,
                Options.Create(
                new EventStoreDbOptions
                {
                    RetryAttempts = 3,
                    TimeoutInSeconds = 10
                }));

            var paymentsEvents = new List<PaymentOperationEvent>
                {
                    new()
                    {
                        EventType = PaymentEventTypes.Added,
                        Payload = new FinancialTransaction
                        {
                            Key = Guid.Parse("7683a5d4-ba29-4274-8e9a-50de5361d46c"),
                            PaymentAccountId = paymentAccountIdA,
                            Amount = 199.1m,
                            CategoryId = Guid.Parse("5fa3c529-c8ed-49a7-bf5c-d8f404d6adb7"),
                            ContractorId = Guid.Parse("4913aea0-07d9-4c31-b7d5-20361347319e"),
                            Comment = "Account #1",
                            OperationDay = new DateOnly(2023, 12, 22)
                        }
                    },
                    new()
                    {
                        EventType = PaymentEventTypes.Removed,
                        Payload = new FinancialTransaction
                        {
                            Key = Guid.Parse("2c683dd6-3eea-40f3-918b-ab1b60ccebc4"),
                            PaymentAccountId = paymentAccountIdA,
                            Amount = 120.1m,
                            CategoryId = Guid.Parse("5fa3c529-c8ed-49a7-bf5c-d8f404d6adb7"),
                            ContractorId = Guid.Parse("4913aea0-07d9-4c31-b7d5-20361347319e"),
                            Comment = "Account #1",
                            OperationDay = new DateOnly(2023, 12, 22)
                        }
                    },
                    new()
                    {
                        EventType = PaymentEventTypes.Updated,
                        Payload = new FinancialTransaction
                        {
                            Key = Guid.Parse("2c683dd6-3eea-40f3-918b-ab1b60ccebc4"),
                            PaymentAccountId = paymentAccountIdA,
                            Amount = 160.1m,
                            CategoryId = Guid.Parse("5fa3c529-c8ed-49a7-bf5c-d8f404d6adb7"),
                            ContractorId = Guid.Parse("4913aea0-07d9-4c31-b7d5-20361347319e"),
                            Comment = "Account #1",
                            OperationDay = new DateOnly(2023, 12, 24)
                        }
                    },
                    new()
                    {
                        EventType = PaymentEventTypes.Added,
                        Payload = new FinancialTransaction
                        {
                            Key = Guid.Parse("a2603450-7c16-4ac7-955a-5e261ccc0b89"),
                            PaymentAccountId = paymentAccountIdB,
                            Amount = 89.1m,
                            CategoryId = Guid.Parse("5fa3c529-c8ed-49a7-bf5c-d8f404d6adb7"),
                            ContractorId = Guid.Parse("4913aea0-07d9-4c31-b7d5-20361347319e"),
                            Comment = "Account #2",
                            OperationDay = new DateOnly(2023, 12, 27)
                        }
                    },
                };

            foreach (var paymentEvent in paymentsEvents)
            {
                var eventTypeTitle = $"{paymentEvent.EventType}_{paymentEvent.Payload.Key}";
                var streamName = PaymentOperationNamesGenerator.GenerateForAccountMonthStream(paymentEvent.Payload.PaymentAccountId.ToString());

                await _sut.SendAsync(paymentEvent, streamName, eventTypeTitle);
            }

            var readResult = await _sut.ReadAsync(paymentAccountIdA.ToString()).ToListAsync();

            readResult.Count.Should().Be(paymentsEvents.Count(p => p.Payload.PaymentAccountId.CompareTo(paymentAccountIdA) == 0));
        }

        [Test]
        public async Task SendAsync_WithHighLoad_ThenAllEventsAreStoredAndLatestPerAccountProcessed()
        {
            var paymentAccountId = Guid.NewGuid();
            var totalEvents = 2_000;

            var dbConnectionString = _eventSourceDbContainer.GetConnectionString();
            using var client = new EventStoreClient(EventStoreClientSettings.Create(dbConnectionString));

            _sut = new PaymentOperationsEventStoreClient(
                Mock.Of<ILogger<PaymentOperationsEventStoreClient>>(),
                _serviceScopeFactoryMock.Object,
                Mock.Of<IDateTimeProvider>(),
                client,
                Options.Create(new EventStoreDbOptions
                {
                    RetryAttempts = 3,
                    TimeoutInSeconds = 10,
                    EventBatchingDelayInMs = 50
                }));

            var tasks = new List<Task>();
            for (int i = 0; i < totalEvents; i++)
            {
                var paymentEvent = new PaymentOperationEvent
                {
                    EventType = PaymentEventTypes.Added,
                    Payload = new FinancialTransaction
                    {
                        Key = Guid.NewGuid(),
                        PaymentAccountId = paymentAccountId,
                        Amount = 100 + i,
                        CategoryId = Guid.NewGuid(),
                        ContractorId = Guid.NewGuid(),
                        Comment = $"Event #{i}",
                        OperationDay = new DateOnly(2024, 1, (i % 28) + 1)
                    }
                };

                var eventTypeTitle = $"{paymentEvent.EventType}_{paymentEvent.Payload.Key}";
                var streamName = PaymentOperationNamesGenerator.GenerateForAccountMonthStream(paymentEvent.Payload.PaymentAccountId.ToString());

                tasks.Add(_sut.SendAsync(paymentEvent, streamName, eventTypeTitle));
            }

            await Task.WhenAll(tasks);

            var readResult = await _sut.ReadAsync(paymentAccountId.ToString()).ToListAsync();

            readResult.Count.Should().Be(totalEvents);

            var latestEventsByPeriod = readResult
                .GroupBy(e => e.Payload.GetMonthPeriodIdentifier())
                .Select(g => g.OrderByDescending(e => e.Payload.OperationDay).Last())
                .ToList();

            latestEventsByPeriod.Should().NotBeEmpty();
        }

        [Test]
        public async Task SendAsync_WithConcurrentHighLoadFromMultipleAccounts_ThenEventsAreStoredCorrectly()
        {
            var accountA = Guid.NewGuid();
            var accountB = Guid.NewGuid();
            var totalEvents = 1_000;

            var dbConnectionString = _eventSourceDbContainer.GetConnectionString();
            using var client = new EventStoreClient(EventStoreClientSettings.Create(dbConnectionString));

            _sut = new PaymentOperationsEventStoreClient(
                Mock.Of<ILogger<PaymentOperationsEventStoreClient>>(),
                _serviceScopeFactoryMock.Object,
                Mock.Of<IDateTimeProvider>(),
                client,
                Options.Create(new EventStoreDbOptions
                {
                    RetryAttempts = 3,
                    TimeoutInSeconds = 10
                }));

            var tasks = Enumerable.Range(0, totalEvents).Select(i =>
            {
                var accountId = i % 2 == 0 ? accountA : accountB;
                var paymentEvent = new PaymentOperationEvent
                {
                    EventType = PaymentEventTypes.Added,
                    Payload = new FinancialTransaction
                    {
                        Key = Guid.NewGuid(),
                        PaymentAccountId = accountId,
                        Amount = 50 + i,
                        CategoryId = Guid.NewGuid(),
                        ContractorId = Guid.NewGuid(),
                        Comment = $"Event {i}",
                        OperationDay = new DateOnly(2024, 2, (i % 28) + 1)
                    }
                };

                var eventTypeTitle = $"{paymentEvent.EventType}_{paymentEvent.Payload.Key}";
                var streamName = PaymentOperationNamesGenerator.GenerateForAccountMonthStream(paymentEvent.Payload.PaymentAccountId.ToString());

                return _sut.SendAsync(paymentEvent, streamName, eventTypeTitle);
            });

            await Task.WhenAll(tasks);

            var resultA = await _sut.ReadAsync(accountA.ToString()).ToListAsync();
            resultA.Should().NotBeEmpty();
            resultA.Count.Should().Be(totalEvents / 2);

            var resultB = await _sut.ReadAsync(accountB.ToString()).ToListAsync();
            resultB.Should().NotBeEmpty();
            resultB.Count.Should().Be(totalEvents / 2);
        }

        public async ValueTask DisposeAsync()
        {
            _eventSourceDbContainer?.StopAsync();
            _eventSourceDbContainer?.DisposeAsync();
        }
    }
}

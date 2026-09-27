using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Domain;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Workers.OperationsConsumer.Configuration;
using HomeBudget.Accounting.Workers.OperationsConsumer.Models;
using HomeBudget.Accounting.Workers.OperationsConsumer.Services;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.IntegrationTests.Workers
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    public sealed class ProjectionRebuildRunnerTests
    {
        [Test]
        public async Task DefaultMode_WritesPlanWithoutSendingProjectionCommand()
        {
            var output = Path.Combine(Path.GetTempPath(), $"projection-plan-{Guid.NewGuid():N}.json");
            var accountId = Guid.NewGuid();
            var period = PeriodFor(accountId);
            var stream = PaymentOperationNamesGenerator.GenerateForAccountMonthStream(period);
            var history = new Mock<IPaymentsHistoryDocumentsClient>();
            history.Setup(client => client.DiscoverProjectionScopesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    new PaymentHistoryProjectionScope(accountId, period, stream, null)
                ]);
            var eventStore = BuildEventStore(BuildEvents(accountId));
            var sender = new Mock<ISender>();
            var runner = CreateRunner(
                history.Object,
                eventStore.Object,
                sender.Object,
                new ProjectionRebuildOptions
                {
                    Enabled = true,
                    ExpectedTarget = "disposable",
                    PlanOutputFile = output
                });

            try
            {
                await runner.RunAsync(CancellationToken.None);

                var plan = JsonSerializer.Deserialize<ProjectionRebuildPlan>(
                    await File.ReadAllTextAsync(output),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                plan.Items.Should().ContainSingle();
                plan.Items[0].ExpectedRevision.Should().Be(1);
                plan.Items[0].ExpectedEventCount.Should().Be(2);
                sender.Verify(
                    current => current.Send(It.IsAny<SyncOperationsHistoryCommand>(), It.IsAny<CancellationToken>()),
                    Times.Never);
            }
            finally
            {
                File.Delete(output);
            }
        }

        [Test]
        public async Task ExecuteMode_WithoutStoppedWriterConfirmation_FailsBeforeReadingOrSending()
        {
            var history = new Mock<IPaymentsHistoryDocumentsClient>();
            var eventStore = new Mock<IEventStoreDbStreamReadClient<PaymentOperationEvent>>();
            var sender = new Mock<ISender>();
            var runner = CreateRunner(
                history.Object,
                eventStore.Object,
                sender.Object,
                new ProjectionRebuildOptions
                {
                    Enabled = true,
                    Execute = true,
                    ExpectedTarget = "disposable",
                    ConfirmTarget = "disposable",
                    OldWritersStopped = false,
                    ScopeFile = "unused.json"
                });

            var execute = async () => await runner.RunAsync(CancellationToken.None);

            await execute.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*old projection writers are stopped*");
            eventStore.VerifyNoOtherCalls();
            sender.VerifyNoOtherCalls();
        }

        [Test]
        public async Task DryRun_WithMismatchedPeriodStreamBinding_FailsClosed()
        {
            var output = Path.Combine(Path.GetTempPath(), $"projection-plan-{Guid.NewGuid():N}.json");
            var accountId = Guid.NewGuid();
            var period = PeriodFor(accountId);
            var history = new Mock<IPaymentsHistoryDocumentsClient>();
            history.Setup(client => client.DiscoverProjectionScopesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([new PaymentHistoryProjectionScope(accountId, period, "wrong-stream", null)]);
            var runner = CreateRunner(
                history.Object,
                BuildEventStore(BuildEvents(accountId)).Object,
                Mock.Of<ISender>(),
                new ProjectionRebuildOptions
                {
                    Enabled = true,
                    ExpectedTarget = "disposable",
                    PlanOutputFile = output
                });

            var execute = async () => await runner.RunAsync(CancellationToken.None);

            await execute.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*does not match its account, period, or source events*");
            File.Exists(output).Should().BeFalse();
        }

        [Test]
        public async Task ExecuteMode_WithPublishedRevisionAheadOfEventStore_FailsBeforeSending()
        {
            var scopeFile = Path.Combine(Path.GetTempPath(), $"projection-plan-{Guid.NewGuid():N}.json");
            var accountId = Guid.NewGuid();
            var period = PeriodFor(accountId);
            var stream = PaymentOperationNamesGenerator.GenerateForAccountMonthStream(period);
            var plan = new ProjectionRebuildPlan(
                "disposable",
                "localhost:27017",
                "history",
                "localhost:2113",
                DateTime.UtcNow,
                [new ProjectionRebuildPlanItem(accountId, period, stream, 1, 2, 2)]);
            await File.WriteAllTextAsync(
                scopeFile,
                JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var eventStore = new Mock<IEventStoreDbStreamReadClient<PaymentOperationEvent>>();
            var sender = new Mock<ISender>();
            var runner = CreateRunner(
                Mock.Of<IPaymentsHistoryDocumentsClient>(),
                eventStore.Object,
                sender.Object,
                new ProjectionRebuildOptions
                {
                    Enabled = true,
                    Execute = true,
                    ExpectedTarget = "disposable",
                    ConfirmTarget = "disposable",
                    OldWritersStopped = true,
                    ScopeFile = scopeFile
                });

            try
            {
                var execute = async () => await runner.RunAsync(CancellationToken.None);

                await execute.Should().ThrowAsync<InvalidOperationException>()
                    .WithMessage("*ahead of its authoritative EventStore revision*");
                eventStore.VerifyNoOtherCalls();
                sender.VerifyNoOtherCalls();
            }
            finally
            {
                File.Delete(scopeFile);
            }
        }

        [Test]
        public async Task ExecuteMode_WithMatchingImmutablePlan_SendsOneCheckpointedRebuild()
        {
            var scopeFile = Path.Combine(Path.GetTempPath(), $"projection-plan-{Guid.NewGuid():N}.json");
            var accountId = Guid.NewGuid();
            var period = PeriodFor(accountId);
            var stream = PaymentOperationNamesGenerator.GenerateForAccountMonthStream(period);
            var plan = new ProjectionRebuildPlan(
                "disposable",
                "localhost:27017",
                "history",
                "localhost:2113",
                DateTime.UtcNow,
                [new ProjectionRebuildPlanItem(accountId, period, stream, 1, 2, null)]);
            await File.WriteAllTextAsync(
                scopeFile,
                JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var history = new Mock<IPaymentsHistoryDocumentsClient>();
            history.Setup(client => client.DiscoverProjectionScopesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([new PaymentHistoryProjectionScope(accountId, period, stream, 1)]);
            var eventStore = BuildEventStore(BuildEvents(accountId));
            var sender = new Mock<ISender>();
            sender.Setup(current => current.Send(
                    It.IsAny<SyncOperationsHistoryCommand>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<decimal>.Succeeded(20m));
            var runner = CreateRunner(
                history.Object,
                eventStore.Object,
                sender.Object,
                new ProjectionRebuildOptions
                {
                    Enabled = true,
                    Execute = true,
                    ExpectedTarget = "disposable",
                    ConfirmTarget = "disposable",
                    OldWritersStopped = true,
                    ScopeFile = scopeFile
                });

            try
            {
                await runner.RunAsync(CancellationToken.None);

                sender.Verify(
                    current => current.Send(
                        It.Is<SyncOperationsHistoryCommand>(command =>
                            command.PaymentAccountId == accountId &&
                            command.Checkpoint.StreamId == stream &&
                            command.Checkpoint.Revision == "1"),
                        It.IsAny<CancellationToken>()),
                    Times.Once);
            }
            finally
            {
                File.Delete(scopeFile);
            }
        }

        private static ProjectionRebuildRunner CreateRunner(
            IPaymentsHistoryDocumentsClient history,
            IEventStoreDbStreamReadClient<PaymentOperationEvent> eventStore,
            ISender sender,
            ProjectionRebuildOptions options)
        {
            return new ProjectionRebuildRunner(
                history,
                eventStore,
                sender,
                Options.Create(options),
                Options.Create(new MongoDbOptions
                {
                    ConnectionString = "mongodb://localhost:27017",
                    PaymentsHistory = "history"
                }),
                Options.Create(new EventStoreDbOptions { Url = new Uri("esdb://localhost:2113?tls=false") }),
                NullLogger<ProjectionRebuildRunner>.Instance);
        }

        private static Mock<IEventStoreDbStreamReadClient<PaymentOperationEvent>> BuildEventStore(
            IReadOnlyList<PaymentOperationEvent> events)
        {
            var eventStore = new Mock<IEventStoreDbStreamReadClient<PaymentOperationEvent>>();
            eventStore.Setup(client => client.ReadAsync(
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(events.ToAsyncEnumerable());
            return eventStore;
        }

        private static IReadOnlyList<PaymentOperationEvent> BuildEvents(Guid accountId)
        {
            return [
                new PaymentOperationEvent
                {
                    SequenceNumber = 0,
                    Payload = new FinancialTransaction
                    {
                        PaymentAccountId = accountId,
                        OperationDay = new DateOnly(2026, 1, 5)
                    }
                },

                new PaymentOperationEvent
                {
                    SequenceNumber = 1,
                    Payload = new FinancialTransaction
                    {
                        PaymentAccountId = accountId,
                        OperationDay = new DateOnly(2026, 1, 15)
                    }
                }

            ];
        }

        private static string PeriodFor(Guid accountId) => new DateOnly(2026, 1, 1)
            .ToFinancialPeriod()
            .ToFinancialMonthIdentifier(accountId);
    }
}

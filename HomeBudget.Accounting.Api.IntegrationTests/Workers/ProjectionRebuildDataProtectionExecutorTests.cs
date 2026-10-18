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

using HomeBudget.Accounting.Domain;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Accounting.Workers.OperationsConsumer.Clients;
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
    internal sealed class ProjectionRebuildDataProtectionExecutorTests
    {
        [Test]
        public async Task ExecuteAsync_WithBoundInventory_UsesSuppressedDerivedProjectionOnly()
        {
            await using var scenario = await Scenario.CreateAsync();

            await scenario.Executor.ExecuteAsync(
                scenario.Operation,
                scenario.Fence,
                CancellationToken.None);

            scenario.Sender.Verify(
                sender => sender.Send(
                    It.Is<SyncOperationsHistoryCommand>(command =>
                        command.SuppressExternalSideEffects &&
                        command.PaymentAccountId == scenario.AccountId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            scenario.ExecutionControl.Verify(
                control => control.SetStageAsync(
                    scenario.Fence,
                    "rebuilding-projections",
                    It.IsAny<CancellationToken>()),
                Times.Once);
            scenario.EventStore.Verify(
                client => client.ReadAsync(
                    scenario.StreamId,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_WhenCancellationWasRequested_DoesNotMutateDerivedState()
        {
            await using var scenario = await Scenario.CreateAsync(cancellationRequested: true);

            await scenario.Executor.ExecuteAsync(
                scenario.Operation,
                scenario.Fence,
                CancellationToken.None);

            scenario.Sender.VerifyNoOtherCalls();
        }

        [Test]
        public async Task ExecuteAsync_WhenActiveGenerationChanged_FailsBeforeSourceReadOrMutation()
        {
            await using var scenario = await Scenario.CreateAsync();
            scenario.GenerationResolver
                .Setup(resolver => resolver.GetActiveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LedgerGenerationScope("ledger-a", "generation-8", 8, "\"token-8\"")
                {
                    EnvironmentId = "disposable"
                });

            var execute = async () => await scenario.Executor.ExecuteAsync(
                scenario.Operation,
                scenario.Fence,
                CancellationToken.None);

            await execute.Should().ThrowAsync<LedgerGenerationChangedException>();
            scenario.EventStore.VerifyNoOtherCalls();
            scenario.Sender.VerifyNoOtherCalls();
        }

        [Test]
        public async Task ExecuteAsync_WhenInventoryBytesChangeUnderSameOperationIdentity_ReturnsConflict()
        {
            await using var scenario = await Scenario.CreateAsync();
            var changedPlan = scenario.Plan with
            {
                Items =
                [
                    scenario.Plan.Items[0] with
                    {
                        ExpectedEventCount = scenario.Plan.Items[0].ExpectedEventCount + 1
                    }

                ]
            };
            changedPlan = changedPlan with
            {
                InventoryFingerprint = ProjectionRebuildInventoryFingerprint.Compute(changedPlan)
            };
            await scenario.WritePlanAsync(changedPlan);

            var execute = async () => await scenario.Executor.ExecuteAsync(
                scenario.Operation,
                scenario.Fence,
                CancellationToken.None);

            await execute.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*does not bind the immutable scope inventory*");
            scenario.EventStore.VerifyNoOtherCalls();
            scenario.Sender.VerifyNoOtherCalls();
        }

        [Test]
        public async Task ReconcileAsync_AfterResume_ValidatesExactPublishedHead()
        {
            await using var scenario = await Scenario.CreateAsync();
            scenario.History.Setup(client => client.DiscoverProjectionScopesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    new PaymentHistoryProjectionScope(
                        scenario.AccountId,
                        scenario.Period,
                        scenario.StreamId,
                        1)
                ]);

            await scenario.Executor.ReconcileAsync(
                scenario.Operation,
                scenario.Fence,
                CancellationToken.None);

            scenario.ExecutionControl.Verify(
                control => control.SetStageAsync(
                    scenario.Fence,
                    "reconciling-projections",
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        private sealed class Scenario : IAsyncDisposable
        {
            private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

            private Scenario()
            {
            }

            public Guid AccountId { get; private init; }
            public string Period { get; private init; }
            public string StreamId { get; private init; }
            public string PlanPath { get; private init; }
            public ProjectionRebuildPlan Plan { get; private set; }
            public DataProtectionOperation Operation { get; private init; }
            public DataProtectionOperationFenceContext Fence { get; private init; }
            public ProjectionRebuildDataProtectionExecutor Executor { get; private init; }
            public Mock<ISender> Sender { get; private init; }
            public Mock<IPaymentsHistoryDocumentsClient> History { get; private init; }
            public Mock<IEventStoreDbStreamReadClient<PaymentOperationEvent>> EventStore { get; private init; }
            public Mock<IActiveLedgerGenerationResolver> GenerationResolver { get; private init; }
            public Mock<IDataProtectionOperationExecutionControl> ExecutionControl { get; private init; }

            public static async Task<Scenario> CreateAsync(bool cancellationRequested = false)
            {
                var accountId = Guid.NewGuid();
                var period = new DateOnly(2026, 1, 1)
                    .ToFinancialPeriod()
                    .ToFinancialMonthIdentifier(accountId);
                var streamId = PaymentOperationNamesGenerator.GenerateForAccountMonthStream(period);
                var planPath = Path.Combine(Path.GetTempPath(), $"managed-projection-{Guid.NewGuid():N}.json");
                var plan = new ProjectionRebuildPlan(
                    "disposable-target",
                    "localhost:27017",
                    "history",
                    "localhost:2113",
                    DateTime.UtcNow,
                    [new ProjectionRebuildPlanItem(accountId, period, streamId, 1, 2, null)])
                {
                    EnvironmentId = "disposable",
                    LedgerId = "ledger-a",
                    LedgerGenerationId = "generation-7",
                    ExternalSideEffectsSuppressed = true
                };
                plan = plan with
                {
                    InventoryFingerprint = ProjectionRebuildInventoryFingerprint.Compute(plan)
                };
                await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, JsonOptions));

                var history = new Mock<IPaymentsHistoryDocumentsClient>();
                var events = BuildEvents(accountId);
                var eventStore = new Mock<IEventStoreDbStreamReadClient<PaymentOperationEvent>>();
                eventStore.Setup(client => client.ReadAsync(
                        streamId,
                        It.IsAny<int>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(events.ToAsyncEnumerable());
                var sender = new Mock<ISender>();
                sender.Setup(current => current.Send(
                        It.IsAny<SyncOperationsHistoryCommand>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result<decimal>.Succeeded(20m));
                var generation = new LedgerGenerationScope(
                    "ledger-a",
                    "generation-7",
                    7,
                    "\"token-7\"")
                {
                    EnvironmentId = "disposable"
                };
                var generationResolver = new Mock<IActiveLedgerGenerationResolver>();
                generationResolver.Setup(resolver => resolver.GetActiveAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(generation);
                var executionControl = new Mock<IDataProtectionOperationExecutionControl>();
                executionControl.Setup(control => control.IsCancellationRequestedAsync(
                        It.IsAny<DataProtectionOperationFenceContext>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(cancellationRequested);
                executionControl.Setup(control => control.SetStageAsync(
                        It.IsAny<DataProtectionOperationFenceContext>(),
                        It.IsAny<string>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                var runner = new ProjectionRebuildRunner(
                    history.Object,
                    eventStore.Object,
                    sender.Object,
                    Options.Create(new ProjectionRebuildOptions
                    {
                        ManagedExecutionEnabled = true,
                        OldWritersStopped = true,
                        ExpectedTarget = "disposable-target",
                        ConfirmTarget = "disposable-target",
                        ScopeFile = planPath,
                        MaxScopes = 10
                    }),
                    Options.Create(new MongoDbOptions
                    {
                        ConnectionString = "mongodb://localhost:27017",
                        PaymentsHistory = "history"
                    }),
                    Options.Create(new EventStoreDbOptions
                    {
                        Url = new Uri("esdb://localhost:2113?tls=false")
                    }),
                    NullLogger<ProjectionRebuildRunner>.Instance,
                    generationResolver.Object);
                var operation = new DataProtectionOperation
                {
                    OperationId = Guid.NewGuid(),
                    OperationKind = DataProtectionOperationKinds.ProjectionRebuild,
                    EnvironmentId = "disposable",
                    LedgerId = "ledger-a",
                    GenerationId = "generation-7",
                    PlanFingerprint = ProjectionRebuildPlanBinding.ComputeOperationFingerprint(
                        "disposable",
                        "ledger-a",
                        "generation-7",
                        plan.InventoryFingerprint)
                };
                var fence = new DataProtectionOperationFenceContext
                {
                    OperationId = operation.OperationId,
                    LeaseId = Guid.NewGuid(),
                    ScopeKey = "ledger:test",
                    OwnerId = "test-owner",
                    FenceToken = 1
                };
                return new Scenario
                {
                    AccountId = accountId,
                    Period = period,
                    StreamId = streamId,
                    PlanPath = planPath,
                    Plan = plan,
                    Operation = operation,
                    Fence = fence,
                    Executor = new ProjectionRebuildDataProtectionExecutor(
                        runner,
                        generationResolver.Object,
                        executionControl.Object),
                    Sender = sender,
                    History = history,
                    EventStore = eventStore,
                    GenerationResolver = generationResolver,
                    ExecutionControl = executionControl
                };
            }

            public async Task WritePlanAsync(ProjectionRebuildPlan plan)
            {
                Plan = plan;
                await File.WriteAllTextAsync(PlanPath, JsonSerializer.Serialize(plan, JsonOptions));
            }

            public ValueTask DisposeAsync()
            {
                File.Delete(PlanPath);
                return ValueTask.CompletedTask;
            }

            private static IReadOnlyList<PaymentOperationEvent> BuildEvents(Guid accountId)
            {
                return
                [
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
        }
    }
}

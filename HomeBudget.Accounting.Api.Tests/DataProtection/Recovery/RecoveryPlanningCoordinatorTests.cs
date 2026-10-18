using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HomeBudget.Accounting.Infrastructure.Configuration;
using HomeBudget.Accounting.Infrastructure.DataProtection.Recovery;
using Microsoft.Extensions.DependencyInjection;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.Recovery
{
    [TestFixture]
    internal sealed class RecoveryPlanningCoordinatorTests
    {
        [Test]
        public async Task PlanAsync_LoadsVerifiedEvidenceAndReturnsImmutablePlan()
        {
            var source = new FakeEvidenceSource(RecoveryPlanningScenario.Inventory());
            var coordinator = new RecoveryPlanningCoordinator(source, new RecoveryPlanSelector());

            var plan = await coordinator.PlanAsync(
                RecoveryPlanningScenario.Request(),
                CancellationToken.None);

            source.LoadCount.Should().Be(1);
            plan.ActivationAllowed.Should().BeFalse();
            plan.BaselineRecoverySetId.Should().Be(RecoveryPlanningScenario.BaselineId);
        }

        [Test]
        public async Task PlanAsync_WhenCancelled_DoesNotLoadEvidence()
        {
            var source = new FakeEvidenceSource(RecoveryPlanningScenario.Inventory());
            var coordinator = new RecoveryPlanningCoordinator(source, new RecoveryPlanSelector());
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await FluentActions.Awaiting(() => coordinator.PlanAsync(
                    RecoveryPlanningScenario.Request(),
                    cancellation.Token))
                .Should().ThrowAsync<OperationCanceledException>();
            source.LoadCount.Should().Be(0);
        }

        [Test]
        public void RegisterRecoveryPlanning_WhenExplicitlyInvoked_RegistersCoordinatorAndEvidenceSource()
        {
            var services = new ServiceCollection();

            services.RegisterRecoveryPlanning<RegisteredEvidenceSource>();
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            scope.ServiceProvider.GetRequiredService<RecoveryPlanningCoordinator>().Should().NotBeNull();
            scope.ServiceProvider.GetRequiredService<IRecoveryPlanningEvidenceSource>()
                .Should().BeOfType<RegisteredEvidenceSource>();
        }

        private sealed class FakeEvidenceSource : IRecoveryPlanningEvidenceSource
        {
            private readonly RecoveryEvidenceInventory _inventory;

            public FakeEvidenceSource(RecoveryEvidenceInventory inventory)
            {
                _inventory = inventory;
            }

            public int LoadCount { get; private set; }

            public Task<RecoveryEvidenceInventory> LoadAsync(
                RecoveryPlanningRequest request,
                CancellationToken cancellationToken)
            {
                LoadCount++;
                return Task.FromResult(_inventory);
            }
        }

        private sealed class RegisteredEvidenceSource : IRecoveryPlanningEvidenceSource
        {
            public RegisteredEvidenceSource()
            {
            }

            public Task<RecoveryEvidenceInventory> LoadAsync(
                RecoveryPlanningRequest request,
                CancellationToken cancellationToken) =>
                Task.FromResult(RecoveryPlanningScenario.Inventory());
        }
    }
}

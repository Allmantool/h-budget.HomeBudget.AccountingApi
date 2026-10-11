using System;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using MediatR;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Accounting.Infrastructure.DataProtection.Capture;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.PIpelines;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Tests.Pipelines
{
    [TestFixture]
    public sealed class MaintenanceAdmissionBehaviorTests
    {
        [Test]
        public async Task Handle_WhenPaymentAdmissionIsClosed_DoesNotInvokeCommandHandler()
        {
            var fence = new Mock<IMaintenanceAdmissionFence>();
            fence.Setup(service => service.AcquireAdmissionPermitAsync(
                    It.IsAny<string>(),
                    "legacy",
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new MaintenanceAdmissionClosedException());
            var behavior = new MaintenanceAdmissionBehavior<AddPaymentOperationCommand, Result<Guid>>(
                fence.Object,
                CreateGenerationResolver());
            var nextCalled = false;
            var command = new AddPaymentOperationCommand(new FinancialTransaction())
            {
                ExpectedGeneration = LedgerGenerationScope.Legacy
            };

            var act = () => behavior.Handle(
                command,
                _ =>
                {
                    nextCalled = true;
                    return Task.FromResult(Result<Guid>.Succeeded(Guid.NewGuid()));
                },
                CancellationToken.None);

            await act.Should().ThrowAsync<MaintenanceAdmissionClosedException>();
            nextCalled.Should().BeFalse();
        }

        [Test]
        public async Task Handle_WhenAdmissionPermitIsGranted_InvokesCanonicalTransferHandlerOnce()
        {
            var fence = CreateOpenFence();
            var behavior = new MaintenanceAdmissionBehavior<ApplyTransferCommand, Result<Guid>>(
                fence,
                CreateGenerationResolver());
            var nextCalls = 0;
            var command = new ApplyTransferCommand(new CrossAccountsTransferOperation())
            {
                ExpectedGeneration = LedgerGenerationScope.Legacy
            };

            await behavior.Handle(
                command,
                _ =>
                {
                    nextCalls++;
                    return Task.FromResult(Result<Guid>.Succeeded(Guid.NewGuid()));
                },
                CancellationToken.None);

            nextCalls.Should().Be(1);
        }

        [Test]
        public async Task Handle_WhenRequestIsAReadOrStatusQuery_DoesNotConsultAdmissionFence()
        {
            var fence = new Mock<IMaintenanceAdmissionFence>(MockBehavior.Strict);
            var behavior = new MaintenanceAdmissionBehavior<string, string>(
                fence.Object,
                CreateGenerationResolver());

            var result = await behavior.Handle(
                "status-query",
                _ => Task.FromResult("available"),
                CancellationToken.None);

            result.Should().Be("available");
            fence.VerifyNoOtherCalls();
        }

        private static IMaintenanceAdmissionFence CreateOpenFence()
        {
            var fence = new Mock<IMaintenanceAdmissionFence>();
            fence.Setup(service => service.AcquireAdmissionPermitAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<IMaintenanceAdmissionPermit>());
            return fence.Object;
        }

        private static IActiveLedgerGenerationResolver CreateGenerationResolver()
        {
            var resolver = new Mock<IActiveLedgerGenerationResolver>();
            resolver.Setup(service => service.GetActiveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(LedgerGenerationScope.Legacy);
            return resolver.Object;
        }
    }
}

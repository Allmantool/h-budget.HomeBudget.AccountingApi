using System;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using MediatR;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Accounts.Models;
using HomeBudget.Components.Categories.Clients.Interfaces;
using HomeBudget.Components.Categories.Models;
using HomeBudget.Components.Contractors.Clients.Interfaces;
using HomeBudget.Components.Contractors.Models;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Factories;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Tests.Services
{
    [TestFixture]
    public class PaymentOperationsServiceTests
    {
        [Test]
        public async Task CreateAsync_WhenCategoryAndContractorExist_ThenOperationCommandIsSent()
        {
            var accountId = Guid.NewGuid();
            var categoryId = Guid.NewGuid();
            var contractorId = Guid.NewGuid();
            var operationId = Guid.NewGuid();
            var dependencies = BuildDependencies(accountId, categoryId, contractorId, operationId);
            var sut = dependencies.BuildService();

            var result = await sut.CreateAsync(
                accountId,
                BuildPayload(categoryId.ToString(), contractorId.ToString()),
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                result.IsSucceeded.Should().BeTrue();
                result.Payload.Should().Be(operationId);
            });

            dependencies.Mediator.Verify(
                x => x.Send(It.IsAny<AddPaymentOperationCommand>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task CreateAsync_WhenPaymentAccountDoesNotExist_ThenFailureIsReturned()
        {
            var accountId = Guid.NewGuid();
            var categoryId = Guid.NewGuid();
            var contractorId = Guid.NewGuid();
            var dependencies = BuildDependencies(
                accountId,
                categoryId,
                contractorId,
                Guid.NewGuid(),
                accountExists: false);
            var sut = dependencies.BuildService();

            var result = await sut.CreateAsync(
                accountId,
                BuildPayload(categoryId.ToString(), contractorId.ToString()),
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                result.IsSucceeded.Should().BeFalse();
                result.StatusMessage.Should().Contain("payment account");
            });

            dependencies.Mediator.Verify(
                x => x.Send(It.IsAny<AddPaymentOperationCommand>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task CreateAsync_WhenCategoryReferenceDoesNotExist_ThenFailureIsReturned()
        {
            var accountId = Guid.NewGuid();
            var categoryId = Guid.NewGuid();
            var contractorId = Guid.NewGuid();
            var dependencies = BuildDependencies(
                accountId,
                categoryId,
                contractorId,
                Guid.NewGuid(),
                categoryExists: false);
            var sut = dependencies.BuildService();

            var result = await sut.CreateAsync(
                accountId,
                BuildPayload(categoryId.ToString(), contractorId.ToString()),
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                result.IsSucceeded.Should().BeFalse();
                result.StatusMessage.Should().Contain("category");
                result.StatusMessage.Should().Contain(categoryId.ToString());
            });

            dependencies.Mediator.Verify(
                x => x.Send(It.IsAny<AddPaymentOperationCommand>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task CreateAsync_WhenContractorReferenceDoesNotExist_ThenFailureIsReturned()
        {
            var accountId = Guid.NewGuid();
            var categoryId = Guid.NewGuid();
            var contractorId = Guid.NewGuid();
            var dependencies = BuildDependencies(
                accountId,
                categoryId,
                contractorId,
                Guid.NewGuid(),
                contractorExists: false);
            var sut = dependencies.BuildService();

            var result = await sut.CreateAsync(
                accountId,
                BuildPayload(categoryId.ToString(), contractorId.ToString()),
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                result.IsSucceeded.Should().BeFalse();
                result.StatusMessage.Should().Contain("contractor");
                result.StatusMessage.Should().Contain(contractorId.ToString());
            });

            dependencies.Mediator.Verify(
                x => x.Send(It.IsAny<AddPaymentOperationCommand>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task CreateAsync_WhenOptionalReferencesAreMissing_ThenCommandUsesEmptyReferences()
        {
            var accountId = Guid.NewGuid();
            var operationId = Guid.NewGuid();
            var dependencies = BuildDependencies(accountId, Guid.NewGuid(), Guid.NewGuid(), operationId);
            var sut = dependencies.BuildService();

            var result = await sut.CreateAsync(
                accountId,
                BuildPayload(null, string.Empty),
                CancellationToken.None);

            result.IsSucceeded.Should().BeTrue();

            dependencies.Mediator.Verify(
                x => x.Send(
                    It.Is<AddPaymentOperationCommand>(
                        c => c.OperationForAdd.CategoryId == Guid.Empty &&
                             c.OperationForAdd.ContractorId == Guid.Empty),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task CreateAsync_WhenMigrationPayloadHasInvalidReference_ThenFailsBeforeCommandDispatch()
        {
            var accountId = Guid.NewGuid();
            var dependencies = BuildDependencies(accountId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var sut = dependencies.BuildService();

            var result = await sut.CreateAsync(
                accountId,
                BuildPayload("not-a-category-guid", null),
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                result.IsSucceeded.Should().BeFalse();
                result.StatusMessage.Should().Contain("categoryId");
            });

            dependencies.Mediator.Verify(
                x => x.Send(It.IsAny<AddPaymentOperationCommand>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task UpdateAsync_WhenCategoryReferenceDoesNotExist_ThenFailureIsReturnedBeforeCommandDispatch()
        {
            var accountId = Guid.NewGuid();
            var operationId = Guid.NewGuid();
            var categoryId = Guid.NewGuid();
            var contractorId = Guid.NewGuid();
            var dependencies = BuildDependencies(
                accountId,
                categoryId,
                contractorId,
                Guid.NewGuid(),
                categoryExists: false);
            dependencies.HistoryClient
                .Setup(x => x.GetByIdAsync(accountId, operationId))
                .ReturnsAsync(new PaymentHistoryDocument
                {
                    Payload = new PaymentOperationHistoryRecord
                    {
                        Record = new FinancialTransaction
                        {
                            CategoryId = Guid.Empty,
                            ContractorId = Guid.Empty,
                            Key = operationId,
                            PaymentAccountId = accountId
                        }
                    }
                });
            var sut = dependencies.BuildService();

            var result = await sut.UpdateAsync(
                accountId,
                operationId,
                BuildPayload(categoryId.ToString(), contractorId.ToString()),
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                result.IsSucceeded.Should().BeFalse();
                result.StatusMessage.Should().Contain("category");
            });

            dependencies.Mediator.Verify(
                x => x.Send(It.IsAny<UpdatePaymentOperationCommand>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        private static PaymentOperationPayload BuildPayload(string categoryId, string contractorId)
        {
            return new PaymentOperationPayload
            {
                Amount = 100m,
                CategoryId = categoryId,
                Comment = "test-payment",
                ContractorId = contractorId,
                OperationDate = new DateOnly(2026, 05, 09),
                ScopeOperationId = 1
            };
        }

        private static PaymentOperationsServiceDependencies BuildDependencies(
            Guid accountId,
            Guid categoryId,
            Guid contractorId,
            Guid operationId,
            bool accountExists = true,
            bool categoryExists = true,
            bool contractorExists = true)
        {
            var accountClient = new Mock<IPaymentAccountDocumentClient>();
            accountClient
                .Setup(x => x.GetByIdAsync(accountId.ToString()))
                .ReturnsAsync(Result<PaymentAccountDocument>.Succeeded(accountExists
                    ? new PaymentAccountDocument { Payload = new PaymentAccount { Key = accountId } }
                    : null));

            var categoryClient = new Mock<ICategoryDocumentsClient>();
            categoryClient
                .Setup(x => x.GetByIdAsync(categoryId))
                .ReturnsAsync(Result<CategoryDocument>.Succeeded(categoryExists
                    ? new CategoryDocument
                    {
                        Payload = new Category(CategoryTypes.Income, ["test-category"])
                        {
                            Key = categoryId
                        }
                    }
                    : null));
            categoryClient
                .Setup(x => x.GetByIdAsync(It.Is<Guid>(id => id != categoryId)))
                .ReturnsAsync(Result<CategoryDocument>.Succeeded(null));

            var contractorClient = new Mock<IContractorDocumentsClient>();
            contractorClient
                .Setup(x => x.GetByIdAsync(contractorId))
                .ReturnsAsync(Result<ContractorDocument>.Succeeded(contractorExists
                    ? new ContractorDocument
                    {
                        Payload = new Contractor(["test-contractor"])
                        {
                            Key = contractorId
                        }
                    }
                    : null));
            contractorClient
                .Setup(x => x.GetByIdAsync(It.Is<Guid>(id => id != contractorId)))
                .ReturnsAsync(Result<ContractorDocument>.Succeeded(null));

            var historyClient = new Mock<IPaymentsHistoryDocumentsClient>();
            var mediator = new Mock<ISender>();
            mediator
                .Setup(x => x.Send(It.IsAny<AddPaymentOperationCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid>.Succeeded(operationId));

            return new PaymentOperationsServiceDependencies(
                accountClient,
                categoryClient,
                contractorClient,
                historyClient,
                mediator);
        }

        private sealed class PaymentOperationsServiceDependencies
        {
            private readonly Mock<IPaymentAccountDocumentClient> _accountClient;
            private readonly Mock<ICategoryDocumentsClient> _categoryClient;
            private readonly Mock<IContractorDocumentsClient> _contractorClient;
            private readonly Mock<IPaymentsHistoryDocumentsClient> _historyClient;

            public PaymentOperationsServiceDependencies(
                Mock<IPaymentAccountDocumentClient> accountClient,
                Mock<ICategoryDocumentsClient> categoryClient,
                Mock<IContractorDocumentsClient> contractorClient,
                Mock<IPaymentsHistoryDocumentsClient> historyClient,
                Mock<ISender> mediator)
            {
                _accountClient = accountClient;
                _categoryClient = categoryClient;
                _contractorClient = contractorClient;
                _historyClient = historyClient;
                Mediator = mediator;
            }

            public Mock<ISender> Mediator { get; }
            public Mock<IPaymentsHistoryDocumentsClient> HistoryClient => _historyClient;

            public PaymentOperationsService BuildService()
            {
                return new PaymentOperationsService(
                    _accountClient.Object,
                    _categoryClient.Object,
                    _contractorClient.Object,
                    _historyClient.Object,
                    Mediator.Object,
                    new FinancialTransactionFactory());
            }
        }
    }
}

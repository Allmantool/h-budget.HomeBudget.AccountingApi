using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Controllers;
using HomeBudget.Accounting.Api.Idempotency;
using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Api.Models.Contractor;
using HomeBudget.Accounting.Api.Models.PaymentAccount;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Accounts.Models;
using HomeBudget.Components.Categories.Clients.Interfaces;
using HomeBudget.Components.Categories.Models;
using HomeBudget.Components.Contractors.Clients.Interfaces;
using HomeBudget.Components.Contractors.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.Tests
{
    [TestFixture]
    public sealed class IdempotentReferenceCreateTests
    {
        private static readonly Guid TargetId = Guid.Parse("15d47229-e0f9-4328-bf28-2173ac36fd88");

        [TestCase(null, "source")]
        [TestCase(" ", "source")]
        [TestCase("key", null)]
        [TestCase("key", " ")]
        public void ContextFactory_WhenRequiredIdentityIsMissing_RejectsRequest(
            string idempotencyKey,
            string sourceReference)
        {
            var created = ReferenceCreateContextFactory.TryCreate(
                idempotencyKey,
                "fingerprint",
                sourceReference,
                out var context);

            created.Should().BeFalse();
            context.Should().BeNull();
        }

        [Test]
        public void ContextFactory_WhenValuesExceedLimits_RejectsRequest()
        {
            ReferenceCreateContextFactory.TryCreate(
                new string('k', 201),
                "fingerprint",
                "source",
                out _).Should().BeFalse();
            ReferenceCreateContextFactory.TryCreate(
                "key",
                "fingerprint",
                new string('s', 501),
                out _).Should().BeFalse();
        }

        [Test]
        public void ContextFactory_WhenIdentityIsValid_HashesKeyAndTrimsSource()
        {
            var created = ReferenceCreateContextFactory.TryCreate(
                "migration-key",
                "fingerprint",
                "  FamilyPro12:ACCOUNT:1  ",
                out var context);

            created.Should().BeTrue();
            context.IdempotencyKeyHash.Should().NotBe("migration-key");
            context.RequestFingerprint.Should().Be("fingerprint");
            context.SourceReference.Should().Be("FamilyPro12:ACCOUNT:1");
        }

        [TestCase(IdempotentDocumentWriteState.Created, true)]
        [TestCase(IdempotentDocumentWriteState.Existing, false)]
        public async Task AccountCreate_WhenRegistrationSucceeds_ReturnsTargetAndPublishesOnlyNewAccount(
            IdempotentDocumentWriteState state,
            bool shouldPublish)
        {
            var channel = Channel.CreateUnbounded<AccountRecord>();
            var client = new AccountClientStub(new IdempotentDocumentWriteResult(TargetId, state));
            var controller = WithIdempotencyHeader(new PaymentAccountsController(
                channel,
                null,
                client,
                new PaymentAccountFactoryStub()));

            var result = await controller.CreateNewAsync(CreateAccountRequest(), CancellationToken.None);

            result.Value.Payload.Should().Be(TargetId);
            client.Context.SourceReference.Should().Be("FamilyPro12:ACCOUNT:1");
            channel.Reader.TryRead(out var record).Should().Be(shouldPublish);
            if (shouldPublish)
            {
                record.Id.Should().Be(TargetId);
            }
        }

        [Test]
        public async Task AccountCreate_WhenRegistrationConflicts_ReturnsConflictWithoutPublishing()
        {
            var channel = Channel.CreateUnbounded<AccountRecord>();
            var controller = WithIdempotencyHeader(new PaymentAccountsController(
                channel,
                null,
                new AccountClientStub(new IdempotentDocumentWriteResult(TargetId, IdempotentDocumentWriteState.Conflict)),
                new PaymentAccountFactoryStub()));

            var result = await controller.CreateNewAsync(CreateAccountRequest(), CancellationToken.None);

            result.Result.Should().BeOfType<ConflictObjectResult>();
            channel.Reader.TryRead(out _).Should().BeFalse();
        }

        [TestCase(IdempotentDocumentWriteState.Created, false)]
        [TestCase(IdempotentDocumentWriteState.Conflict, true)]
        public async Task CategoryCreate_ReflectsRegistrationOutcome(
            IdempotentDocumentWriteState state,
            bool isConflict)
        {
            var client = new CategoryClientStub(new IdempotentDocumentWriteResult(TargetId, state));
            var controller = WithIdempotencyHeader(new CategoriesController(
                null,
                client,
                new CategoryFactoryStub()));

            var result = await controller.CreateNewAsync(
                new CreateCategoryRequest
                {
                    CategoryType = CategoryTypes.Expense.Key,
                    NameNodes = ["Food"],
                    SourceReference = "FamilyPro12:CATEGORY:1"
                },
                CancellationToken.None);

            (result.Result is ConflictObjectResult).Should().Be(isConflict);
            if (!isConflict)
            {
                result.Value.Payload.Should().Be(TargetId);
            }

            client.Context.SourceReference.Should().Be("FamilyPro12:CATEGORY:1");
            client.Context.RequestFingerprint.Should().NotBeNullOrWhiteSpace();
        }

        [TestCase(IdempotentDocumentWriteState.Existing, false)]
        [TestCase(IdempotentDocumentWriteState.Conflict, true)]
        public async Task ContractorCreate_ReflectsRegistrationOutcome(
            IdempotentDocumentWriteState state,
            bool isConflict)
        {
            var client = new ContractorClientStub(new IdempotentDocumentWriteResult(TargetId, state));
            var controller = WithIdempotencyHeader(new ContractorsController(
                client,
                new ContractorFactoryStub()));

            var result = await controller.CreateNewAsync(
                new CreateContractorRequest
                {
                    NameNodes = ["Store"],
                    SourceReference = "FamilyPro12:PAYEE:1"
                },
                CancellationToken.None);

            (result.Result is ConflictObjectResult).Should().Be(isConflict);
            if (!isConflict)
            {
                result.Value.Payload.Should().Be(TargetId);
            }

            client.Context.SourceReference.Should().Be("FamilyPro12:PAYEE:1");
            client.Context.RequestFingerprint.Should().NotBeNullOrWhiteSpace();
        }

        [Test]
        public async Task AccountCreate_WhenSourceReferenceIsMissing_ReturnsBadRequest()
        {
            var controller = WithIdempotencyHeader(new PaymentAccountsController(
                Channel.CreateUnbounded<AccountRecord>(),
                null,
                new AccountClientStub(new IdempotentDocumentWriteResult(TargetId, IdempotentDocumentWriteState.Created)),
                new PaymentAccountFactoryStub()));
            var request = CreateAccountRequest();
            request.SourceReference = null;

            var result = await controller.CreateNewAsync(request, CancellationToken.None);

            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Test]
        public async Task CategoryCreate_WhenSourceReferenceIsMissing_ReturnsBadRequest()
        {
            var controller = WithIdempotencyHeader(new CategoriesController(
                null,
                new CategoryClientStub(new IdempotentDocumentWriteResult(TargetId, IdempotentDocumentWriteState.Created)),
                new CategoryFactoryStub()));

            var result = await controller.CreateNewAsync(
                new CreateCategoryRequest
                {
                    CategoryType = CategoryTypes.Expense.Key,
                    NameNodes = ["Food"]
                },
                CancellationToken.None);

            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Test]
        public async Task ContractorCreate_WhenSourceReferenceIsMissing_ReturnsBadRequest()
        {
            var controller = WithIdempotencyHeader(new ContractorsController(
                new ContractorClientStub(new IdempotentDocumentWriteResult(TargetId, IdempotentDocumentWriteState.Created)),
                new ContractorFactoryStub()));

            var result = await controller.CreateNewAsync(
                new CreateContractorRequest { NameNodes = ["Store"] },
                CancellationToken.None);

            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Test]
        public async Task AccountCreate_WithoutIdempotencyHeader_PreservesLegacyCreateAndPublishes()
        {
            var channel = Channel.CreateUnbounded<AccountRecord>();
            var client = new AccountClientStub(
                new IdempotentDocumentWriteResult(TargetId, IdempotentDocumentWriteState.Created),
                Result<Guid>.Succeeded(TargetId));
            var controller = WithHttpContext(new PaymentAccountsController(
                channel,
                null,
                client,
                new PaymentAccountFactoryStub()));

            var result = await controller.CreateNewAsync(CreateAccountRequest(), CancellationToken.None);

            result.Value.Payload.Should().Be(TargetId);
            client.IdempotentCalls.Should().Be(0);
            channel.Reader.TryRead(out var record).Should().BeTrue();
            record.Id.Should().Be(TargetId);
        }

        [Test]
        public async Task CategoryCreate_WithoutIdempotencyHeader_PreservesLegacyFailure()
        {
            var client = new CategoryClientStub(
                new IdempotentDocumentWriteResult(TargetId, IdempotentDocumentWriteState.Created),
                Result<Guid>.Failure("failed"));
            var controller = WithHttpContext(new CategoriesController(null, client, new CategoryFactoryStub()));

            var result = await controller.CreateNewAsync(
                new CreateCategoryRequest
                {
                    CategoryType = CategoryTypes.Expense.Key,
                    NameNodes = ["Food"]
                },
                CancellationToken.None);

            result.Value.IsSucceeded.Should().BeFalse();
            result.Value.StatusMessage.Should().Be("failed");
            client.IdempotentCalls.Should().Be(0);
        }

        [Test]
        public async Task ContractorCreate_WithoutIdempotencyHeader_PreservesLegacySuccess()
        {
            var client = new ContractorClientStub(
                new IdempotentDocumentWriteResult(TargetId, IdempotentDocumentWriteState.Created),
                Result<Guid>.Succeeded(TargetId));
            var controller = WithHttpContext(new ContractorsController(client, new ContractorFactoryStub()));

            var result = await controller.CreateNewAsync(
                new CreateContractorRequest { NameNodes = ["Store"] },
                CancellationToken.None);

            result.Value.Payload.Should().Be(TargetId);
            client.IdempotentCalls.Should().Be(0);
        }

        private static T WithIdempotencyHeader<T>(T controller)
            where T : ControllerBase
        {
            WithHttpContext(controller);
            controller.Request.Headers["Idempotency-Key"] = "migration-key";
            return controller;
        }

        private static T WithHttpContext<T>(T controller)
            where T : ControllerBase
        {
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            return controller;
        }

        private static CreatePaymentAccountRequest CreateAccountRequest() => new()
        {
            AccountType = AccountTypes.Cash.Key,
            Agent = "Family Pro",
            Currency = "BYN",
            Description = "Cash",
            SourceReference = "FamilyPro12:ACCOUNT:1"
        };

        private sealed class PaymentAccountFactoryStub : IPaymentAccountFactory
        {
            public PaymentAccount Create(
                string agent,
                decimal initialBalance,
                string currency,
                string description,
                AccountTypes accountType) => new()
                {
                    Key = TargetId,
                    Agent = agent,
                    InitialBalance = initialBalance,
                    Balance = initialBalance,
                    Currency = currency,
                    Description = description,
                    Type = accountType
                };
        }

        private sealed class CategoryFactoryStub : ICategoryFactory
        {
            public Category Create(CategoryTypes categoryType, IEnumerable<string> nameNodes) =>
                new(categoryType, nameNodes) { Key = TargetId };
        }

        private sealed class ContractorFactoryStub : IContractorFactory
        {
            public Contractor Create(IEnumerable<string> nameNodes) => new(nameNodes) { Key = TargetId };
        }

        private sealed class AccountClientStub(
            IdempotentDocumentWriteResult result,
            Result<Guid> legacyResult = null) : IPaymentAccountDocumentClient
        {
            public IdempotentDocumentWriteContext Context { get; private set; }
            public int IdempotentCalls { get; private set; }

            public Task<IdempotentDocumentWriteResult> InsertIdempotentAsync(
                PaymentAccount payload,
                IdempotentDocumentWriteContext context,
                CancellationToken cancellationToken)
            {
                IdempotentCalls++;
                Context = context;
                return Task.FromResult(result);
            }

            public Task<Result<IReadOnlyCollection<PaymentAccountDocument>>> GetAsync() => throw new NotSupportedException();
            public Task<Result<PaymentAccountDocument>> GetByIdAsync(string paymentAccountId) => throw new NotSupportedException();
            public Task<Result<Guid>> InsertOneAsync(PaymentAccount payload) => Task.FromResult(legacyResult);
            public Task<Result<Guid>> RemoveAsync(string paymentAccountId) => throw new NotSupportedException();
            public Task<Result<Guid>> UpdateAsync(string requestPaymentAccountGuid, PaymentAccount paymentAccountForUpdate) => throw new NotSupportedException();
        }

        private sealed class CategoryClientStub(
            IdempotentDocumentWriteResult result,
            Result<Guid> legacyResult = null) : ICategoryDocumentsClient
        {
            public int IdempotentCalls { get; private set; }
            public IdempotentDocumentWriteContext Context { get; private set; }

            public Task<IdempotentDocumentWriteResult> InsertIdempotentAsync(
                Category payload,
                IdempotentDocumentWriteContext context,
                CancellationToken cancellationToken)
            {
                IdempotentCalls++;
                Context = context;
                return Task.FromResult(result);
            }

            public Task<Result<IReadOnlyCollection<CategoryDocument>>> GetAsync() => throw new NotSupportedException();
            public Task<Result<CategoryDocument>> GetByIdAsync(Guid contractorId) => throw new NotSupportedException();
            public Task<Result<Guid>> InsertOneAsync(Category payload) => Task.FromResult(legacyResult);
            public Task<bool> CheckIfExistsAsync(string contractorKey) => throw new NotSupportedException();
            public Task<Result<IReadOnlyCollection<CategoryDocument>>> GetByIdsAsync(IEnumerable<Guid> operationIds) => throw new NotSupportedException();
        }

        private sealed class ContractorClientStub(
            IdempotentDocumentWriteResult result,
            Result<Guid> legacyResult = null) : IContractorDocumentsClient
        {
            public int IdempotentCalls { get; private set; }
            public IdempotentDocumentWriteContext Context { get; private set; }

            public Task<IdempotentDocumentWriteResult> InsertIdempotentAsync(
                Contractor payload,
                IdempotentDocumentWriteContext context,
                CancellationToken cancellationToken)
            {
                IdempotentCalls++;
                Context = context;
                return Task.FromResult(result);
            }

            public Task<Result<IReadOnlyCollection<ContractorDocument>>> GetAsync() => throw new NotSupportedException();
            public Task<Result<ContractorDocument>> GetByIdAsync(Guid contractorId) => throw new NotSupportedException();
            public Task<Result<Guid>> InsertOneAsync(Contractor payload) => Task.FromResult(legacyResult);
            public Task<bool> CheckIfExistsAsync(string contractorKey) => throw new NotSupportedException();
        }
    }
}

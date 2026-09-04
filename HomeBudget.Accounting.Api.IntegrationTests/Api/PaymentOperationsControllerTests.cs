using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.Extensions;
using HomeBudget.Accounting.Api.IntegrationTests.Filters;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Api.Models.History;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Accounting.Api.Models.PaymentAccount;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Core.Constants;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [NonParallelizable]
    [Order(IntegrationTestOrderIndex.PaymentOperationsControllerTests)]
    public class PaymentOperationsControllerTests : BaseIntegrationTests
    {
        private const string ApiHost = $"/{Endpoints.PaymentOperations}";

        private readonly OperationsTestWebApp _sut = new();
        private RestClient _restClient;
        private RestClient _restClientAllowingHttpErrors;

        [OneTimeSetUp]
        public override async Task SetupAsync()
        {
            await _sut.InitAsync();
            await base.SetupAsync();

            _restClient = _sut.RestHttpClient;
            _restClientAllowingHttpErrors = _sut.RestHttpClientAllowingHttpErrors;
        }

        [Test]
        public async Task CreateNewOperation_WhenCreateAnOperation_ShouldAddExtraPaymentOperationEvent()
        {
            var paymentAccountId = (await SavePaymentAccountAsync()).Payload;

            var operationAmountBefore = (await GetHistoryRecordsAsync(paymentAccountId)).Count;

            var categoryIdResult = await SaveCategoryAsync(CategoryTypes.Income, nameof(CreateNewOperation_WhenCreateAnOperation_ShouldAddExtraPaymentOperationEvent));

            var requestBody = new CreateOperationRequest
            {
                Amount = 100,
                Comment = "New operation",
                CategoryId = categoryIdResult.Payload,
                ContractorId = string.Empty,
                OperationDate = new DateOnly(2024, 1, 3)
            };

            var postCreateRequest = new RestRequest($"{ApiHost}/{paymentAccountId}", Method.Post)
                .AddJsonBody(requestBody);

            var response = await _restClient.ExecuteWithDelayAsync<Result<CreateOperationResponse>>(
                postCreateRequest,
                executionDelayAfterInMs: 5000);

            response.IsSuccessful.Should().Be(true, DescribeResponse(response));
            response.Data.IsSucceeded.Should().BeTrue(DescribeResponse(response));

            var result = response.Data;
            var payload = result.Payload;
            var operationId = Guid.Parse(payload.PaymentOperationId);

            var operationAmountAfter = (await WaitForHistoryRecordsAsync(
                paymentAccountId,
                records => records.Any(record => record.Record.Key == operationId),
                $"created operation '{operationId}' is visible in payment history",
                [operationId])).Count;

            Assert.Multiple(() =>
            {
                operationId.Should().NotBeEmpty();
                Guid.TryParse(payload.PaymentAccountId, out _).Should().BeTrue();
                operationAmountBefore.Should().BeLessThan(operationAmountAfter);
            });
        }

        [Test]
        public async Task CreateNewOperation_WhenCreateAnOperation_ShouldAddExtraPaymentOperationHistoryRecord()
        {
            var paymentAccountId = (await SavePaymentAccountAsync()).Payload;

            var operationsAmountBefore = (await GetHistoryRecordsAsync(paymentAccountId)).Count;

            var operationIds = new List<Guid>();

            foreach (var i in Enumerable.Range(1, 7))
            {
                var requestBody = new CreateOperationRequest
                {
                    Amount = 10 + i,
                    Comment = $"New operation - {i}",
                    CategoryId = (await SaveCategoryAsync(CategoryTypes.Income, $"{nameof(CreateOperationRequest)}-{i}")).Payload,
                    ContractorId = string.Empty,
                    OperationDate = new DateOnly(2023, 12, 15)
                };

                var postCreateRequest = new RestRequest($"{ApiHost}/{paymentAccountId}", Method.Post)
                    .AddJsonBody(requestBody);

                var createResponse = await _restClient.ExecuteWithDelayAsync<Result<CreateOperationResponse>>(postCreateRequest);
                createResponse.IsSuccessful.Should().BeTrue(DescribeResponse(createResponse));
                createResponse.Data.IsSucceeded.Should().BeTrue(DescribeResponse(createResponse));

                var operationId = Guid.Parse(createResponse.Data.Payload.PaymentOperationId);
                operationIds.Add(operationId);

                await WaitForHistoryRecordAsync(
                    paymentAccountId,
                    operationId,
                    record => record.Record.Amount == requestBody.Amount &&
                              record.Record.OperationDay == requestBody.OperationDate,
                    cancellationToken: TestContext.CurrentContext.CancellationToken);
            }

            var operationAmountAfter = (await WaitForHistoryRecordsAsync(
                paymentAccountId,
                records => operationIds.All(operationId => records.Any(record => record.Record.Key == operationId)),
                "all created operations are visible in payment history",
                operationIds)).Count;

            operationsAmountBefore.Should().BeLessThan(operationAmountAfter);
        }

        [Test]
        public async Task CreateNewOperation_WhenCreateAnOperation_PaymentAccountBalanceShouldBeIncreased()
        {
            var categoryIdResult = await SaveCategoryAsync(CategoryTypes.Income, nameof(CreateNewOperation_WhenCreateAnOperation_PaymentAccountBalanceShouldBeIncreased));

            var accountId = (await SavePaymentAccountAsync()).Payload;

            var balanceBefore = (await GetPaymentsAccountAsync(accountId)).InitialBalance;

            var requestBody = new CreateOperationRequest
            {
                Amount = 100,
                Comment = "New operation",
                CategoryId = categoryIdResult.Payload,
                ContractorId = string.Empty,
                OperationDate = new DateOnly(2024, 1, 6)
            };

            var postCreateRequest = new RestRequest($"{ApiHost}/{accountId}", Method.Post)
                .AddJsonBody(requestBody);

            var createResponse = await _restClient.ExecuteWithDelayAsync<Result<CreateOperationResponse>>(postCreateRequest);
            createResponse.IsSuccessful.Should().BeTrue(DescribeResponse(createResponse));
            createResponse.Data.IsSucceeded.Should().BeTrue(DescribeResponse(createResponse));

            var operationId = Guid.Parse(createResponse.Data.Payload.PaymentOperationId);
            await WaitForHistoryRecordAsync(
                accountId,
                operationId,
                record => record.Record.Amount == requestBody.Amount,
                $"created operation '{operationId}' with amount {requestBody.Amount} is visible");

            var operationAmountAfter = (await WaitForPaymentAccountAsync(
                accountId,
                account => account.Balance == balanceBefore + requestBody.Amount,
                $"balance equals initial balance {balanceBefore} plus operation amount {requestBody.Amount}",
                [operationId])).Balance;

            operationAmountAfter.Should().Be(balanceBefore + requestBody.Amount);
        }

        [Test]
        public async Task DeleteById_WithValidOperationRef_ThenSuccessful()
        {
            const decimal initialBalance = 11.2m;
            var paymentAccountId = (await SavePaymentAccountAsync()).Payload;

            var categoryIdResult = await SaveCategoryAsync(CategoryTypes.Income, nameof(DeleteById_WithValidOperationRef_ThenSuccessful));

            var requestBody = new CreateOperationRequest
            {
                Amount = 25.24m,
                CategoryId = categoryIdResult.Payload,
                ContractorId = string.Empty,
                Comment = "Some test",
                OperationDate = new DateOnly(2024, 1, 6),
            };

            var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                .AddJsonBody(requestBody);

            var createOperationResult = await _restClient.ExecuteWithDelayAsync<Result<CreateOperationResponse>>(postCreateRequest);
            createOperationResult.IsSuccessful.Should().BeTrue(DescribeResponse(createOperationResult));
            createOperationResult.Data.IsSucceeded.Should().BeTrue(DescribeResponse(createOperationResult));

            var newOperationId = createOperationResult.Data.Payload.PaymentOperationId;
            var parsedOperationId = Guid.Parse(newOperationId);
            await WaitForHistoryRecordAsync(
                paymentAccountId,
                parsedOperationId,
                record => record.Record.Amount == requestBody.Amount,
                $"created operation '{parsedOperationId}' is visible before delete");

            var addOperationBalance = (await WaitForPaymentAccountAsync(
                paymentAccountId,
                account => account.Balance == requestBody.Amount + initialBalance,
                $"balance includes created operation '{parsedOperationId}'",
                [parsedOperationId])).Balance;

            var deleteOperationRequest = new RestRequest($"{ApiHost}/{paymentAccountId}/{newOperationId}", Method.Delete);

            var deleteResponse = await _restClient.ExecuteWithDelayAsync<Result<RemoveOperationResponse>>(deleteOperationRequest);
            Assert.Multiple(() =>
            {
                deleteResponse.IsSuccessful.Should().BeTrue(DescribeResponse(deleteResponse));
                deleteResponse.Data.IsSucceeded.Should().BeTrue(DescribeResponse(deleteResponse));
            });

            var deleteOperationBalance = (await WaitForPaymentAccountAsync(
                paymentAccountId,
                account => account.Balance == initialBalance,
                $"balance returns to initial balance after deleting operation '{parsedOperationId}'",
                [parsedOperationId])).Balance;

            await PaymentProjectionWaiter.WaitForHistoryRecordRemovedAsync(
                _restClient,
                paymentAccountId,
                parsedOperationId);

            Assert.Multiple(() =>
            {
                addOperationBalance.Should().Be(requestBody.Amount + initialBalance);
                deleteOperationBalance.Should().Be(initialBalance);
            });
        }

        [Test]
        public async Task DeleteById_WithValidOperationRef_OperationsAmountShouldBeDescriesed()
        {
            var paymentAccountId = (await SavePaymentAccountAsync()).Payload;

            var categoryIdResult = await SaveCategoryAsync(
                CategoryTypes.Income,
                nameof(DeleteById_WithValidOperationRef_OperationsAmountShouldBeDescriesed));

            var requestBody = new CreateOperationRequest
            {
                Amount = 25.24m,
                CategoryId = categoryIdResult.Payload,
                ContractorId = string.Empty,
                Comment = "Some test",
                OperationDate = new DateOnly(2024, 1, 6),
            };

            var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                .AddJsonBody(requestBody);

            var postResult = await _restClient.ExecuteWithDelayAsync<Result<CreateOperationResponse>>(postCreateRequest);
            postResult.IsSuccessful.Should().BeTrue(DescribeResponse(postResult));
            postResult.Data.IsSucceeded.Should().BeTrue(DescribeResponse(postResult));

            var newOperationId = postResult.Data.Payload.PaymentOperationId;
            var parsedOperationId = Guid.Parse(newOperationId);
            await WaitForHistoryRecordAsync(paymentAccountId, parsedOperationId);

            var operationAmountBefore = (await GetHistoryRecordsAsync(paymentAccountId)).Count;

            var deleteOperationRequest = new RestRequest($"{ApiHost}/{paymentAccountId}/{newOperationId}", Method.Delete);

            var deleteResponse = await _restClient.ExecuteWithDelayAsync<Result<RemoveOperationResponse>>(deleteOperationRequest);
            Assert.Multiple(() =>
            {
                deleteResponse.IsSuccessful.Should().BeTrue(DescribeResponse(deleteResponse));
                deleteResponse.Data.IsSucceeded.Should().BeTrue(DescribeResponse(deleteResponse));
            });

            var operationAmountAfter = (await WaitForHistoryRecordsAsync(
                paymentAccountId,
                records => records.All(record => record.Record.Key != parsedOperationId))).Count;

            Assert.Multiple(() =>
            {
                operationAmountBefore.Should().BeGreaterThan(operationAmountAfter);
            });
        }

        [Test]
        public async Task DeleteById_WithInValidOperationRef_ThenFail()
        {
            const string operationId = "invalid-operation-ref";
            const string accountId = "invalid-acc-ref";

            var deleteOperationRequest = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Delete);

            var response = await _restClientAllowingHttpErrors.ExecuteAllowingHttpErrorAsync<Result<RemoveOperationResponse>>(
                deleteOperationRequest,
                [HttpStatusCode.BadRequest]);

            var result = response.ShouldBeHttpFailureWithDomainFailure(
                HttpStatusCode.BadRequest,
                "invalid payment operation delete route ids should be rejected");

            result.StatusMessage.Should().Contain("Invalid", response.DescribeResponse());
            result.StatusMessage.Should().Contain("payment account", response.DescribeResponse());
        }

        [Test]
        public async Task Update_WithInvalid_ThenFail()
        {
            const string operationId = "invalid-operation-ref";
            const string accountId = "invalid-acc-ref";

            var categoryIdResult = await SaveCategoryAsync(CategoryTypes.Income, nameof(Update_WithInvalid_ThenFail));

            var requestBody = new UpdateOperationRequest
            {
                Amount = 100,
                Comment = "Some description",
                CategoryId = categoryIdResult.Payload,
                ContractorId = string.Empty
            };

            var patchUpdateOperation = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Patch)
                .AddJsonBody(requestBody);

            var response = await _restClientAllowingHttpErrors.ExecuteAllowingHttpErrorAsync<Result<UpdateOperationResponse>>(
                patchUpdateOperation,
                [HttpStatusCode.BadRequest]);

            var result = response.ShouldBeHttpFailureWithDomainFailure(
                HttpStatusCode.BadRequest,
                "invalid payment operation update route ids should be rejected");

            result.StatusMessage.Should().Contain("Invalid", response.DescribeResponse());
            result.StatusMessage.Should().Contain("payment account", response.DescribeResponse());
        }

        [Test]
        public async Task Update_WithMissingOperationForValidAccount_ThenFail()
        {
            var accountId = (await SavePaymentAccountAsync()).Payload;

            var categoryIdResult = await SaveCategoryAsync(CategoryTypes.Income, nameof(Update_WithMissingOperationForValidAccount_ThenFail));

            var requestBody = new UpdateOperationRequest
            {
                Amount = 100,
                Comment = "Some description",
                CategoryId = categoryIdResult.Payload,
                ContractorId = string.Empty
            };

            var missingOperationId = Guid.NewGuid();

            var patchUpdateOperation = new RestRequest($"{ApiHost}/{accountId}/{missingOperationId}", Method.Patch)
                .AddJsonBody(requestBody);

            var response = await _restClientAllowingHttpErrors.ExecuteAllowingHttpErrorAsync<Result<UpdateOperationResponse>>(
                patchUpdateOperation,
                [HttpStatusCode.NotFound]);

            var result = response.ShouldBeHttpFailureWithDomainFailure(
                HttpStatusCode.NotFound,
                "missing operation update should return not found");

            Assert.Multiple(() =>
            {
                result.Payload.Should().BeNull(response.DescribeResponse());
                result.StatusMessage.Should().Contain(missingOperationId.ToString(), response.DescribeResponse());
                result.StatusMessage.Should().Contain(accountId.ToString(), response.DescribeResponse());
                result.StatusMessage.Should().Contain("hasn't been found", response.DescribeResponse());
            });
        }

        [Test]
        public async Task Update_WithValid_ThenSuccessful()
        {
            var accountId = (await SavePaymentAccountAsync()).Payload;

            var createCategoryId = await SaveCategoryAsync(CategoryTypes.Income, $"{nameof(Update_WithValid_ThenSuccessful)}-seed");
            var createRequestBody = new CreateOperationRequest
            {
                Amount = 12.34m,
                Comment = "seed-operation",
                CategoryId = createCategoryId.Payload,
                ContractorId = string.Empty,
                OperationDate = new DateOnly(2024, 1, 6)
            };

            var createOperationRequest = new RestRequest($"{ApiHost}/{accountId}", Method.Post)
                .AddJsonBody(createRequestBody);

            var createResponse = await _restClient.ExecuteWithDelayAsync<Result<CreateOperationResponse>>(
                createOperationRequest,
                executionDelayAfterInMs: 1000);

            createResponse.IsSuccessful.Should().BeTrue(DescribeResponse(createResponse));
            createResponse.Data.IsSucceeded.Should().BeTrue(DescribeResponse(createResponse));

            var operationId = Guid.Parse(createResponse.Data.Payload.PaymentOperationId);
            var seededOperation = await WaitForHistoryRecordAsync(accountId, operationId);

            var categoryIdResult = await SaveCategoryAsync(CategoryTypes.Income, nameof(Update_WithValid_ThenSuccessful));

            var requestBody = new UpdateOperationRequest
            {
                Amount = 100,
                Comment = "Some update description",
                CategoryId = categoryIdResult.Payload,
                ContractorId = string.Empty
            };

            var patchUpdateOperation = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Patch)
                .AddJsonBody(requestBody);

            var response = await _sut.RestHttpClient.ExecuteWithDelayAsync<Result<UpdateOperationResponse>>(patchUpdateOperation);

            var result = response.Data;

            Assert.Multiple(() =>
            {
                response.IsSuccessful.Should().BeTrue(DescribeResponse(response));
                result.IsSucceeded.Should().BeTrue(DescribeResponse(response));
                result.StatusMessage.Should().BeNullOrEmpty(DescribeResponse(response));
                result.Payload.PaymentAccountId.Should().Be(accountId.ToString(), DescribeResponse(response));
                result.Payload.PaymentOperationId.Should().Be(operationId.ToString(), DescribeResponse(response));
                seededOperation.Record.PaymentAccountId.Should().Be(accountId);
                seededOperation.Record.Key.Should().Be(operationId);
            });

            var updatedOperation = await WaitForHistoryRecordAsync(
                accountId,
                operationId,
                record => record.Record.Amount == requestBody.Amount &&
                          record.Record.Comment == requestBody.Comment &&
                          record.Record.CategoryId == Guid.Parse(requestBody.CategoryId));

            Assert.Multiple(() =>
            {
                updatedOperation.Record.PaymentAccountId.Should().Be(accountId);
                updatedOperation.Record.Key.Should().Be(operationId);
                updatedOperation.Record.Amount.Should().Be(requestBody.Amount);
                updatedOperation.Record.Comment.Should().Be(requestBody.Comment);
            });
        }

        [Test]
        public async Task Update_WithValid_BalanceShouldBeExpectedlyUpdated()
        {
            var accountId = (await SavePaymentAccountAsync()).Payload;

            var categoryIdResult = await SaveCategoryAsync(CategoryTypes.Income, nameof(Update_WithValid_BalanceShouldBeExpectedlyUpdated));

            var requestCreateBody = new CreateOperationRequest
            {
                Amount = 12.0m,
                Comment = "New operation",
                CategoryId = categoryIdResult.Payload,
                ContractorId = string.Empty,
                OperationDate = new DateOnly(2024, 1, 6),
            };

            var postCreateRequest = new RestRequest($"{ApiHost}/{accountId}", Method.Post)
                .AddJsonBody(requestCreateBody);

            var saveResponseResult = await _restClient.ExecuteWithDelayAsync<Result<CreateOperationResponse>>(postCreateRequest, executionDelayAfterInMs: 1000);
            saveResponseResult.IsSuccessful.Should().BeTrue(DescribeResponse(saveResponseResult));
            saveResponseResult.Data.IsSucceeded.Should().BeTrue(DescribeResponse(saveResponseResult));

            var justCreatedOperationId = Guid.Parse(saveResponseResult.Data.Payload.PaymentOperationId);
            await WaitForHistoryRecordAsync(accountId, justCreatedOperationId);

            var balanceBefore = (await WaitForPaymentAccountAsync(
                accountId,
                account => account.Balance == account.InitialBalance + requestCreateBody.Amount,
                $"balance includes created operation '{justCreatedOperationId}'",
                [justCreatedOperationId])).Balance;

            var requestUpdateBody = new UpdateOperationRequest
            {
                Amount = 17.22m,
                Comment = "Some update description",
                CategoryId = categoryIdResult.Payload,
                ContractorId = string.Empty,
                OperationDate = new DateOnly(2025, 2, 7)
            };

            var patchUpdateOperation = new RestRequest($"{ApiHost}/{accountId}/{justCreatedOperationId}", Method.Patch)
                .AddJsonBody(requestUpdateBody);

            var updateResponse = await _restClient.ExecuteWithDelayAsync<Result<UpdateOperationResponse>>(patchUpdateOperation, executionDelayAfterInMs: 8_000);

            Assert.Multiple(() =>
            {
                updateResponse.IsSuccessful.Should().BeTrue(DescribeResponse(updateResponse));
                updateResponse.Data.IsSucceeded.Should().BeTrue(DescribeResponse(updateResponse));
                updateResponse.Data.StatusMessage.Should().BeNullOrEmpty(DescribeResponse(updateResponse));
            });

            await WaitForHistoryRecordAsync(
                accountId,
                justCreatedOperationId,
                record => record.Record.Amount == requestUpdateBody.Amount &&
                          record.Record.Comment == requestUpdateBody.Comment &&
                          record.Record.OperationDay == requestUpdateBody.OperationDate);

            var balanceAfter = (await WaitForPaymentAccountAsync(
                accountId,
                account => account.Balance == account.InitialBalance + requestUpdateBody.Amount,
                $"balance includes updated operation '{justCreatedOperationId}'",
                [justCreatedOperationId])).Balance;

            balanceBefore.Should().BeLessThan(balanceAfter);
        }

        [Test]
        public async Task Create_WhenIdempotencyKeyIsRetried_ShouldReuseTheCommandAndProjectOneOperation()
        {
            var accountId = (await SavePaymentAccountAsync()).Payload;
            var categoryId = (await SaveCategoryAsync(CategoryTypes.Income, nameof(Create_WhenIdempotencyKeyIsRetried_ShouldReuseTheCommandAndProjectOneOperation))).Payload;
            var requestBody = new CreateOperationRequest
            {
                Amount = 42.50m,
                Comment = "idempotent-create",
                CategoryId = categoryId,
                ContractorId = string.Empty,
                OperationDate = new DateOnly(2025, 3, 4)
            };
            var idempotencyKey = Guid.NewGuid().ToString("N");

            var firstResponse = await _restClient.ExecuteAsync<Result<CreateOperationResponse>>(
                WithIdempotencyKey(new RestRequest($"{ApiHost}/{accountId}", Method.Post).AddJsonBody(requestBody), idempotencyKey));
            var retryResponse = await _restClient.ExecuteAsync<Result<CreateOperationResponse>>(
                WithIdempotencyKey(new RestRequest($"{ApiHost}/{accountId}", Method.Post).AddJsonBody(requestBody), idempotencyKey));

            firstResponse.IsSuccessful.Should().BeTrue(DescribeResponse(firstResponse));
            retryResponse.IsSuccessful.Should().BeTrue(DescribeResponse(retryResponse));
            firstResponse.Data.IsSucceeded.Should().BeTrue(DescribeResponse(firstResponse));
            retryResponse.Data.IsSucceeded.Should().BeTrue(DescribeResponse(retryResponse));
            firstResponse.Data.Payload.IsDuplicate.Should().BeFalse();
            retryResponse.Data.Payload.IsDuplicate.Should().BeTrue();
            retryResponse.Data.Payload.CommandId.Should().Be(firstResponse.Data.Payload.CommandId);
            retryResponse.Data.Payload.PaymentOperationId.Should().Be(firstResponse.Data.Payload.PaymentOperationId);

            var operationId = Guid.Parse(firstResponse.Data.Payload.PaymentOperationId);
            var records = await WaitForHistoryRecordsAsync(
                accountId,
                history => history.Count(record => record.Record.Key == operationId) == 1,
                "the idempotent create operation is projected exactly once",
                [operationId]);

            var statusResponse = await WaitForCommandStatusAsync(accountId, firstResponse.Data.Payload.CommandId);

            statusResponse.IsSuccessful.Should().BeTrue(DescribeResponse(statusResponse));
            statusResponse.Data.IsSucceeded.Should().BeTrue(DescribeResponse(statusResponse));
            statusResponse.Data.Payload.Status.Should().Be("Projected");
            statusResponse.Data.Payload.AcceptedAt.Should().NotBe(default);
            statusResponse.Data.Payload.PublishedAt.Should().NotBeNull();
            statusResponse.Data.Payload.PersistedAt.Should().NotBeNull();
            statusResponse.Data.Payload.ProjectedAt.Should().NotBeNull();
            records.Count(record => record.Record.Key == operationId).Should().Be(1);
        }

        [Test]
        public async Task Create_WhenResponseIsLostAfterDurableAcceptance_ShouldRetryTheOriginalCommandAndProjectOnce()
        {
            var accountId = (await SavePaymentAccountAsync()).Payload;
            var balanceBefore = (await GetPaymentsAccountAsync(accountId)).Balance;
            var categoryId = (await SaveCategoryAsync(CategoryTypes.Income, nameof(Create_WhenResponseIsLostAfterDurableAcceptance_ShouldRetryTheOriginalCommandAndProjectOnce))).Payload;
            var requestBody = new CreateOperationRequest
            {
                Amount = 31m,
                Comment = "response-loss-create",
                CategoryId = categoryId,
                ContractorId = string.Empty,
                OperationDate = new DateOnly(2025, 3, 9)
            };
            var idempotencyKey = Guid.NewGuid().ToString("N");

            Func<Task> firstAttempt = () => _restClient.ExecuteAsync<Result<CreateOperationResponse>>(
                WithIdempotencyKey(new RestRequest($"{ApiHost}/{accountId}", Method.Post)
                    .AddJsonBody(requestBody)
                    .AddHeader(DiscardResponseAfterAcceptedPaymentCommandFilter.HeaderName, "true"), idempotencyKey));

            await firstAttempt.Should().ThrowAsync<HttpRequestException>();

            var retryResponse = await _restClient.ExecuteAsync<Result<CreateOperationResponse>>(
                WithIdempotencyKey(new RestRequest($"{ApiHost}/{accountId}", Method.Post).AddJsonBody(requestBody), idempotencyKey));

            retryResponse.IsSuccessful.Should().BeTrue(DescribeResponse(retryResponse));
            retryResponse.Data.IsSucceeded.Should().BeTrue(DescribeResponse(retryResponse));
            retryResponse.Data.Payload.IsDuplicate.Should().BeTrue();

            var operationId = Guid.Parse(retryResponse.Data.Payload.PaymentOperationId);
            var records = await WaitForHistoryRecordsAsync(
                accountId,
                history => history.Count(record => record.Record.Key == operationId) == 1,
                "the command accepted before response loss is projected exactly once",
                [operationId]);
            var account = await WaitForPaymentAccountAsync(
                accountId,
                paymentAccount => paymentAccount.Balance == balanceBefore + requestBody.Amount,
                "the command accepted before response loss changes the balance exactly once",
                [operationId]);
            var status = await WaitForCommandStatusAsync(accountId, retryResponse.Data.Payload.CommandId);

            records.Count(record => record.Record.Key == operationId).Should().Be(1);
            account.Balance.Should().Be(balanceBefore + requestBody.Amount);
            status.Data.Payload.Status.Should().Be("Projected");
        }

        [Test]
        public async Task Create_WhenIdempotencyKeyIsReusedForDifferentPayload_ShouldReturnConflictWithoutAnotherProjection()
        {
            var accountId = (await SavePaymentAccountAsync()).Payload;
            var categoryId = (await SaveCategoryAsync(CategoryTypes.Income, nameof(Create_WhenIdempotencyKeyIsReusedForDifferentPayload_ShouldReturnConflictWithoutAnotherProjection))).Payload;
            var idempotencyKey = Guid.NewGuid().ToString("N");
            var originalRequest = new CreateOperationRequest
            {
                Amount = 15m,
                Comment = "original-command",
                CategoryId = categoryId,
                ContractorId = string.Empty,
                OperationDate = new DateOnly(2025, 3, 5)
            };

            var firstResponse = await _restClient.ExecuteAsync<Result<CreateOperationResponse>>(
                WithIdempotencyKey(new RestRequest($"{ApiHost}/{accountId}", Method.Post).AddJsonBody(originalRequest), idempotencyKey));
            firstResponse.IsSuccessful.Should().BeTrue(DescribeResponse(firstResponse));
            firstResponse.Data.IsSucceeded.Should().BeTrue(DescribeResponse(firstResponse));

            var changedRequest = originalRequest with { Amount = 16m };
            var conflictResponse = await _restClientAllowingHttpErrors.ExecuteAllowingHttpErrorAsync<Result<CreateOperationResponse>>(
                WithIdempotencyKey(new RestRequest($"{ApiHost}/{accountId}", Method.Post).AddJsonBody(changedRequest), idempotencyKey),
                [HttpStatusCode.Conflict]);

            conflictResponse.StatusCode.Should().Be(HttpStatusCode.Conflict, DescribeResponse(conflictResponse));
            conflictResponse.Data.IsSucceeded.Should().BeFalse(DescribeResponse(conflictResponse));

            var operationId = Guid.Parse(firstResponse.Data.Payload.PaymentOperationId);
            var records = await WaitForHistoryRecordsAsync(
                accountId,
                history => history.Count(record => record.Record.Key == operationId) == 1,
                "the conflicting retry does not create another operation",
                [operationId]);

            records.Count(record => record.Record.Key == operationId).Should().Be(1);
        }

        [Test]
        public async Task Create_WhenConcurrentRetriesUseOneIdempotencyKey_ShouldAcceptOneCommandWithoutServerErrors()
        {
            var accountId = (await SavePaymentAccountAsync()).Payload;
            var categoryId = (await SaveCategoryAsync(CategoryTypes.Income, nameof(Create_WhenConcurrentRetriesUseOneIdempotencyKey_ShouldAcceptOneCommandWithoutServerErrors))).Payload;
            var requestBody = new CreateOperationRequest
            {
                Amount = 23m,
                Comment = "concurrent-idempotent-create",
                CategoryId = categoryId,
                ContractorId = string.Empty,
                OperationDate = new DateOnly(2025, 3, 7)
            };
            var idempotencyKey = Guid.NewGuid().ToString("N");

            var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _restClient.ExecuteAsync<Result<CreateOperationResponse>>(
                WithIdempotencyKey(new RestRequest($"{ApiHost}/{accountId}", Method.Post).AddJsonBody(requestBody), idempotencyKey))));

            responses.Should().OnlyContain(response => (int)response.StatusCode < 500);
            responses.Should().OnlyContain(response => response.IsSuccessful && response.Data != null && response.Data.IsSucceeded);
            responses.Select(response => response.Data.Payload.CommandId).Distinct().Should().ContainSingle();
            responses.Select(response => response.Data.Payload.PaymentOperationId).Distinct().Should().ContainSingle();
            responses.Count(response => !response.Data.Payload.IsDuplicate).Should().Be(1);

            var operationId = Guid.Parse(responses[0].Data.Payload.PaymentOperationId);
            var records = await WaitForHistoryRecordsAsync(
                accountId,
                history => history.Count(record => record.Record.Key == operationId) == 1,
                "concurrent retries are projected as one operation",
                [operationId]);

            records.Count(record => record.Record.Key == operationId).Should().Be(1);
        }

        [Test]
        public async Task UpdateAndDelete_WhenIdempotencyKeysAreRetried_ShouldReuseTheirOriginalCommands()
        {
            var accountId = (await SavePaymentAccountAsync()).Payload;
            var categoryId = (await SaveCategoryAsync(CategoryTypes.Income, nameof(UpdateAndDelete_WhenIdempotencyKeysAreRetried_ShouldReuseTheirOriginalCommands))).Payload;
            var createRequest = new CreateOperationRequest
            {
                Amount = 10m,
                Comment = "before-update",
                CategoryId = categoryId,
                ContractorId = string.Empty,
                OperationDate = new DateOnly(2025, 3, 6)
            };
            var createResponse = await _restClient.ExecuteAsync<Result<CreateOperationResponse>>(
                new RestRequest($"{ApiHost}/{accountId}", Method.Post).AddJsonBody(createRequest));
            createResponse.IsSuccessful.Should().BeTrue(DescribeResponse(createResponse));
            createResponse.Data.IsSucceeded.Should().BeTrue(DescribeResponse(createResponse));

            var operationId = Guid.Parse(createResponse.Data.Payload.PaymentOperationId);
            await WaitForHistoryRecordAsync(accountId, operationId);
            var updateRequest = new UpdateOperationRequest
            {
                Amount = 12m,
                Comment = "after-update",
                CategoryId = categoryId,
                ContractorId = string.Empty,
                OperationDate = createRequest.OperationDate
            };
            var updateKey = Guid.NewGuid().ToString("N");
            var firstUpdate = await _restClient.ExecuteAsync<Result<UpdateOperationResponse>>(
                WithIdempotencyKey(new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Patch).AddJsonBody(updateRequest), updateKey));
            var retryUpdate = await _restClient.ExecuteAsync<Result<UpdateOperationResponse>>(
                WithIdempotencyKey(new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Patch).AddJsonBody(updateRequest), updateKey));

            firstUpdate.Data.IsSucceeded.Should().BeTrue(DescribeResponse(firstUpdate));
            retryUpdate.Data.IsSucceeded.Should().BeTrue(DescribeResponse(retryUpdate));
            retryUpdate.Data.Payload.IsDuplicate.Should().BeTrue();
            retryUpdate.Data.Payload.CommandId.Should().Be(firstUpdate.Data.Payload.CommandId);
            await WaitForHistoryRecordAsync(accountId, operationId, record => record.Record.Amount == updateRequest.Amount);

            var deleteKey = Guid.NewGuid().ToString("N");
            var firstDelete = await _restClient.ExecuteAsync<Result<RemoveOperationResponse>>(
                WithIdempotencyKey(new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Delete), deleteKey));
            var retryDelete = await _restClient.ExecuteAsync<Result<RemoveOperationResponse>>(
                WithIdempotencyKey(new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Delete), deleteKey));

            firstDelete.Data.IsSucceeded.Should().BeTrue(DescribeResponse(firstDelete));
            retryDelete.Data.IsSucceeded.Should().BeTrue(DescribeResponse(retryDelete));
            retryDelete.Data.Payload.IsDuplicate.Should().BeTrue();
            retryDelete.Data.Payload.CommandId.Should().Be(firstDelete.Data.Payload.CommandId);
            await PaymentProjectionWaiter.WaitForHistoryRecordRemovedAsync(_restClient, accountId, operationId);
        }

        [Test]
        public async Task CommandStatus_WhenCommandDoesNotExist_ShouldReturnNotFoundWithoutCommandMetadata()
        {
            var response = await _restClientAllowingHttpErrors.ExecuteAllowingHttpErrorAsync<Result<PaymentCommandStatusResponse>>(
                new RestRequest($"{ApiHost}/{Guid.NewGuid()}/commands/{Guid.NewGuid()}"),
                [HttpStatusCode.NotFound]);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound, DescribeResponse(response));
            response.Data?.Payload.Should().BeNull();
        }

        [Test]
        public async Task CommandStatus_WhenCommandBelongsToAnotherAccount_ShouldReturnNotFoundWithoutCommandMetadata()
        {
            var ownerAccountId = (await SavePaymentAccountAsync()).Payload;
            var categoryId = (await SaveCategoryAsync(CategoryTypes.Income, nameof(CommandStatus_WhenCommandBelongsToAnotherAccount_ShouldReturnNotFoundWithoutCommandMetadata))).Payload;
            var createResponse = await _restClient.ExecuteAsync<Result<CreateOperationResponse>>(
                WithIdempotencyKey(
                    new RestRequest($"{ApiHost}/{ownerAccountId}", Method.Post).AddJsonBody(new CreateOperationRequest
                    {
                        Amount = 8m,
                        Comment = "cross-account-status",
                        CategoryId = categoryId,
                        ContractorId = string.Empty,
                        OperationDate = new DateOnly(2025, 3, 8)
                    }),
                    Guid.NewGuid().ToString("N")));

            createResponse.IsSuccessful.Should().BeTrue(DescribeResponse(createResponse));
            createResponse.Data.IsSucceeded.Should().BeTrue(DescribeResponse(createResponse));

            var response = await _restClientAllowingHttpErrors.ExecuteAllowingHttpErrorAsync<Result<PaymentCommandStatusResponse>>(
                new RestRequest($"{ApiHost}/{Guid.NewGuid()}/commands/{createResponse.Data.Payload.CommandId}"),
                [HttpStatusCode.NotFound]);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound, DescribeResponse(response));
            response.Data?.Payload.Should().BeNull();
        }

        [Test]
        public async Task Create_WhenWorkerIsRestartedAfterCommandAcceptance_ShouldProjectOneOperationAndPreserveLifecycle()
        {
            var accountId = (await SavePaymentAccountAsync()).Payload;
            var balanceBefore = (await GetPaymentsAccountAsync(accountId)).Balance;
            var categoryId = (await SaveCategoryAsync(CategoryTypes.Income, nameof(Create_WhenWorkerIsRestartedAfterCommandAcceptance_ShouldProjectOneOperationAndPreserveLifecycle))).Payload;

            await _sut.StopWorkersAsync();

            var createResponse = await _restClient.ExecuteAsync<Result<CreateOperationResponse>>(
                WithIdempotencyKey(
                    new RestRequest($"{ApiHost}/{accountId}", Method.Post).AddJsonBody(new CreateOperationRequest
                    {
                        Amount = 27m,
                        Comment = "worker-restart-create",
                        CategoryId = categoryId,
                        ContractorId = string.Empty,
                        OperationDate = new DateOnly(2025, 3, 10)
                    }),
                    Guid.NewGuid().ToString("N")));

            createResponse.IsSuccessful.Should().BeTrue(DescribeResponse(createResponse));
            createResponse.Data.IsSucceeded.Should().BeTrue(DescribeResponse(createResponse));

            await _sut.RestartWorkersAsync();

            var operationId = Guid.Parse(createResponse.Data.Payload.PaymentOperationId);
            var records = await WaitForHistoryRecordsAsync(
                accountId,
                history => history.Count(record => record.Record.Key == operationId) == 1,
                "the command accepted before worker restart is projected exactly once",
                [operationId]);
            var account = await WaitForPaymentAccountAsync(
                accountId,
                paymentAccount => paymentAccount.Balance == balanceBefore + 27m,
                "the command accepted before worker restart changes the balance exactly once",
                [operationId]);
            var status = await WaitForCommandStatusAsync(accountId, createResponse.Data.Payload.CommandId);

            records.Count(record => record.Record.Key == operationId).Should().Be(1);
            account.Balance.Should().Be(balanceBefore + 27m);
            status.Data.Payload.Status.Should().Be("Projected");
        }

        [Test]
        public async Task Create_WhenKafkaRedeliversTheSameDurableMessage_ShouldCommitDuplicateWithoutAnotherBusinessEffect()
        {
            var accountId = (await SavePaymentAccountAsync()).Payload;
            var balanceBefore = (await GetPaymentsAccountAsync(accountId)).Balance;
            var categoryId = (await SaveCategoryAsync(CategoryTypes.Income, nameof(Create_WhenKafkaRedeliversTheSameDurableMessage_ShouldCommitDuplicateWithoutAnotherBusinessEffect))).Payload;
            var createResponse = await _restClient.ExecuteAsync<Result<CreateOperationResponse>>(
                WithIdempotencyKey(
                    new RestRequest($"{ApiHost}/{accountId}", Method.Post).AddJsonBody(new CreateOperationRequest
                    {
                        Amount = 19m,
                        Comment = "kafka-redelivery-create",
                        CategoryId = categoryId,
                        ContractorId = string.Empty,
                        OperationDate = new DateOnly(2025, 3, 11)
                    }),
                    Guid.NewGuid().ToString("N")));

            createResponse.IsSuccessful.Should().BeTrue(DescribeResponse(createResponse));
            createResponse.Data.IsSucceeded.Should().BeTrue(DescribeResponse(createResponse));

            var commandId = createResponse.Data.Payload.CommandId;
            var operationId = Guid.Parse(createResponse.Data.Payload.PaymentOperationId);
            await WaitForCommandStatusAsync(accountId, commandId);
            var payload = await GetOutboxPayloadAsync(commandId);
            var duplicateDelivery = await ProduceDuplicateKafkaMessageAsync(accountId, commandId, payload);

            await WaitForPaymentConsumerCommitAsync(duplicateDelivery.TopicPartitionOffset);

            var records = await WaitForHistoryRecordsAsync(
                accountId,
                history => history.Count(record => record.Record.Key == operationId) == 1,
                "Kafka redelivery has no second projected operation",
                [operationId]);
            var account = await WaitForPaymentAccountAsync(
                accountId,
                paymentAccount => paymentAccount.Balance == balanceBefore + 19m,
                "Kafka redelivery has no second balance effect",
                [operationId]);
            var status = await WaitForCommandStatusAsync(accountId, commandId);

            records.Count(record => record.Record.Key == operationId).Should().Be(1);
            account.Balance.Should().Be(balanceBefore + 19m);
            status.Data.Payload.Status.Should().Be("Projected");
        }

        private async Task<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>> GetHistoryRecordsAsync(Guid paymentAccountId)
        {
            var getPaymentHistoryRecordsRequest = new RestRequest($"{Endpoints.PaymentsHistory}/{paymentAccountId}");

            var getResponse = await _restClient
                .ExecuteWithDelayAsync<Result<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>>>(getPaymentHistoryRecordsRequest, executionDelayBeforeInMs: 2000);

            return getResponse.Data.Payload;
        }

        private async Task<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>> WaitForHistoryRecordsAsync(
            Guid paymentAccountId,
            Func<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>, bool> condition,
            string conditionDescription = "custom payment history condition",
            IEnumerable<Guid> knownOperationIds = null,
            CancellationToken cancellationToken = default)
        {
            return await PaymentProjectionWaiter.WaitForHistoryRecordsAsync(
                _restClient,
                paymentAccountId,
                condition,
                conditionDescription,
                knownOperationIds,
                cancellationToken: cancellationToken);
        }

        private async Task<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>> GetHistoryRecordsOnceAsync(Guid paymentAccountId)
        {
            var request = new RestRequest($"{Endpoints.PaymentsHistory}/{paymentAccountId}");
            var response = await _restClient.ExecuteAsync<Result<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>>>(request);

            if (!response.IsSuccessful || response.Data?.Payload == null)
            {
                return Array.Empty<PaymentOperationHistoryRecordResponse>();
            }

            return response.Data.Payload;
        }

        private async Task<PaymentAccount> GetPaymentsAccountAsync(Guid paymentAccountId)
        {
            var getPaymentsAccountRequest = new RestRequest($"{Endpoints.PaymentAccounts}/byId/{paymentAccountId}");

            var getResponse = await _restClient
                .ExecuteWithDelayAsync<Result<PaymentAccount>>(getPaymentsAccountRequest, executionDelayBeforeInMs: 5000);

            return getResponse.Data.Payload;
        }

        private async Task<PaymentAccount> WaitForPaymentAccountAsync(
            Guid paymentAccountId,
            Func<PaymentAccount, bool> condition,
            string conditionDescription = null,
            IEnumerable<Guid> knownOperationIds = null,
            CancellationToken cancellationToken = default)
        {
            return await PaymentProjectionWaiter.WaitForPaymentAccountAsync(
                _restClient,
                paymentAccountId,
                condition,
                conditionDescription ?? "custom payment account condition",
                knownOperationIds,
                cancellationToken);
        }

        private async Task<PaymentAccount> GetPaymentsAccountOnceAsync(Guid paymentAccountId)
        {
            var getPaymentsAccountRequest = new RestRequest($"{Endpoints.PaymentAccounts}/byId/{paymentAccountId}");
            var getResponse = await _restClient.ExecuteAsync<Result<PaymentAccount>>(getPaymentsAccountRequest);

            if (!getResponse.IsSuccessful || getResponse.Data?.Payload == null)
            {
                return null;
            }

            return getResponse.Data.Payload;
        }

        private async Task<PaymentOperationHistoryRecordResponse> WaitForHistoryRecordAsync(
            Guid paymentAccountId,
            Guid operationId,
            Func<PaymentOperationHistoryRecordResponse, bool> condition = null,
            string conditionDescription = null,
            CancellationToken cancellationToken = default)
        {
            return await PaymentProjectionWaiter.WaitForHistoryRecordAsync(
                _restClient,
                paymentAccountId,
                operationId,
                condition,
                conditionDescription,
                cancellationToken);
        }

        private async Task<PaymentOperationHistoryRecordResponse> GetHistoryRecordAsync(Guid paymentAccountId, Guid operationId)
        {
            var request = new RestRequest($"{Endpoints.PaymentsHistory}/{paymentAccountId}/byId/{operationId}");
            var response = await _restClient.ExecuteAsync<Result<PaymentOperationHistoryRecordResponse>>(request);

            if (!response.IsSuccessful || response.Data?.Payload == null)
            {
                return null;
            }

            return response.Data.Payload;
        }

        private async Task<RestResponse<Result<PaymentCommandStatusResponse>>> WaitForCommandStatusAsync(
            Guid paymentAccountId,
            string commandId)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            RestResponse<Result<PaymentCommandStatusResponse>> response = null;

            do
            {
                response = await _restClient.ExecuteAsync<Result<PaymentCommandStatusResponse>>(
                    new RestRequest($"{ApiHost}/{paymentAccountId}/commands/{commandId}"));

                if (response.Data?.Payload?.Status == "Projected")
                {
                    return response;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
            while (DateTime.UtcNow < deadline);

            Assert.Fail($"Payment command '{commandId}' did not reach the Projected status. {DescribeResponse(response)}");
            return response;
        }

        private static string DescribeResponse<T>(RestResponse<Result<T>> response)
        {
            if (response == null)
            {
                return "Response was null.";
            }

            return $"HTTP {(int)response.StatusCode} {response.StatusCode}, transport-success={response.IsSuccessful}, rest-error='{response.ErrorMessage}', domain-success={response.Data?.IsSucceeded}, status='{response.Data?.StatusMessage}', content='{response.Content}'";
        }

        private async Task<Result<string>> SaveCategoryAsync(CategoryTypes categoryType, string category)
        {
            var requestSaveBody = new CreateCategoryRequest
            {
                CategoryType = categoryType.Key,
                NameNodes =
                [
                    nameof(categoryType),
                    category
                ]
            };

            var saveCategoryRequest = new RestRequest($"{Endpoints.Categories}", Method.Post)
                .AddJsonBody(requestSaveBody);

            var paymentsHistoryResponse = await _restClient
                .ExecuteWithDelayAsync<Result<string>>(saveCategoryRequest, executionDelayAfterInMs: 1000);

            return paymentsHistoryResponse.Data;
        }

        private async Task<Result<Guid>> SavePaymentAccountAsync()
        {
            var requestSaveBody = new CreatePaymentAccountRequest
            {
                InitialBalance = 11.2m,
                Description = "test-account",
                AccountType = AccountTypes.Deposit.Key,
                Agent = "Personal",
                Currency = "usd"
            };

            var saveCategoryRequest = new RestRequest($"{Endpoints.PaymentAccounts}", Method.Post)
                .AddJsonBody(requestSaveBody);

            var paymentsHistoryResponse = await _restClient
                .ExecuteWithDelayAsync<Result<Guid>>(saveCategoryRequest, executionDelayAfterInMs: 1000);

            return paymentsHistoryResponse.Data;
        }

        private static RestRequest WithIdempotencyKey(RestRequest request, string idempotencyKey)
        {
            return request.AddHeader("Idempotency-Key", idempotencyKey);
        }

        private async Task<string> GetOutboxPayloadAsync(string commandId)
        {
            await using var connection = new SqlConnection(TestContainers.AccountingDbConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "SELECT Payload FROM dbo.OutboxAccountPayments WHERE MessageId = @MessageId;",
                connection);
            command.Parameters.AddWithValue("@MessageId", commandId);

            var payload = await command.ExecuteScalarAsync();
            payload.Should().BeOfType<string>($"outbox command '{commandId}' must be durably registered before Kafka redelivery");
            return (string)payload;
        }

        private async Task<DeliveryResult<string, string>> ProduceDuplicateKafkaMessageAsync(
            Guid accountId,
            string commandId,
            string payload)
        {
            var bootstrapServers = await TestContainers.KafkaContainer.GetReachableBootstrapAsync();
            using var producer = new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = bootstrapServers
            }).Build();

            return await producer.ProduceAsync(
                BaseTopics.AccountingPayments,
                new Message<string, string>
                {
                    Key = accountId.ToString(),
                    Value = payload,
                    Headers = new Headers
                    {
                        { KafkaMessageHeaders.MessageId, Encoding.UTF8.GetBytes(commandId) }
                    }
                });
        }

        private async Task WaitForPaymentConsumerCommitAsync(TopicPartitionOffset duplicateDelivery)
        {
            var bootstrapServers = await TestContainers.KafkaContainer.GetReachableBootstrapAsync();
            using var consumer = new ConsumerBuilder<Ignore, Ignore>(new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = "accounting.payments.group",
                EnableAutoCommit = false
            }).Build();
            var deadline = DateTime.UtcNow.AddSeconds(30);
            TopicPartitionOffset committedOffset = default;

            do
            {
                committedOffset = consumer
                    .Committed([duplicateDelivery.TopicPartition], TimeSpan.FromSeconds(2))
                    .Single();
                if (committedOffset.Offset > duplicateDelivery.Offset)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
            while (DateTime.UtcNow < deadline);

            Assert.Fail(
                $"Payment consumer group did not commit Kafka redelivery at {duplicateDelivery}. " +
                $"Last committed offset was {committedOffset}.");
        }
    }
}

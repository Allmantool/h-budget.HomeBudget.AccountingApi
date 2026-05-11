using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.Extensions;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Api.Models.History;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Accounting.Api.Models.PaymentAccount;
using HomeBudget.Accounting.Domain.Enumerations;
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

        [OneTimeSetUp]
        public override async Task SetupAsync()
        {
            await _sut.InitAsync();
            await base.SetupAsync();

            _restClient = _sut.RestHttpClient;
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

            var response = await _restClient.ExecuteAsync<Result<RemoveOperationResponse>>(deleteOperationRequest);

            var result = response.Data;

            result.IsSucceeded.Should().BeFalse();
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

            var response = await _restClient.ExecuteAsync<Result<UpdateOperationResponse>>(patchUpdateOperation);

            var result = response.Data;

            result.IsSucceeded.Should().BeFalse();
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

            var response = await _restClient.ExecuteAsync<Result<UpdateOperationResponse>>(patchUpdateOperation);

            Assert.Multiple(() =>
            {
                response.IsSuccessful.Should().BeTrue(DescribeResponse(response));
                response.Data.IsSucceeded.Should().BeFalse(DescribeResponse(response));
                response.Data.StatusMessage.Should().Contain(missingOperationId.ToString(), DescribeResponse(response));
                response.Data.StatusMessage.Should().Contain(accountId.ToString(), DescribeResponse(response));
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
    }
}

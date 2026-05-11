using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    [Order(IntegrationTestOrderIndex.PaymentsHistoryControllerTests)]
    public class PaymentsHistoryControllerTests : BaseIntegrationTests
    {
        private const string ApiHost = $"/{Endpoints.PaymentsHistory}";

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
        public async Task GetPaymentOperations_WhenTryToGetAllOperations_ThenIsSuccessStatusCode()
        {
            const string accountId = "92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84";
            var getOperationsRequest = new RestRequest($"{ApiHost}/{accountId}");

            var response = await _restClient.ExecuteAsync<Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>>(getOperationsRequest);

            Assert.Multiple(() =>
            {
                response.IsSuccessful.Should().BeTrue();

                var result = response.Data;

                result.IsSucceeded.Should().BeTrue();
                result.Payload.Should().NotBeNull();
            });
        }

        [Test]
        public async Task GetPaymentOperations_WithSeveralPaymentOperations_ThenBalanceHistoryHasBeenCalculatedCorrectly()
        {
            const int createRequestAmount = 3;
            const decimal initialBalance = 11.2m;
            var paymentAccountOperationResult = await SavePaymentAccountAsync(initialBalance);

            var paymentAccountId = paymentAccountOperationResult.Payload;

            var categoryId = await SaveCategoryAsync(CategoryTypes.Income, "add-test-6");
            var operationIds = new Collection<Guid>();

            foreach (var i in Enumerable.Range(1, createRequestAmount))
            {
                var requestBody = new CreateOperationRequest
                {
                    Amount = 10 + i,
                    Comment = $"New operation - {i}",
                    CategoryId = categoryId,
                    ContractorId = string.Empty,
                    OperationDate = new DateOnly(2023, 12, 15).AddDays(i)
                };

                var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
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

            var historyRecords = await WaitForHistoryRecordsAsync(
                paymentAccountId,
                records =>
                    operationIds.All(operationId => records.Any(record => record.Record.Key == operationId)) &&
                    records.Count == createRequestAmount &&
                    records
                        .OrderBy(r => r.Record.OperationDay)
                        .Select(r => r.Balance)
                        .SequenceEqual([22.2m, 34.2m, 47.2m]),
                "all created operations are visible and balances are 22.2, 34.2, 47.2",
                operationIds);

            var historyRecordBalance = historyRecords.OrderBy(r => r.Record.OperationDay).Select(r => r.Balance);

            Assert.Multiple(() =>
            {
                Assert.That(() => historyRecords.Count, Is.EqualTo(createRequestAmount));
                historyRecordBalance.Should().BeEquivalentTo([22.2m, 34.2m, 47.2m]);
            });
        }

        [Test]
        public async Task GetPaymentOperations_WithExpenseOperations_ReturnsNegativeBalance()
        {
            const decimal expectedBalanceAmount = 2M;
            const decimal initialBalance = 10m;

            var paymentAccountId = (await SavePaymentAccountAsync(initialBalance)).Payload;

            var expenseCategoryId = await SaveCategoryAsync(CategoryTypes.Expense, "add-test-5");

            var requestBody = new CreateOperationRequest
            {
                Amount = 8,
                Comment = "New operation - expense",
                CategoryId = expenseCategoryId,
                ContractorId = string.Empty,
                OperationDate = new DateOnly(2023, 12, 15)
            };

            var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                .AddJsonBody(requestBody);

            await _restClient.ExecuteWithDelayAsync(postCreateRequest, executionDelayInMs: 2000);

            var historyRecords = await WaitForHistoryRecordsAsync(
                paymentAccountId,
                records => records.Count == 1 && records.Single().Balance == expectedBalanceAmount,
                $"single expense operation is visible with balance {expectedBalanceAmount}",
                cancellationToken: TestContext.CurrentContext.CancellationToken);

            var accountAfter = await PaymentProjectionWaiter.WaitForPaymentAccountAsync(
                _restClient,
                paymentAccountId,
                account => account.Balance == expectedBalanceAmount,
                $"balance is {expectedBalanceAmount}",
                cancellationToken: TestContext.CurrentContext.CancellationToken);

            Assert.Multiple(() =>
            {
                historyRecords.Single().Balance.Should().Be(expectedBalanceAmount);
                accountAfter.Balance.Should().Be(expectedBalanceAmount);
            });
        }

        [Test]
        public async Task GetPaymentOperations_WithSeveralOperations_ShouldReturnExpectedBalanceOperationsOrdering()
        {
            const int createRequestAmount = 3;
            var paymentAccountId = (await SavePaymentAccountAsync(11.2m)).Payload;
            var operationIds = new Collection<Guid>();

            foreach (var i in Enumerable.Range(1, createRequestAmount))
            {
                var requestBody = new CreateOperationRequest
                {
                    Amount = 7 + i,
                    Comment = $"New operation - {i}",
                    CategoryId = await SaveCategoryAsync(CategoryTypes.Income, $"add-test-loop-{i}"),
                    ContractorId = string.Empty,
                    OperationDate = new DateOnly(2023, 12, 15).AddDays(i)
                };

                var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
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

            var historyRecords = await WaitForHistoryRecordsAsync(
                paymentAccountId,
                records =>
                    operationIds.All(operationId => records.Any(record => record.Record.Key == operationId)) &&
                    records.Count == createRequestAmount &&
                    records
                        .Select(r => r.Balance)
                        .OrderBy(balance => balance)
                        .SequenceEqual([19.2m, 28.2m, 38.2m]),
                "all created operation IDs are visible and balances are 19.2, 28.2, 38.2",
                operationIds);

            historyRecords.Select(r => r.Balance).Should().BeEquivalentTo([19.2m, 28.2m, 38.2m]);
        }

        [Test]
        public async Task GetPaymentOperations_WhenUpdateExistedOperation_ReturnsUpToDateOperationState()
        {
            const int createRequestAmount = 3;
            var paymentAccountId = (await SavePaymentAccountAsync(11.2m)).Payload;

            var operationsGuids = new Collection<Guid>();

            foreach (var i in Enumerable.Range(0, createRequestAmount))
            {
                var requestBody = new CreateOperationRequest
                {
                    Amount = 7,
                    Comment = $"{i} - New operation",
                    CategoryId = await SaveCategoryAsync(CategoryTypes.Income, $"add-test-3s-{i}"),
                    ContractorId = string.Empty,
                    OperationDate = new DateOnly(2023, 12, 15).AddDays(i * (i % 2 == 0 ? -3 : 3))
                };

                var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                    .AddJsonBody(requestBody);

                var saveResponse = await _restClient.ExecuteWithDelayAsync<Result<CreateOperationResponse>>(postCreateRequest);
                var operationGuid = Guid.Parse(saveResponse.Data.Payload.PaymentOperationId);

                operationsGuids.Add(operationGuid);
            }

            var updateRequest = new UpdateOperationRequest
            {
                Amount = 9120,
                OperationDate = new DateOnly(2027, 1, 17),
                CategoryId = await SaveCategoryAsync(CategoryTypes.Expense, "add-test-3s-0"),
                ContractorId = string.Empty,
                Comment = "0 - Update operation"
            };

            var operationForUpdateKey = operationsGuids.First();
            await WaitForHistoryRecordAsync(paymentAccountId, operationForUpdateKey);

            var updateCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}/{operationForUpdateKey}", Method.Patch)
                .AddJsonBody(updateRequest);

            var updateResponse = await _restClient.ExecuteWithDelayAsync<Result<UpdateOperationResponse>>(updateCreateRequest);
            updateResponse.Data.StatusMessage.Should().BeNullOrEmpty();
            updateResponse.Data.IsSucceeded.Should().BeTrue();

            var historyRecords = (await WaitForHistoryRecordsAsync(
                    paymentAccountId,
                    records =>
                    {
                        if (records.Count != createRequestAmount)
                        {
                            return false;
                        }

                        var updatedRecord = records.SingleOrDefault(r => r.Record.Key == operationForUpdateKey);

                        return updatedRecord is not null &&
                               updatedRecord.Record.Amount == updateRequest.Amount &&
                               updatedRecord.Record.OperationDay == updateRequest.OperationDate &&
                               updatedRecord.Record.Comment == updateRequest.Comment;
                    }))
                .OrderBy(r => r.Record.OperationDay)
                .ThenBy(r => r.Record.Key)
                .Select(r => r)
                .ToList();

            var updatedHistoryRecord = historyRecords.Single(r => r.Record.Key == operationForUpdateKey);

            Assert.Multiple(() =>
            {
                historyRecords.Select(r => r.Record.Key).Should().OnlyHaveUniqueItems();
                historyRecords.Select(r => r.Record.Amount).Should().BeEquivalentTo([7, 7, 9120]);
                historyRecords.Select(r => r.Balance).Should().BeEquivalentTo([18.2m, 25.2m, -9094.8m]);
                updatedHistoryRecord.Record.Amount.Should().Be(updateRequest.Amount);
                updatedHistoryRecord.Record.OperationDay.Should().Be(updateRequest.OperationDate);
                updatedHistoryRecord.Record.Comment.Should().Be(updateRequest.Comment);
                updatedHistoryRecord.Balance.Should().Be(-9094.8m);
            });
        }

        [Test]
        public async Task GetPaymentOperations_WhenAddAndThenUpdate_ReturnsUpToDateOperationState()
        {
            var paymentAccountId = (await SavePaymentAccountAsync(11.2m)).Payload;

            var requestBody = new CreateOperationRequest
            {
                Amount = 7,
                Comment = "New operation - x",
                CategoryId = await SaveCategoryAsync(CategoryTypes.Income, "add-test-2"),
                ContractorId = string.Empty,
                OperationDate = new DateOnly(2023, 12, 15)
            };

            var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                .AddJsonBody(requestBody);

            var addResponse = await _restClient.ExecuteWithDelayAsync<Result<CreateOperationResponse>>(
                postCreateRequest,
                executionDelayAfterInMs: 5000);

            var newOperationId = Guid.Parse(addResponse.Data.Payload.PaymentOperationId);
            await WaitForHistoryRecordAsync(paymentAccountId, newOperationId);

            var updateRequest = new UpdateOperationRequest
            {
                Amount = 11,
                OperationDate = new DateOnly(2027, 1, 17),
                CategoryId = await SaveCategoryAsync(CategoryTypes.Expense, "add-test-2"),
                ContractorId = string.Empty,
                Comment = "updated state"
            };

            var updateOperationRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}/{newOperationId}", Method.Patch)
                .AddJsonBody(updateRequest);

            var updateResponse = await _restClient.ExecuteWithDelayAsync<Result<UpdateOperationResponse>>(
                updateOperationRequest);

            updateResponse.Data.StatusMessage.Should().BeNullOrEmpty();
            updateResponse.Data.IsSucceeded.Should().BeTrue();

            var targetPaymentHistory = (await WaitForHistoryRecordsAsync(
                paymentAccountId,
                records =>
                {
                    var updatedRecord = records.SingleOrDefault(r => r.Record.Key == newOperationId);

                    return updatedRecord is not null &&
                           updatedRecord.Record.Amount == updateRequest.Amount &&
                           updatedRecord.Record.OperationDay == updateRequest.OperationDate &&
                           updatedRecord.Record.Comment == updateRequest.Comment &&
                           updatedRecord.Balance == 0.2m;
                },
                knownOperationIds: [newOperationId]))
                .Single(r => r.Record.Key == newOperationId);

            targetPaymentHistory.Balance.Should().Be(0.2m);
        }

        [Test]
        public async Task GetOperationById_WhenValidFilterById_ReturnsOperationWithExpectedAmount()
        {
            var paymentAccountId = (await SavePaymentAccountAsync(11.2m)).Payload;

            var requestBody = new CreateOperationRequest
            {
                CategoryId = await SaveCategoryAsync(CategoryTypes.Income, "add-test-1"),
                ContractorId = string.Empty,
                Comment = "Some test",
                OperationDate = new DateOnly(2024, 1, 6),
                Amount = 35.64m
            };

            var createPaymentRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                .AddJsonBody(requestBody);

            var createPaymentResponse = await _restClient.ExecuteWithDelayAsync<Result<CreateOperationResponse>>(createPaymentRequest);
            var createPaymentRequestResult = createPaymentResponse.Data;

            var newPaymentId = createPaymentRequestResult.Payload.PaymentOperationId;

            var paymentHistory = await WaitForHistoryRecordAsync(
                paymentAccountId,
                Guid.Parse(newPaymentId),
                record => record.Record.Amount == 35.64m,
                "operation is visible by id with amount 35.64",
                TestContext.CurrentContext.CancellationToken);

            Assert.Multiple(() =>
            {
                createPaymentRequestResult.IsSucceeded.Should().BeTrue();
                paymentHistory.Record.Amount.Should().Be(35.64m);
            });
        }

        [Test]
        public async Task GetOperationById_WhenInValidFilterById_ReturnsFalseResult()
        {
            const string operationId = "invalid-operation-ref";
            const string accountId = "invalid-acc-ref";

            var getOperationByIdRequest = new RestRequest($"{ApiHost}/{accountId}/byId/{operationId}");

            var response = await _restClient.ExecuteAsync<Result<PaymentOperationHistoryRecord>>(getOperationByIdRequest);

            var result = response.Data;

            result.IsSucceeded.Should().BeFalse();
        }

        private async Task<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>> GetHistoryRecordsAsync(Guid paymentAccountId)
        {
            var getPaymentHistoryRecordsRequest = new RestRequest($"{Endpoints.PaymentsHistory}/{paymentAccountId}");

            var paymentsHistoryResponse = await _restClient.ExecuteWithDelayAsync<Result<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>>>(
                getPaymentHistoryRecordsRequest,
                executionDelayBeforeInMs: 4000);

            return paymentsHistoryResponse.Data.Payload;
        }

        private async Task<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>> WaitForHistoryRecordsAsync(
            Guid paymentAccountId,
            Func<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>, bool> condition,
            string conditionDescription = null,
            IEnumerable<Guid> knownOperationIds = null,
            CancellationToken cancellationToken = default)
        {
            return await PaymentProjectionWaiter.WaitForHistoryRecordsAsync(
                _restClient,
                paymentAccountId,
                condition,
                conditionDescription ?? "custom payment history condition",
                knownOperationIds,
                cancellationToken);
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

        private static string DescribeResponse<T>(RestResponse<Result<T>> response)
        {
            if (response == null)
            {
                return "Response was null.";
            }

            return $"HTTP {(int)response.StatusCode} {response.StatusCode}, transport-success={response.IsSuccessful}, rest-error='{response.ErrorMessage}', domain-success={response.Data?.IsSucceeded}, status='{response.Data?.StatusMessage}', content='{response.Content}'";
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

        private async Task<string> SaveCategoryAsync(CategoryTypes categoryType, string categoryNode)
        {
            var requestSaveBody = new CreateCategoryRequest
            {
                CategoryType = categoryType.Key,
                NameNodes =
                [
                    nameof(categoryType),
                    categoryNode
                ]
            };

            var saveCategoryRequest = new RestRequest($"{Endpoints.Categories}", Method.Post)
                .AddJsonBody(requestSaveBody);

            var paymentsHistoryResponse = await _restClient
                .ExecuteAsync<Result<string>>(saveCategoryRequest);

            return paymentsHistoryResponse.Data.Payload;
        }

        private async Task<Result<Guid>> SavePaymentAccountAsync(decimal initialBalance)
        {
            var requestSaveBody = new CreatePaymentAccountRequest
            {
                InitialBalance = initialBalance,
                Description = "test-account",
                AccountType = AccountTypes.Deposit.Key,
                Agent = "Personal",
                Currency = "usd"
            };

            var saveAccountRequest = new RestRequest($"{Endpoints.PaymentAccounts}", Method.Post)
                .AddJsonBody(requestSaveBody);

            var paymentsHistoryResponse = await _restClient.ExecuteWithDelayAsync<Result<Guid>>(
                saveAccountRequest,
                executionDelayAfterInMs: 1000);

            return paymentsHistoryResponse.Data;
        }

        private async Task<PaymentAccount> GetPaymentsAccountAsync(Guid paymentAccountId)
        {
            var getPaymentsAccountRequest = new RestRequest($"{Endpoints.PaymentAccounts}/byId/{paymentAccountId}");

            var getResponse = await _restClient
                .ExecuteWithDelayAsync<Result<PaymentAccount>>(getPaymentsAccountRequest, executionDelayAfterInMs: 3500);

            return getResponse.Data.Payload;
        }
    }
}

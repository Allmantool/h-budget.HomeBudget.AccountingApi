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
using HomeBudget.Accounting.Api.Models.History;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Accounting.Api.Models.PaymentAccount;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [NonParallelizable]
    [Order(IntegrationTestOrderIndex.CrossAccountTransferControllerTests)]
    public class CrossAccountTransferControllerTests : BaseIntegrationTests
    {
        private const string CrossAccountsTransferApiHost = $"/{Endpoints.CrossAccountsTransfer}";
        private const string PaymentHistoryApiHost = $"/{Endpoints.PaymentsHistory}";

        private readonly CrossAccountsTransferWebApp _sut = new();
        private RestClient _restClient;

        [OneTimeSetUp]
        public override async Task SetupAsync()
        {
            await OperationsTestWebApp.ResetAsync();
            await _sut.InitAsync();
            await base.SetupAsync();

            _restClient = _sut.RestHttpClient;
        }

        [Test]
        public async Task ApplyTransfer_WithStandardFlow_ThenExpectedOperationWillBeAccomplishedAsync()
        {
            var senderAccountId = (await SavePaymentAccountAsync(0, AccountTypes.Deposit, CurrencyTypes.Byn)).Payload;
            var recipientAccountId = (await SavePaymentAccountAsync(0, AccountTypes.Cash, CurrencyTypes.Usd)).Payload;

            var requestBody = new CrossAccountsTransferRequest
            {
                Amount = 100,
                Recipient = recipientAccountId,
                Sender = senderAccountId,
                OperationAt = new DateOnly(2024, 05, 07),
                Multiplier = 0.12m
            };

            var createRequest = new RestRequest($"{CrossAccountsTransferApiHost}", Method.Post)
                .AddJsonBody(requestBody);

            var transferOperationResponse = await _restClient.ExecuteWithDelayAsync<Result<CrossAccountsTransferResponse>>(
                createRequest,
                executionDelayAfterInMs: 20000);

            transferOperationResponse.IsSuccessful.Should().BeTrue(DescribeResponse(transferOperationResponse));
            transferOperationResponse.Data.IsSucceeded.Should().BeTrue(DescribeResponse(transferOperationResponse));

            var transferOperationId = transferOperationResponse.Data.Payload.PaymentOperationId;
            var knownOperationIds = new[] { transferOperationId };

            var senderHistoryResponsePayload = await WaitForHistoryAsync(
                senderAccountId,
                records => records.Count == 1,
                knownOperationIds);
            var recipientHistoryResponsePayload = await WaitForHistoryAsync(
                recipientAccountId,
                records => records.Count == 1,
                knownOperationIds);

            Assert.Multiple(() =>
            {
                var senderResponse = senderHistoryResponsePayload.Single();
                var recipientResponse = recipientHistoryResponsePayload.Single();

                senderResponse.Balance.Should().Be(-100);
                recipientResponse.Balance.Should().Be(12);

                var senderOperationId = senderResponse.Record.Key;
                var recipientOperationId = recipientResponse.Record.Key;

                recipientOperationId.Should().Be(senderOperationId);
            });
        }

        [Test]
        public async Task RemoveTransfer_ThenRelatedOperationsAlsoWillBeDeletedAsync()
        {
            var senderAccountId = (await SavePaymentAccountAsync(0, AccountTypes.Deposit, CurrencyTypes.Byn)).Payload;
            var recipientAccountId = (await SavePaymentAccountAsync(0, AccountTypes.Cash, CurrencyTypes.Usd)).Payload;

            var createTransferRequestBody = new CrossAccountsTransferRequest
            {
                Amount = 100,
                Recipient = recipientAccountId,
                Sender = senderAccountId,
                OperationAt = new DateOnly(2024, 05, 07),
                Multiplier = 0.12m
            };

            var createRequest = new RestRequest($"{CrossAccountsTransferApiHost}", Method.Post)
                .AddJsonBody(createTransferRequestBody);

            var transferOperationResponse = await _restClient.ExecuteWithDelayAsync<Result<CrossAccountsTransferResponse>>(createRequest, executionDelayAfterInMs: 3000);

            transferOperationResponse.IsSuccessful.Should().BeTrue(DescribeResponse(transferOperationResponse));
            transferOperationResponse.Data.IsSucceeded.Should().BeTrue(DescribeResponse(transferOperationResponse));

            var transferOperationId = transferOperationResponse.Data.Payload.PaymentOperationId;
            var knownOperationIds = new[] { transferOperationId };

            await WaitForHistoryAsync(senderAccountId, records => records.Count == 1, knownOperationIds);
            await WaitForHistoryAsync(recipientAccountId, records => records.Count == 1, knownOperationIds);

            var removeTransferRequestBody = new RemoveTransferRequest
            {
                PaymentAccountId = senderAccountId,
                TransferOperationId = transferOperationId
            };

            var removeRequest = new RestRequest($"{CrossAccountsTransferApiHost}", Method.Delete)
                 .AddJsonBody(removeTransferRequestBody);

            await _restClient.ExecuteWithDelayAsync<Result<CrossAccountsTransferResponse>>(removeRequest, executionDelayBeforeInMs: 3000, executionDelayAfterInMs: 2500);

            var senderHistoryResponsePayload = await WaitForHistoryAsync(
                senderAccountId,
                records => records.Count == 0,
                knownOperationIds);
            var recipientHistoryResponsePayload = await WaitForHistoryAsync(
                recipientAccountId,
                records => records.Count == 0,
                knownOperationIds);

            Assert.Multiple(() =>
            {
                senderHistoryResponsePayload.Should().BeEmpty();
                recipientHistoryResponsePayload.Should().BeEmpty();
            });
        }

        [Test]
        [Ignore("Not implemented so far")]
        public async Task UpdateTransfer_WithoutSenderOrRecipientAccountChange_ThenValuesWillBeUpdatedAsync()
        {
            var senderAccountId = (await SavePaymentAccountAsync(0, AccountTypes.Deposit, CurrencyTypes.Byn)).Payload;
            var recipientAccountId = (await SavePaymentAccountAsync(0, AccountTypes.Cash, CurrencyTypes.Usd)).Payload;

            var requestBody = new CrossAccountsTransferRequest
            {
                Amount = 100,
                Recipient = recipientAccountId,
                Sender = senderAccountId,
                OperationAt = new DateOnly(2024, 05, 07),
                Multiplier = 0.12m
            };

            var createTransferRequest = new RestRequest($"{CrossAccountsTransferApiHost}", Method.Post)
                .AddJsonBody(requestBody);

            var transferResponse = await _restClient.ExecuteAsync<Result<CrossAccountsTransferResponse>>(createTransferRequest);

            var updateTransferRequestBody = new UpdateTransferRequest
            {
                TransferOperationId = transferResponse.Data.Payload.PaymentOperationId,
                Amount = 1350,
                Multiplier = 10m,
                OperationAt = new DateOnly(2024, 05, 11),
                Recipient = recipientAccountId,
                Sender = senderAccountId
            };

            var updateRequest = new RestRequest($"{CrossAccountsTransferApiHost}", Method.Patch)
                .AddJsonBody(updateTransferRequestBody);

            await _restClient.ExecuteAsync<Result<CrossAccountsTransferResponse>>(updateRequest);

            var senderHistoryResponsePayload = await GetHistoryByPaymentAccountIdAsync(senderAccountId);

            var recipientHistoryResponsePayload = await GetHistoryByPaymentAccountIdAsync(recipientAccountId);

            Assert.Multiple(() =>
            {
                senderHistoryResponsePayload.First().Balance.Should().Be(-100m);
                recipientHistoryResponsePayload.First().Balance.Should().Be(1350m);
            });
        }

        private async Task<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>> GetHistoryByPaymentAccountIdAsync(Guid accountId)
        {
            var getRecipientOperationsRequest = new RestRequest($"{PaymentHistoryApiHost}/{accountId}");

            var recipientHistoryResponse = await _restClient
                .ExecuteWithDelayAsync<Result<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>>>(getRecipientOperationsRequest, executionDelayBeforeInMs: 2000);

            return recipientHistoryResponse.Data.Payload;
        }

        private async Task<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>> WaitForHistoryAsync(
            Guid accountId,
            Func<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>, bool> condition,
            IEnumerable<Guid> knownOperationIds = null,
            CancellationToken cancellationToken = default)
        {
            return await PaymentProjectionWaiter.WaitForHistoryRecordsAsync(
                _restClient,
                accountId,
                condition,
                "transfer payment history reaches expected state",
                knownOperationIds,
                cancellationToken: cancellationToken);
        }

        private static string DescribeResponse<T>(RestResponse<Result<T>> response)
        {
            if (response == null)
            {
                return "Response was null.";
            }

            return $"HTTP {(int)response.StatusCode} {response.StatusCode}, transport-success={response.IsSuccessful}, rest-error='{response.ErrorMessage}', domain-success={response.Data?.IsSucceeded}, status='{response.Data?.StatusMessage}', content='{response.Content}'";
        }

        private async Task<Result<Guid>> SavePaymentAccountAsync(decimal initialBalance, AccountTypes accountType, CurrencyTypes currencyType)
        {
            var requestSaveBody = new CreatePaymentAccountRequest
            {
                InitialBalance = initialBalance,
                Description = "test-description",
                AccountType = accountType.Key,
                Agent = "test-agent",
                Currency = currencyType.ToString()
            };

            var saveCategoryRequest = new RestRequest($"{Endpoints.PaymentAccounts}", Method.Post)
                .AddJsonBody(requestSaveBody);

            var paymentsHistoryResponse = await _restClient
                .ExecuteWithDelayAsync<Result<Guid>>(saveCategoryRequest);

            return paymentsHistoryResponse.Data;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
        private RestClient _restClientAllowingHttpErrors;

        [OneTimeSetUp]
        public override async Task SetupAsync()
        {
            await OperationsTestWebApp.ResetAsync();
            await _sut.InitAsync();
            await base.SetupAsync();

            _restClient = _sut.RestHttpClient;
            _restClientAllowingHttpErrors = _sut.RestHttpClientAllowingHttpErrors;
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
                senderResponse.Record.RelatedPaymentAccountId.Should().Be(recipientAccountId);
                recipientResponse.Record.RelatedPaymentAccountId.Should().Be(senderAccountId);
                senderResponse.Record.ConversionMultiplier.Should().Be(requestBody.Multiplier);
                recipientResponse.Record.ConversionMultiplier.Should().Be(requestBody.Multiplier);
            });
        }

        [Test]
        public async Task ApplyTransfer_WhenSenderProjectionUsesNegativeSignedAmount_ShouldAcceptTheTransfer()
        {
            var senderAccountId = (await SavePaymentAccountAsync(0m, AccountTypes.Deposit, CurrencyTypes.Usd)).Payload;
            var recipientAccountId = (await SavePaymentAccountAsync(0m, AccountTypes.Cash, CurrencyTypes.Usd)).Payload;
            var request = new CrossAccountsTransferRequest
            {
                Amount = 23m,
                Sender = senderAccountId,
                Recipient = recipientAccountId,
                Multiplier = 1m,
                OperationAt = new DateOnly(2025, 3, 12)
            };

            var response = await _restClient.ExecuteAsync<Result<CrossAccountsTransferResponse>>(
                new RestRequest(CrossAccountsTransferApiHost, Method.Post).AddJsonBody(request));

            response.IsSuccessful.Should().BeTrue(DescribeResponse(response));
            response.Data.IsSucceeded.Should().BeTrue(DescribeResponse(response));
            var operationId = response.Data.Payload.PaymentOperationId;
            var senderHistory = await WaitForHistoryAsync(
                senderAccountId,
                records => records.Count == 1 && records.Single().Record.Key == operationId,
                [operationId]);
            var recipientHistory = await WaitForHistoryAsync(
                recipientAccountId,
                records => records.Count == 1 && records.Single().Record.Key == operationId,
                [operationId]);

            senderHistory.Single().Record.Amount.Should().Be(
                -23m,
                "the transfer write contract creates a signed sender operation after accepting its positive transfer magnitude");
            recipientHistory.Single().Record.Amount.Should().Be(
                23m,
                "the transfer write contract creates the corresponding positive recipient operation");
        }

        [Test]
        public async Task ApplyTransfer_WithCustomConversionMultiplier_ThenPersistsTheCustomRateAsync()
        {
            var senderAccountId = (await SavePaymentAccountAsync(0, AccountTypes.Deposit, CurrencyTypes.Usd)).Payload;
            var recipientAccountId = (await SavePaymentAccountAsync(0, AccountTypes.Cash, CurrencyTypes.Byn)).Payload;
            var requestBody = new CrossAccountsTransferRequest
            {
                Amount = 15m,
                Recipient = recipientAccountId,
                Sender = senderAccountId,
                OperationAt = new DateOnly(2024, 05, 07),
                Multiplier = 2.977m,
                CustomConversionMultiplier = 3.1m
            };

            var transferOperationResponse = await _restClient.ExecuteWithDelayAsync<Result<CrossAccountsTransferResponse>>(
                new RestRequest(CrossAccountsTransferApiHost, Method.Post).AddJsonBody(requestBody),
                executionDelayAfterInMs: 20000);

            transferOperationResponse.IsSuccessful.Should().BeTrue(DescribeResponse(transferOperationResponse));
            transferOperationResponse.Data.IsSucceeded.Should().BeTrue(DescribeResponse(transferOperationResponse));

            var transferOperationId = transferOperationResponse.Data.Payload.PaymentOperationId;
            var knownOperationIds = new[] { transferOperationId };
            var recipientHistory = await WaitForHistoryAsync(
                recipientAccountId,
                records => records.Count == 1 && records.Single().Balance == 46.5m,
                knownOperationIds);
            var senderHistory = await WaitForHistoryAsync(senderAccountId, records => records.Count == 1, knownOperationIds);

            Assert.Multiple(() =>
            {
                senderHistory.Single().Record.ConversionMultiplier.Should().Be(3.1m);
                recipientHistory.Single().Record.ConversionMultiplier.Should().Be(3.1m);
                recipientHistory.Single().Record.Amount.Should().Be(46.5m);
            });
        }

        [Test]
        public async Task ApplyTransfer_WithOpeningBalances_ThenHistoryShowsEachAccountsResultingBalanceAsync()
        {
            var senderAccountId = (await SavePaymentAccountAsync(34m, AccountTypes.Deposit, CurrencyTypes.Usd)).Payload;
            var recipientAccountId = (await SavePaymentAccountAsync(8m, AccountTypes.Cash, CurrencyTypes.Usd)).Payload;
            var requestBody = new CrossAccountsTransferRequest
            {
                Amount = 1m,
                Recipient = recipientAccountId,
                Sender = senderAccountId,
                OperationAt = new DateOnly(2024, 05, 07),
                Multiplier = 1m
            };

            var response = await _restClient.ExecuteAsync<Result<CrossAccountsTransferResponse>>(
                new RestRequest(CrossAccountsTransferApiHost, Method.Post).AddJsonBody(requestBody));

            response.IsSuccessful.Should().BeTrue(DescribeResponse(response));
            response.Data.IsSucceeded.Should().BeTrue(DescribeResponse(response));

            var knownOperationIds = new[] { response.Data.Payload.PaymentOperationId };
            var senderHistory = await WaitForHistoryAsync(
                senderAccountId,
                records => records.Count == 1 && records.Single().Balance == 33m,
                knownOperationIds);
            var recipientHistory = await WaitForHistoryAsync(
                recipientAccountId,
                records => records.Count == 1 && records.Single().Balance == 9m,
                knownOperationIds);

            Assert.Multiple(() =>
            {
                senderHistory.Single().Record.Amount.Should().Be(-1m);
                senderHistory.Single().Balance.Should().Be(33m);
                recipientHistory.Single().Record.Amount.Should().Be(1m);
                recipientHistory.Single().Balance.Should().Be(9m);
            });
        }

        [Test]
        public async Task ApplyIdempotentExactTransfer_WhenRetried_ThenOneAtomicTransferIsProjectedAndConflictIsDeterministic()
        {
            var senderAccountId = (await SavePaymentAccountAsync(0m, AccountTypes.Deposit, CurrencyTypes.Usd)).Payload;
            var recipientAccountId = (await SavePaymentAccountAsync(0m, AccountTypes.Cash, CurrencyTypes.Byn)).Payload;
            var idempotencyKey = $"transfer-{Guid.NewGuid():N}";
            var request = new CrossAccountsTransferRequest
            {
                Sender = senderAccountId,
                Recipient = recipientAccountId,
                SenderAmount = 1800m,
                RecipientAmount = 6030m,
                SenderCurrency = "USD",
                RecipientCurrency = "BYN",
                SourceReference = "FamilyPro12:fixture:REESTR:23460-23461",
                OperationAt = new DateOnly(2026, 9, 17)
            };

            var first = await _restClient.ExecuteAsync<Result<CrossAccountsTransferResponse>>(
                new RestRequest(CrossAccountsTransferApiHost, Method.Post)
                    .AddHeader("Idempotency-Key", idempotencyKey)
                    .AddJsonBody(request));
            var replay = await _restClient.ExecuteAsync<Result<CrossAccountsTransferResponse>>(
                new RestRequest(CrossAccountsTransferApiHost, Method.Post)
                    .AddHeader("Idempotency-Key", idempotencyKey)
                    .AddJsonBody(request));
            var conflict = await _restClientAllowingHttpErrors.ExecuteAllowingHttpErrorAsync<Result<CrossAccountsTransferResponse>>(
                new RestRequest(CrossAccountsTransferApiHost, Method.Post)
                    .AddHeader("Idempotency-Key", idempotencyKey)
                    .AddJsonBody(request with { RecipientAmount = 6031m }),
                [HttpStatusCode.Conflict]);

            first.IsSuccessful.Should().BeTrue(DescribeResponse(first));
            replay.IsSuccessful.Should().BeTrue(DescribeResponse(replay));
            first.Data.Payload.PaymentOperationId.Should().Be(replay.Data.Payload.PaymentOperationId);
            first.Data.Payload.CommandId.Should().Be(replay.Data.Payload.CommandId);
            replay.Data.Payload.IsDuplicate.Should().BeTrue();
            conflict.StatusCode.Should().Be(HttpStatusCode.Conflict, DescribeResponse(conflict));

            var transferId = first.Data.Payload.PaymentOperationId;
            var senderHistory = await WaitForHistoryAsync(
                senderAccountId,
                records => records.Count(record => record.Record.Key == transferId) == 1,
                [transferId]);
            var recipientHistory = await WaitForHistoryAsync(
                recipientAccountId,
                records => records.Count(record => record.Record.Key == transferId) == 1,
                [transferId]);
            var status = await WaitForTransferStatusAsync(transferId, first.Data.Payload.CommandId, "Projected");

            Assert.Multiple(() =>
            {
                senderHistory.Single(record => record.Record.Key == transferId).Record.Amount.Should().Be(-1800m);
                recipientHistory.Single(record => record.Record.Key == transferId).Record.Amount.Should().Be(6030m);
                status.IsSuccessful.Should().BeTrue(DescribeResponse(status));
                status.Data.Payload.Status.Should().Be("Projected");
                status.Data.Payload.SenderAmount.Should().Be(1800m);
                status.Data.Payload.RecipientAmount.Should().Be(6030m);
                status.Data.Payload.SenderCurrency.Should().Be("USD");
                status.Data.Payload.RecipientCurrency.Should().Be("BYN");
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

        private async Task<RestResponse<Result<TransferCommandStatusResponse>>> WaitForTransferStatusAsync(
            Guid transferId,
            string commandId,
            string expectedStatus)
        {
            RestResponse<Result<TransferCommandStatusResponse>> response = null;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                response = await _restClient.ExecuteAsync<Result<TransferCommandStatusResponse>>(
                    new RestRequest($"{CrossAccountsTransferApiHost}/{transferId}/commands/{commandId}"));
                if (response.Data?.Payload?.Status == expectedStatus)
                {
                    return response;
                }

                await Task.Delay(500);
            }

            return response;
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

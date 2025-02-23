using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FluentAssertions;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.Extensions;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Accounting.Api.Models.PaymentAccount;
using HomeBudget.Accounting.Api.Models.History;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    [TestFixture]
    [Category("Integration")]
    public class CrossAccountTransferControllerTests
    {
        private const string CrossAccountsTransferApiHost = $"/{Endpoints.CrossAccountsTransfer}";
        private const string PaymentHistoryApiHost = $"/{Endpoints.PaymentsHistory}";

        private CrossAccountsTransferWebApp _sut;

        [OneTimeSetUp]
        public async Task SetupAsync()
        {
            _sut = new CrossAccountsTransferWebApp();
            await _sut.StartAsync();
        }

        [Test]
        public async Task ApplyTransfer_WithStandardFlow_ThenExpectedOperationWillBeAccomplished()
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

            await _sut.RestHttpClient.ExecuteWithDelayAsync<Result<CrossAccountsTransferResponse>>(createRequest, executionDelayAfterInMs: 8000);

            var senderHistoryResponsePayload = await GetHistoryByPaymentAccountIdAsync(senderAccountId);
            var recipientHistoryResponsePayload = await GetHistoryByPaymentAccountIdAsync(recipientAccountId);

            Assert.Multiple(() =>
            {
                senderHistoryResponsePayload.Single().Balance.Should().Be(-100);
                recipientHistoryResponsePayload.Single().Balance.Should().Be(12);

                var senderOperationId = senderHistoryResponsePayload.Single().Record.Key;
                var recipientOperationId = recipientHistoryResponsePayload.Single().Record.Key;

                recipientOperationId.Should().Be(senderOperationId);
            });
        }

        [Test]
        public async Task RemoveTransfer_ThenRelatedOperationsAlsoWillBeDeleted()
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

            var transferOperationResponse = await _sut.RestHttpClient.ExecuteWithDelayAsync<Result<CrossAccountsTransferResponse>>(createRequest, executionDelayAfterInMs: 2000);

            var removeTransferRequestBody = new RemoveTransferRequest
            {
                PaymentAccountId = senderAccountId,
                TransferOperationId = transferOperationResponse.Data.Payload.PaymentOperationId
            };

            var removeRequest = new RestRequest($"{CrossAccountsTransferApiHost}", Method.Delete)
                 .AddJsonBody(removeTransferRequestBody);

            await _sut.RestHttpClient.ExecuteWithDelayAsync<Result<CrossAccountsTransferResponse>>(removeRequest, executionDelayAfterInMs: 2500);

            var senderHistoryResponsePayload = await GetHistoryByPaymentAccountIdAsync(senderAccountId);

            var recipientHistoryResponsePayload = await GetHistoryByPaymentAccountIdAsync(recipientAccountId);

            Assert.Multiple(() =>
            {
                senderHistoryResponsePayload.Should().BeEmpty();
                recipientHistoryResponsePayload.Should().BeEmpty();
            });
        }

        [Test]
        [Ignore("Not implemented so far")]
        public async Task UpdateTransfer_WithoutSenderOrRecipientAccountChange_ThenValuesWillBeUpdated()
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

            var transferResponse = await _sut.RestHttpClient.ExecuteAsync<Result<CrossAccountsTransferResponse>>(createTransferRequest);

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

            await _sut.RestHttpClient.ExecuteAsync<Result<CrossAccountsTransferResponse>>(updateRequest);

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

            var recipientHistoryResponse = await _sut.RestHttpClient
                .ExecuteWithDelayAsync<Result<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>>>(getRecipientOperationsRequest, executionDelayAfterInMs: 2000);

            return recipientHistoryResponse.Data.Payload;
        }

        private async Task<Result<Guid>> SavePaymentAccountAsync(decimal initialBalance, AccountTypes accountType, CurrencyTypes currencyType)
        {
            var requestSaveBody = new CreatePaymentAccountRequest
            {
                InitialBalance = initialBalance,
                Description = "test-description",
                AccountType = accountType.Id,
                Agent = "test-agent",
                Currency = currencyType.ToString()
            };

            var saveCategoryRequest = new RestRequest($"{Endpoints.PaymentAccounts}", Method.Post)
                .AddJsonBody(requestSaveBody);

            var paymentsHistoryResponse = await _sut.RestHttpClient
                .ExecuteWithDelayAsync<Result<Guid>>(saveCategoryRequest, executionDelayAfterInMs: 1000);

            return paymentsHistoryResponse.Data;
        }
    }
}

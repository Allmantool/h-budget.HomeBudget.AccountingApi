using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FluentAssertions;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Accounting.Api.Models.PaymentAccount;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    [TestFixture]
    [Category("Integration")]
    public class CrossAccountTransferControllerTests : IAsyncDisposable
    {
        private const string CrossAccountsTransferApiHost = $"/{Endpoints.CrossAccountsTransfer}";
        private const string PaymentHistoryApiHost = $"/{Endpoints.PaymentsHistory}";

        private readonly CrossAccountsTransferWebApp _sut = new();

        [OneTimeTearDown]
        public async Task StopAsync() => await _sut.StopAsync();

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

            await _sut.RestHttpClient.ExecuteAsync<Result<CrossAccountsTransferResponse>>(createRequest);

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

            var transferOperationResponse = await _sut.RestHttpClient.ExecuteAsync<Result<CrossAccountsTransferResponse>>(createRequest);

            var removeTransferRequestBody = new RemoveTransferRequest
            {
                PaymentAccountId = senderAccountId,
                TransferOperationId = transferOperationResponse.Data.Payload.PaymentOperationId
            };

            var removeRequest = new RestRequest($"{CrossAccountsTransferApiHost}", Method.Delete)
                 .AddJsonBody(removeTransferRequestBody);

            await _sut.RestHttpClient.ExecuteAsync<Result<CrossAccountsTransferResponse>>(removeRequest);

            var senderHistoryResponsePayload = await GetHistoryByPaymentAccountIdAsync(senderAccountId);

            var recipientHistoryResponsePayload = await GetHistoryByPaymentAccountIdAsync(recipientAccountId);

            Assert.Multiple(() =>
            {
                senderHistoryResponsePayload.Should().BeEmpty();
                recipientHistoryResponsePayload.Should().BeEmpty();
            });
        }

        public ValueTask DisposeAsync()
        {
            return _sut?.DisposeAsync() ?? ValueTask.CompletedTask;
        }

        private async Task<IReadOnlyCollection<PaymentOperationHistoryRecord>> GetHistoryByPaymentAccountIdAsync(Guid accountId)
        {
            var getRecipientOperationsRequest = new RestRequest($"{PaymentHistoryApiHost}/{accountId}");

            var recipientHistoryResponse = await _sut.RestHttpClient.ExecuteAsync<Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>>(getRecipientOperationsRequest);

            return recipientHistoryResponse.Data.Payload;
        }

        private async Task<Result<Guid>> SavePaymentAccountAsync(decimal initialBalance, AccountTypes accountType, CurrencyTypes currencyType)
        {
            var requestSaveBody = new CreatePaymentAccountRequest
            {
                InitialBalance = initialBalance,
                Description = "test-description",
                AccountType = accountType,
                Agent = "test-agent",
                Currency = currencyType.ToString()
            };

            var saveCategoryRequest = new RestRequest($"{Endpoints.PaymentAccounts}", Method.Post)
                .AddJsonBody(requestSaveBody);

            var paymentsHistoryResponse = await _sut.RestHttpClient
                .ExecuteAsync<Result<Guid>>(saveCategoryRequest);

            return paymentsHistoryResponse.Data;
        }
    }
}

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
            var senderAccountId = (await SavePaymentAccountAsync(0, AccountTypes.Deposit, CurrencyTypes.BYN)).Payload;
            var recipientAccountId = (await SavePaymentAccountAsync(0, AccountTypes.Cash, CurrencyTypes.USD)).Payload;

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

            var getSenderOperationsRequest = new RestRequest($"{PaymentHistoryApiHost}/{senderAccountId}");

            var senderHistoryResponse = _sut.RestHttpClient.Execute<Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>>(getSenderOperationsRequest);

            var senderHistoryResponsePayload = senderHistoryResponse.Data.Payload;

            var getRecipientOperationsRequest = new RestRequest($"{PaymentHistoryApiHost}/{recipientAccountId}");

            var recipientHistoryResponse = _sut.RestHttpClient.Execute<Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>>(getRecipientOperationsRequest);

            var recipientHistoryResponsePayload = recipientHistoryResponse.Data.Payload;

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
            var senderAccountId = (await SavePaymentAccountAsync(0, AccountTypes.Deposit, CurrencyTypes.BYN)).Payload;
            var recipientAccountId = (await SavePaymentAccountAsync(0, AccountTypes.Cash, CurrencyTypes.USD)).Payload;

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

            await _sut.RestHttpClient.ExecuteAsync<Result<CrossAccountsTransferResponse>>(createRequest);

            var removeTransferRequestBody = new RemoveTransferRequest
            {
                PaymentAccountId = Guid.NewGuid(),
                TransferOperationId = Guid.NewGuid()
            };

            new RestRequest($"{CrossAccountsTransferApiHost}", Method.Delete)
                .AddJsonBody(removeTransferRequestBody);

            var getSenderOperationsRequest = new RestRequest($"{PaymentHistoryApiHost}/{senderAccountId}");

            var senderHistoryResponse = _sut.RestHttpClient.Execute<Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>>(getSenderOperationsRequest);

            var senderHistoryResponsePayload = senderHistoryResponse.Data.Payload;

            var getRecipientOperationsRequest = new RestRequest($"{PaymentHistoryApiHost}/{recipientAccountId}");

            var recipientHistoryResponse = _sut.RestHttpClient.Execute<Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>>(getRecipientOperationsRequest);

            var recipientHistoryResponsePayload = recipientHistoryResponse.Data.Payload;

            Assert.Multiple(() =>
            {
                senderHistoryResponsePayload.Single().Balance.Should().Be(0);
                recipientHistoryResponsePayload.Single().Balance.Should().Be(0);
            });
        }

        public ValueTask DisposeAsync()
        {
            return _sut?.DisposeAsync() ?? ValueTask.CompletedTask;
        }

        private async Task<Result<Guid>> SavePaymentAccountAsync(decimal initialBalance, AccountTypes accountType, CurrencyTypes currencyType)
        {
            var requestSaveBody = new CreatePaymentAccountRequest
            {
                Balance = initialBalance,
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

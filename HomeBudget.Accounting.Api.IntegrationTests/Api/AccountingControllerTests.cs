using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FluentAssertions;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Models.PaymentAccount;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [Order(IntegrationTestOrderIndex.AccountingControllerTests)]
    public class AccountingControllerTests
    {
        private const string ApiHost = $"/{Endpoints.PaymentAccounts}";

        private readonly AccountingTestWebApp _sut = new();

        [Test]
        public async Task GetPaymentAccounts_WhenTryToGetAllPaymentAccounts_ThenIsSuccessStatusCode()
        {
            var paymentAccountIdResult = await SavePaymentAccountAsync();

            var getPaymentAccountsRequest = new RestRequest(ApiHost);

            var response = await _sut.RestHttpClient.ExecuteAsync<Result<IReadOnlyCollection<PaymentAccountResponse>>>(getPaymentAccountsRequest);

            Assert.Multiple(() =>
            {
                response.IsSuccessful.Should().BeTrue();
                response.Data?.Payload.Count(ac => ac.Key.CompareTo(paymentAccountIdResult.Payload) == 0).Should().Be(1);
            });
        }

        [Test]
        public async Task GetPaymentAccountById_WhenValidFilterById_ReturnsAccountWithExpectedBalance()
        {
            var paymentAccountIdResult = await SavePaymentAccountAsync();

            var getPaymentAccountByIdRequest = new RestRequest($"{ApiHost}/byId/{paymentAccountIdResult.Payload}");

            var response = await _sut.RestHttpClient.ExecuteAsync<Result<PaymentAccount>>(getPaymentAccountByIdRequest);

            var result = response.Data;
            var payload = result.Payload;

            payload.InitialBalance.Should().Be(11.2m);
        }

        [Test]
        public void GetPaymentAccountById_WhenInValidFilterById_ReturnsFalseResult()
        {
            const string paymentAccountId = "invalidRef";

            var getPaymentAccountByIdRequest = new RestRequest($"{ApiHost}/byId/{paymentAccountId}");

            var response = _sut.RestHttpClient.Execute<Result<PaymentAccount>>(getPaymentAccountByIdRequest);

            var result = response.Data;

            result.IsSucceeded.Should().BeFalse();
        }

        [Test]
        public async Task MakePaymentAccount_WhenCreateANewOnePaymentAccount_ReturnsNewGeneratedGuid()
        {
            var requestBody = new CreatePaymentAccountRequest
            {
                InitialBalance = 100,
                AccountType = AccountTypes.Cash.Id,
                Agent = "Vtb",
                Currency = "",
                Description = "Some description"
            };

            var postMakePaymentAccountRequest = new RestRequest(ApiHost, Method.Post).AddJsonBody(requestBody);

            var response = await _sut.RestHttpClient.ExecuteAsync<Result<string>>(postMakePaymentAccountRequest);

            var result = response.Data;
            var payload = result.Payload;

            Guid.TryParse(payload, out _).Should().BeTrue();
        }

        [Test]
        public async Task MakePaymentAccount_ThenAppropriateBalanceAmounts_ShouldBeSet()
        {
            var requestBody = new CreatePaymentAccountRequest
            {
                InitialBalance = 100,
                AccountType = 1,
                Agent = "PriorBank",
                Currency = "BYN",
                Description = "Description-test"
            };

            var postMakePaymentAccountRequest = new RestRequest(ApiHost, Method.Post).AddJsonBody(requestBody);

            var createResponse = await _sut.RestHttpClient.ExecuteAsync<Result<string>>(postMakePaymentAccountRequest);

            var getPaymentAccountByIdRequest = new RestRequest($"{ApiHost}/byId/{createResponse.Data.Payload}");

            var getByIdResponse = await _sut.RestHttpClient.ExecuteAsync<Result<PaymentAccountResponse>>(getPaymentAccountByIdRequest);

            var getAccountById = getByIdResponse.Data.Payload;

            Assert.Multiple(() =>
            {
                getAccountById.InitialBalance.Should().Be(100);
                getAccountById.AccountType.Should().Be(AccountTypes.Virtual.Id);
                getAccountById.Balance.Should().Be(100);
            });
        }

        [Test]
        public async Task MakePaymentAccount_WhenCreateANewOnePaymentAccountWithJsonRequest_ReturnsNewGeneratedGuid()
        {
            const string requestBody = "{\"accountType\":1,\"currency\":\"BYN\",\"balance\":\"150\",\"agent\":\"Priorbank\",\"description\":\"Card for Salary\"}";

            var postMakePaymentAccountRequest = new RestRequest(ApiHost, Method.Post).AddJsonBody(requestBody);

            var response = await _sut.RestHttpClient.ExecuteAsync<Result<string>>(postMakePaymentAccountRequest);

            var result = response.Data;
            var payload = result.Payload;

            Guid.TryParse(payload, out _).Should().BeTrue();
        }

        [Test]
        public async Task RemovePaymentAccount_WithValidPaymentAccountRef_ThenSuccessful()
        {
            var paymentAccountId = (await SavePaymentAccountAsync()).Payload;

            var deletePaymentAccountRequest = new RestRequest($"{ApiHost}/{paymentAccountId}", Method.Delete);

            var response = await _sut.RestHttpClient.ExecuteAsync<Result<Guid>>(deletePaymentAccountRequest);

            var result = response.Data;

            result.IsSucceeded.Should().BeTrue();
        }

        [Test]
        public void RemovePaymentAccount_WithInValidPaymentAccountRef_ThenFail()
        {
            const string paymentAccountId = "Invalid";

            var deletePaymentAccountRequest = new RestRequest($"{ApiHost}/{paymentAccountId}", Method.Delete);

            var response = _sut.RestHttpClient.Execute<Result<Guid>>(deletePaymentAccountRequest);

            var result = response.Data;

            result.IsSucceeded.Should().BeFalse();
        }

        [Test]
        public void Update_WithInvalid_ThenFail()
        {
            const string paymentAccountId = "Invalid";

            var requestBody = new UpdatePaymentAccountRequest
            {
                Balance = 100,
                AccountType = AccountTypes.Cash.Id,
                Agent = "Vtb",
                Currency = "BYN",
                Description = "Some description"
            };

            var patchUpdatePaymentAccount = new RestRequest($"{ApiHost}/{paymentAccountId}", Method.Patch)
                .AddJsonBody(requestBody);

            var response = _sut.RestHttpClient.Execute<Result<string>>(patchUpdatePaymentAccount);

            var result = response.Data;

            result.IsSucceeded.Should().BeFalse();
        }

        [Test]
        public async Task Update_WithValid_ThenSuccessful()
        {
            var paymentAccountId = (await SavePaymentAccountAsync()).Payload;

            var requestBody = new UpdatePaymentAccountRequest
            {
                Balance = 150,
                AccountType = AccountTypes.Loan.Id,
                Agent = "Vtb Updated",
                Currency = "BYN Updated",
                Description = "Updated description"
            };

            var patchUpdatePaymentAccount = new RestRequest($"{ApiHost}/{paymentAccountId}", Method.Patch)
                .AddJsonBody(requestBody);

            var response = await _sut.RestHttpClient.ExecuteAsync<Result<string>>(patchUpdatePaymentAccount);

            var result = response.Data;

            result.IsSucceeded.Should().BeTrue();
        }

        private async Task<Result<Guid>> SavePaymentAccountAsync()
        {
            var requestSaveBody = new CreatePaymentAccountRequest
            {
                InitialBalance = 11.2m,
                Description = "test-account",
                AccountType = AccountTypes.Deposit.Id,
                Agent = "Personal",
                Currency = "usd"
            };

            var saveCategoryRequest = new RestRequest($"{Endpoints.PaymentAccounts}", Method.Post)
                .AddJsonBody(requestSaveBody);

            var paymentsHistoryResponse = await _sut.RestHttpClient
                .ExecuteAsync<Result<Guid>>(saveCategoryRequest);

            return paymentsHistoryResponse.Data;
        }
    }
}

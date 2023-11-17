using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using FluentAssertions;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Models.PaymentAccount;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Api.IntegrationTests.Constants;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    // [Ignore("Intend to be used only for local testing. Not appropriate infrastructure has been setup")]
    [Category(TestTypes.Integration)]
    [TestFixture]
    public class AccountingControllerTests : IAsyncDisposable
    {
        private const string ApiHost = "/paymentAccounts";

        private readonly AccountingTestWebApp _sut = new();

        [Test]
        public void GetPaymentAccounts_WhenTryToGetAllPaymentAccounts_ThenIsSuccessStatusCode()
        {
            var getPaymentAccountsRequest = new RestRequest(ApiHost);

            var response = _sut.RestHttpClient.Execute<Result<IReadOnlyCollection<PaymentAccount>>>(getPaymentAccountsRequest);

            Assert.IsTrue(response.IsSuccessful);
        }

        [Test]
        public void GetPaymentAccount_WhenValidFilterById_ReturnsAccountWithExpectedBalance()
        {
            const string paymentAccountId = "47d84ccf-7f79-4b6b-a691-3c2b313b0905";

            var getPaymentAccountByIdRequest = new RestRequest($"{ApiHost}/byId/{paymentAccountId}");

            var response = _sut.RestHttpClient.Execute<Result<PaymentAccount>>(getPaymentAccountByIdRequest);

            var result = response.Data;
            var payload = result.Payload;

            payload.Balance.Should().Be(20.24m);
        }

        [Test]
        public void GetPaymentAccount_WhenInValidFilterById_ReturnsFalseResult()
        {
            const string paymentAccountId = "invalidRef";

            var getPaymentAccountByIdRequest = new RestRequest($"{ApiHost}/byId/{paymentAccountId}");

            var response = _sut.RestHttpClient.Execute<Result<PaymentAccount>>(getPaymentAccountByIdRequest);

            var result = response.Data;

            result.IsSucceeded.Should().BeFalse();
        }

        [Test]
        public void MakePaymentAccount_WhenCreateANewOnePaymentAccount_ReturnsNewGeneratedGuid()
        {
            var requestBody = new CreatePaymentAccountRequest
            {
                Balance = 100,
                AccountType = AccountTypes.Cash,
                Agent = "Vtb",
                Currency = "",
                Description = "Some description"
            };

            var postMakePaymentAccountRequest = new RestRequest(ApiHost, Method.Post).AddJsonBody(requestBody);

            var response = _sut.RestHttpClient.Execute<Result<string>>(postMakePaymentAccountRequest);

            var result = response.Data;
            var payload = result.Payload;

            Guid.TryParse(payload, out _).Should().BeTrue();
        }

        [Test]
        public void RemovePaymentAccount_WithValidPaymentAccountRef_ThenSuccessful()
        {
            const string paymentAccountId = "47d84ccf-7f79-4b6b-a691-3c2b313b0905";

            var deleteMakePaymentAccountRequest = new RestRequest($"{ApiHost}/{paymentAccountId}", Method.Delete);

            var response = _sut.RestHttpClient.Execute<Result<bool>>(deleteMakePaymentAccountRequest);

            var result = response.Data;
            var payload = result.Payload;

            payload.Should().BeTrue();
        }

        [Test]
        public void RemovePaymentAccount_WithInValidPaymentAccountRef_ThenFail()
        {
            const string paymentAccountId = "Invalid";

            var deleteMakePaymentAccountRequest = new RestRequest($"{ApiHost}/{paymentAccountId}", Method.Delete);

            var response = _sut.RestHttpClient.Execute<Result<bool>>(deleteMakePaymentAccountRequest);

            var result = response.Data;
            var payload = result.Payload;

            payload.Should().BeFalse();
        }

        [Test]
        public void Update_WithInvalid_ThenFail()
        {
            const string paymentAccountId = "Invalid";

            var requestBody = new UpdatePaymentAccountRequest
            {
                Balance = 100,
                AccountType = AccountTypes.Cash,
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
        public void Update_WithValid_ThenSuccessful()
        {
            const string paymentAccountId = "257f78da-1e0f-4ce7-9c50-b494804a6830";

            var requestBody = new UpdatePaymentAccountRequest
            {
                Balance = 150,
                AccountType = AccountTypes.Loan,
                Agent = "Vtb Updated",
                Currency = "BYN Updated",
                Description = "Updated description"
            };

            var patchUpdatePaymentAccount = new RestRequest($"{ApiHost}/{paymentAccountId}", Method.Patch)
                .AddJsonBody(requestBody);

            var response = _sut.RestHttpClient.Execute<Result<string>>(patchUpdatePaymentAccount);

            var result = response.Data;

            result.IsSucceeded.Should().BeTrue();
        }

        public async ValueTask DisposeAsync()
        {
            if (_sut != null)
            {
                await _sut.DisposeAsync();
            }
        }
    }
}

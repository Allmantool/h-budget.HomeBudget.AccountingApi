using System;
using System.Collections.Generic;
using System.Linq;

using FluentAssertions;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Categories;
using HomeBudget.Components.Contractors;
using HomeBudget.Components.Operations;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    [TestFixture]
    [Category("Integration")]
    public class PaymentsHistoryControllerTests
    {
        private const string ApiHost = $"/{Endpoints.PaymentsHistory}";

        private readonly OperationsTestWebApp _sut = new();

        [Test]
        public void GetPaymentOperations_WhenTryToGetAllOperations_ThenIsSuccessStatusCode()
        {
            const string accountId = "92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84";
            var getOperationsRequest = new RestRequest($"{ApiHost}/{accountId}");

            var response = _sut.RestHttpClient.Execute<Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>>(getOperationsRequest);

            response.IsSuccessful.Should().BeTrue();
        }

        [Test]
        public void GetPaymentOperations_WithSeveralPaymentOperations_ThenBalanceHistoryHasBeenCalculatedCorrectly()
        {
            var paymentAccountId = Guid.Parse("aed5a7ff-cd0f-4c61-b5ab-a3d7b8f9ac64");

            foreach (var i in Enumerable.Range(1, 7))
            {
                var requestBody = new CreateOperationRequest
                {
                    Amount = 10 + i,
                    Comment = $"New operation - {i}",
                    CategoryId = MockCategoriesStore.Categories.First().Key.ToString(),
                    ContractorId = MockContractorsStore.Contractors.First().Key.ToString(),
                    OperationDate = new DateOnly(2023, 12, 15)
                };

                var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                    .AddJsonBody(requestBody);

                _sut.RestHttpClient.Execute<Result<CreateOperationResponse>>(postCreateRequest);
            }

            var getPaymentHistoryRecordsRequest = new RestRequest($"{Endpoints.PaymentsHistory}/{paymentAccountId}");

            var paymentsHistoryResponse = _sut.RestHttpClient
                .Execute<Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>>(getPaymentHistoryRecordsRequest);

            var historyRecords = paymentsHistoryResponse.Data.Payload;

            var accountHistoryRecords = MockOperationsHistoryStore.Records.Where(r => r.Record.PaymentAccountId.CompareTo(paymentAccountId) == 0).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(() => accountHistoryRecords.Count, Is.EqualTo(7).After(1).Seconds.PollEvery(250).MilliSeconds);
                Assert.That(() => historyRecords.Last().Balance, Is.EqualTo(98).After(1).Seconds.PollEvery(250).MilliSeconds);
            });
        }

        [Test]
        public void GetOperationById_WhenValidFilterById_ReturnsOperationWithExpectedAmount()
        {
            var operationId = Guid.Parse("2adb60a8-6367-4b8b-afa0-4ff7f7b1c92c");
            var accountId = Guid.Parse("92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84");

            MockOperationsHistoryStore.SetState(accountId, new List<PaymentOperationHistoryRecord>
            {
                new()
                {
                    Record = new PaymentOperation
                    {
                        PaymentAccountId = accountId,
                        Key = operationId,
                        Amount = 35.64m
                    }
                }
            });

            var getOperationByIdRequest = new RestRequest($"{ApiHost}/{accountId}/byId/{operationId}");

            var response = _sut.RestHttpClient.Execute<Result<PaymentOperationHistoryRecord>>(getOperationByIdRequest);

            var result = response.Data;
            var payload = result.Payload;

            payload.Record.Amount.Should().Be(35.64m);
        }

        [Test]
        public void GetOperationById_WhenInValidFilterById_ReturnsFalseResult()
        {
            const string operationId = "invalid-operation-ref";
            const string accountId = "invalid-acc-ref";

            var getOperationByIdRequest = new RestRequest($"{ApiHost}/{accountId}/byId/{operationId}");

            var response = _sut.RestHttpClient.Execute<Result<PaymentOperationHistoryRecord>>(getOperationByIdRequest);

            var result = response.Data;

            result.IsSucceeded.Should().BeFalse();
        }
    }
}

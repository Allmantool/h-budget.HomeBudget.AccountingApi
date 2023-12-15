using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Categories;
using HomeBudget.Components.Contractors;
using HomeBudget.Components.Operations;
using HomeBudget.Components.Accounts;

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
        public async Task GetPaymentOperations_WithSeveralPaymentOperations_ThenBalanceHistoryHasBeenCalculatedCorrectly()
        {
            const int createRequestAmount = 11;
            const decimal expectedBalance = 176M;
            var paymentAccountId = Guid.Parse("aed5a7ff-cd0f-4c61-b5ab-a3d7b8f9ac64");

            foreach (var i in Enumerable.Range(1, createRequestAmount))
            {
                var requestBody = new CreateOperationRequest
                {
                    Amount = 10 + i,
                    Comment = $"New operation - {i}",
                    CategoryId = MockCategoriesStore.Categories.First().Key.ToString(),
                    ContractorId = MockContractorsStore.Contractors.First().Key.ToString(),
                    OperationDate = new DateOnly(2023, 12, 15).AddDays(i)
                };

                var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                    .AddJsonBody(requestBody);

                var response = await _sut.RestHttpClient.ExecuteAsync(postCreateRequest);

                // TODO: concurrency issue (skip for now) -- temp workaround
                await Task.Delay(TimeSpan.FromSeconds(0.1), CancellationToken.None);
            }

            var getPaymentHistoryRecordsRequest = new RestRequest($"{Endpoints.PaymentsHistory}/{paymentAccountId}");

            var paymentsHistoryResponse = await _sut.RestHttpClient
                .ExecuteAsync<Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>>(getPaymentHistoryRecordsRequest);

            var historyRecords = paymentsHistoryResponse.Data.Payload;

            Assert.Multiple(() =>
            {
                MockOperationEventsStore.EventsForAccount(paymentAccountId).Count.Should().Be(createRequestAmount);
                MockAccountsStore.Records.Single(ac => ac.Key.CompareTo(paymentAccountId) == 0).Balance.Should().Be(expectedBalance);

                Assert.That(() => historyRecords.Count, Is.EqualTo(createRequestAmount));
                Assert.That(() => historyRecords.Last().Balance, Is.EqualTo(expectedBalance).After(10));
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

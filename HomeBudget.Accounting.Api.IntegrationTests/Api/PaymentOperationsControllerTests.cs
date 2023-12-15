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
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts;
using HomeBudget.Components.Categories;
using HomeBudget.Components.Contractors;
using HomeBudget.Components.Operations;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    [TestFixture]
    [Category("Integration")]
    public class PaymentOperationsControllerTests : IAsyncDisposable
    {
        private const string ApiHost = $"/{Endpoints.PaymentOperations}";

        private readonly OperationsTestWebApp _sut = new();

        [Test]
        public void CreateNewOperation_WhenCreateAnOperation_ShouldAddExtraPaymentOperationEvent()
        {
            var operationAmountBefore = MockOperationEventsStore.Events.Count;

            var requestBody = new CreateOperationRequest
            {
                Amount = 100,
                Comment = "New operation",
                CategoryId = MockCategoriesStore.Categories.First().Key.ToString(),
                ContractorId = MockContractorsStore.Contractors.First().Key.ToString(),
            };

            const string paymentAccountId = "92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84";

            var postCreateRequest = new RestRequest($"{ApiHost}/{paymentAccountId}", Method.Post)
                .AddJsonBody(requestBody);

            var response = _sut.RestHttpClient.Execute<Result<CreateOperationResponse>>(postCreateRequest);

            var result = response.Data;
            var payload = result.Payload;

            var operationAmountAfter = MockOperationEventsStore.Events.Count;

            Assert.Multiple(() =>
            {
                Guid.TryParse(payload.PaymentOperationId, out _).Should().BeTrue();
                Guid.TryParse(payload.PaymentAccountId, out _).Should().BeTrue();
                operationAmountBefore.Should().BeLessThan(operationAmountAfter);
            });
        }

        [Test]
        public void CreateNewOperation_WhenCreateAnOperation_ShouldAddExtraPaymentOperationHistoryRecord()
        {
            const string paymentAccountId = "c9b33506-9a98-4f76-ad8e-17c96858305b";

            var operationsAmountBefore = MockOperationsHistoryStore.Records
                .Count(r => r.Record.PaymentAccountId.CompareTo(Guid.Parse(paymentAccountId)) == 0);

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

                var postCreateRequest = new RestRequest($"{ApiHost}/{paymentAccountId}", Method.Post)
                    .AddJsonBody(requestBody);

                _sut.RestHttpClient.Execute<Result<CreateOperationResponse>>(postCreateRequest);
            }

            var getPaymentHistoryRecordsRequest = new RestRequest($"{Endpoints.PaymentsHistory}/{paymentAccountId}");

            var paymentsHistoryResponse = _sut.RestHttpClient
                .Execute<Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>>(getPaymentHistoryRecordsRequest);

            var operationAmountAfter = paymentsHistoryResponse.Data.Payload.Count;

            Assert.Multiple(() =>
            {
                operationsAmountBefore.Should().BeLessThan(operationAmountAfter);
            });
        }

        [Test]
        public void CreateNewOperation_WhenCreateAnOperation_BalanceShouldBeIncreased()
        {
            var requestBody = new CreateOperationRequest
            {
                Amount = 100,
                Comment = "New operation",
                CategoryId = MockCategoriesStore.Categories.First().Key.ToString(),
                ContractorId = MockContractorsStore.Contractors.First().Key.ToString(),
            };

            const string accountId = "92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84";

            var balanceBefore = MockAccountsStore.Records.Single(pa => pa.Key.Equals(Guid.Parse(accountId))).Balance;

            var postCreateRequest = new RestRequest($"{ApiHost}/{accountId}", Method.Post)
                .AddJsonBody(requestBody);

            _sut.RestHttpClient.Execute<Result<CreateOperationResponse>>(postCreateRequest);

            var operationAmountAfter = MockAccountsStore.Records.Single(pa => pa.Key.Equals(Guid.Parse(accountId))).Balance;

            Assert.Multiple(() =>
            {
                balanceBefore.Should().BeLessThan(operationAmountAfter);
            });
        }

        [Test]
        public void DeleteById_WithValidOperationRef_ThenSuccessful()
        {
            var operationId = Guid.Parse("5a53e3d3-0596-4ade-8aff-f3b3b956d0bd");
            var accountId = Guid.Parse("aed5a7ff-cd0f-4c65-b5ab-a3d7b8f9ac07");

            MockOperationsHistoryStore.SetState(new[]
            {
                new PaymentOperationHistoryRecord
                {
                    Balance = 25.24m,
                    Record = new PaymentOperation
                    {
                        Amount = 25.24m,
                        PaymentAccountId = accountId,
                        Key = operationId
                    }
                }
            });

            var balanceBefore = MockAccountsStore.Records
                .Single(pa => pa.Key.CompareTo(accountId) == 0)
                .Balance;

            var deleteOperationRequest = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Delete);

            _sut.RestHttpClient.Execute<Result<RemoveOperationResponse>>(deleteOperationRequest);

            var balanceAfter = MockAccountsStore.Records
                .Single(pa => pa.Key.CompareTo(accountId) == 0)
                .Balance;

            balanceBefore.Should().BeGreaterThan(balanceAfter);
        }

        [Test]
        public void DeleteById_WithValidOperationRef_OperationsAmountShouldBeDescriesed()
        {
            const string operationId = "20a8ca8e-0127-462c-b854-b2868490f3ec";
            const string accountId = "852530a6-70b0-4040-8912-8558d59d977a";

            MockOperationsHistoryStore.SetState(new[]
            {
                new PaymentOperationHistoryRecord
                {
                    Balance = 12.48m,
                    Record = new PaymentOperation
                    {
                        Amount = 12.48m,
                        PaymentAccountId = Guid.Parse(accountId),
                        Key = Guid.Parse(operationId)
                    }
                }
            });

            var operationAmountBefore = MockOperationsHistoryStore.Records
                .Count(r => r.Record.PaymentAccountId.CompareTo(Guid.Parse(accountId)) == 0);

            var deleteOperationRequest = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Delete);

            _sut.RestHttpClient.Execute<Result<RemoveOperationResponse>>(deleteOperationRequest);

            var operationAmountAfter = MockOperationsHistoryStore.Records
                .Count(r => r.Record.PaymentAccountId.CompareTo(Guid.Parse(accountId)) == 0);

            operationAmountBefore.Should().BeGreaterThan(operationAmountAfter);
        }

        [Test]
        public void DeleteById_WithInValidOperationRef_ThenFail()
        {
            const string operationId = "invalid-operation-ref";
            const string accountId = "invalid-acc-ref";

            var deleteOperationRequest = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Delete);

            var response = _sut.RestHttpClient.Execute<Result<RemoveOperationResponse>>(deleteOperationRequest);

            var result = response.Data;

            result.IsSucceeded.Should().BeFalse();
        }

        [Test]
        public void Update_WithInvalid_ThenFail()
        {
            const string operationId = "invalid-operation-ref";
            const string accountId = "invalid-acc-ref";

            var requestBody = new UpdateOperationRequest
            {
                Amount = 100,
                Comment = "Some description",
                CategoryId = MockCategoriesStore.Categories.First().CategoryKey,
                ContractorId = MockContractorsStore.Contractors.First().ContractorKey
            };

            var patchUpdateOperation = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Patch)
                .AddJsonBody(requestBody);

            var response = _sut.RestHttpClient.Execute<Result<UpdateOperationResponse>>(patchUpdateOperation);

            var result = response.Data;

            result.IsSucceeded.Should().BeFalse();
        }

        [Test]
        public void Update_WithValid_ThenSuccessful()
        {
            const string operationId = "2adb60a8-6367-4b8b-afa0-4ff7f7b1c92c";
            const string accountId = "92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84";

            var requestBody = new UpdateOperationRequest
            {
                Amount = 100,
                Comment = "Some update description",
                CategoryId = MockCategoriesStore.Categories.First().Key.ToString(),
                ContractorId = MockContractorsStore.Contractors.First().Key.ToString()
            };

            var patchUpdateOperation = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Patch)
                .AddJsonBody(requestBody);

            var response = _sut.RestHttpClient.Execute<Result<UpdateOperationResponse>>(patchUpdateOperation);

            var result = response.Data;

            Assert.Multiple(() =>
            {
                result.IsSucceeded.Should().BeTrue();
            });
        }

        [Test]
        public void Update_WithValid_BalanceShouldBeExpectedlyUpdated()
        {
            var operationId = Guid.Parse("2adb60a8-6367-4b8b-afa0-4ff7f7b1c92c");
            var accountId = Guid.Parse("35a40606-3782-4f53-8f64-49649b71ab6f");

            MockOperationEventsStore.Events.Add(new PaymentOperationEvent
            {
                EventType = EventTypes.Add,
                Payload = new PaymentOperation
                {
                    PaymentAccountId = accountId,
                    Key = operationId,
                    Amount = 12.0m
                }
            });

            var requestBody = new UpdateOperationRequest
            {
                Amount = 17.22m,
                Comment = "Some update description",
                CategoryId = MockCategoriesStore.Categories.First().Key.ToString(),
                ContractorId = MockContractorsStore.Contractors.First().Key.ToString()
            };

            var balanceBefore = MockAccountsStore.Records.Single(pa => pa.Key.CompareTo(accountId) == 0).Balance;

            var patchUpdateOperation = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Patch)
                .AddJsonBody(requestBody);

            _sut.RestHttpClient.Execute<Result<UpdateOperationResponse>>(patchUpdateOperation);

            var balanceAfter = MockAccountsStore.Records.Single(pa => pa.Key.CompareTo(accountId) == 0).Balance;

            balanceBefore.Should().BeLessThan(balanceAfter);
        }

        public ValueTask DisposeAsync()
        {
            return _sut?.DisposeAsync() ?? ValueTask.CompletedTask;
        }
    }
}

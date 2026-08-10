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
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Models.History;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Accounting.Api.Models.PaymentAccount;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [NonParallelizable]
    [Order(IntegrationTestOrderIndex.CrossAccountTransferControllerTests)]
    public class PaymentsHistoryRunningBalanceRegressionTests : BaseIntegrationTests
    {
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
        public async Task GetHistory_WithNonZeroOpeningBalanceAndOutgoingTransfers_ReturnsRunningBalancesAsync()
        {
            var senderAccountId = (await SavePaymentAccountAsync(34m)).Payload;
            var recipientAccountId = (await SavePaymentAccountAsync(0m)).Payload;
            var transferAmounts = new[] { 2m, 5.1m, 7.25m };
            var operationIds = new List<Guid>();

            foreach (var amount in transferAmounts)
            {
                var operationId = await ApplyTransferAsync(senderAccountId, recipientAccountId, amount);
                operationIds.Add(operationId);

                await WaitForHistoryAsync(
                    senderAccountId,
                    records => records.Count == operationIds.Count,
                    operationIds);
            }

            var history = await WaitForHistoryAsync(
                senderAccountId,
                records => records.Count == transferAmounts.Length &&
                           records.Select(record => record.Balance).SequenceEqual([32m, 26.9m, 19.65m]),
                operationIds);
            var account = await PaymentProjectionWaiter.WaitForPaymentAccountAsync(
                _restClient,
                senderAccountId,
                account => account.Balance == 19.65m,
                "authoritative account balance reconciles with completed history",
                operationIds,
                TestContext.CurrentContext.CancellationToken);
            var recipientHistory = await WaitForHistoryAsync(
                recipientAccountId,
                records => records.Count == transferAmounts.Length &&
                           records.Select(record => record.Balance).SequenceEqual([2m, 7.1m, 14.35m]),
                operationIds);
            var repeatedHistory = await GetHistoryAsync(senderAccountId);

            Assert.Multiple(() =>
            {
                history.Select(record => record.Record.Key).Should().Equal(operationIds);
                history.Select(record => record.Record.Amount).Should().Equal(-2m, -5.1m, -7.25m);
                history.Select(record => record.Balance).Should().Equal(32m, 26.9m, 19.65m);
                recipientHistory.Select(record => record.Record.Key).Should().Equal(operationIds);
                recipientHistory.Select(record => record.Record.Amount).Should().Equal(2m, 5.1m, 7.25m);
                recipientHistory.Select(record => record.Balance).Should().Equal(2m, 7.1m, 14.35m);
                repeatedHistory.Select(record => record.Record.Key).Should().Equal(history.Select(record => record.Record.Key));
                repeatedHistory.Select(record => record.Balance).Should().Equal(history.Select(record => record.Balance));
                history.Last().Balance.Should().Be(account.Balance);
            });
        }

        private async Task<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>> GetHistoryAsync(Guid accountId)
        {
            var response = await _restClient.ExecuteAsync<Result<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>>>(
                new RestRequest($"/{Endpoints.PaymentsHistory}/{accountId}"));

            response.IsSuccessful.Should().BeTrue(DescribeResponse(response));
            response.Data.IsSucceeded.Should().BeTrue(DescribeResponse(response));

            return response.Data.Payload;
        }

        private async Task<Guid> ApplyTransferAsync(Guid senderAccountId, Guid recipientAccountId, decimal amount)
        {
            var request = new CrossAccountsTransferRequest
            {
                Amount = amount,
                Recipient = recipientAccountId,
                Sender = senderAccountId,
                OperationAt = new DateOnly(2026, 8, 9),
                Multiplier = 1m
            };
            var response = await _restClient.ExecuteAsync<Result<CrossAccountsTransferResponse>>(
                new RestRequest($"/{Endpoints.CrossAccountsTransfer}", Method.Post).AddJsonBody(request));

            response.IsSuccessful.Should().BeTrue(DescribeResponse(response));
            response.Data.IsSucceeded.Should().BeTrue(DescribeResponse(response));

            return response.Data.Payload.PaymentOperationId;
        }

        private async Task<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>> WaitForHistoryAsync(
            Guid accountId,
            Func<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>, bool> condition,
            IEnumerable<Guid> operationIds,
            CancellationToken cancellationToken = default)
        {
            return await PaymentProjectionWaiter.WaitForHistoryRecordsAsync(
                _restClient,
                accountId,
                condition,
                "outgoing transfer history reaches the expected running balance",
                operationIds,
                cancellationToken);
        }

        private async Task<Result<Guid>> SavePaymentAccountAsync(decimal initialBalance)
        {
            var request = new CreatePaymentAccountRequest
            {
                InitialBalance = initialBalance,
                Description = "payments history balance regression account",
                AccountType = AccountTypes.Deposit.Key,
                Agent = "test-agent",
                Currency = CurrencyTypes.Usd.ToString()
            };
            var response = await _restClient.ExecuteAsync<Result<Guid>>(
                new RestRequest($"/{Endpoints.PaymentAccounts}", Method.Post).AddJsonBody(request));

            response.IsSuccessful.Should().BeTrue(DescribeResponse(response));
            response.Data.IsSucceeded.Should().BeTrue(DescribeResponse(response));

            return response.Data;
        }

        private static string DescribeResponse<T>(RestResponse<Result<T>> response)
        {
            return response == null
                ? "Response was null."
                : $"HTTP {(int)response.StatusCode} {response.StatusCode}, transport-success={response.IsSuccessful}, rest-error='{response.ErrorMessage}', domain-success={response.Data?.IsSucceeded}, status='{response.Data?.StatusMessage}', content='{response.Content}'";
        }
    }
}

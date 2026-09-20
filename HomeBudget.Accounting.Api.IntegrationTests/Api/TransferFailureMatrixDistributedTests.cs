using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.Extensions;
using HomeBudget.Accounting.Api.IntegrationTests.Filters;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Models.History;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Accounting.Api.Models.PaymentAccount;
using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [NonParallelizable]
    internal sealed class TransferFailureMatrixDistributedTests : BaseIntegrationTests
    {
        private const string TransferApi = "/cross-accounts-transfer";
        private const string HistoryApi = "/payments-history";
        private readonly CrossAccountsTransferWebApp _sut = new();
        private readonly CrossAccountsTransferWebApp _sqlUnavailableSut = new(
            services =>
            {
                services.RemoveAll<ITransferCommandStore>();
                services.AddSingleton<ITransferCommandStore, SqlUnavailableTransferCommandStore>();
            },
            initializeWorkers: false);
        private RestClient _client;
        private RestClient _clientAllowingErrors;
        private bool _mongoPaused;
        private bool _kafkaPaused;

        [OneTimeSetUp]
        public override async Task SetupAsync()
        {
            await OperationsTestWebApp.ResetAsync();
            await _sut.InitAsync();
            await _sqlUnavailableSut.InitAsync();
            await base.SetupAsync();
            _client = _sut.RestHttpClient;
            _clientAllowingErrors = _sut.RestHttpClientAllowingHttpErrors;
        }

        [TearDown]
        public async Task RestoreDisposableInfrastructureAsync()
        {
            if (TestContainers is null)
            {
                return;
            }

            await UnpauseMongoAsync();
            await UnpauseKafkaAsync();
            await TestContainers.KafkaContainer.WaitForKafkaReadyAsync(TimeSpan.FromMinutes(2));
            await _sut.RestartWorkersAsync();
        }

        [Test]
        public async Task Xfer001SqlUnavailableBeforeAcceptanceRejectsWithoutFinancialEffects()
        {
            var fixture = await CreateFixtureAsync("XFER-001");
            var response = await _sqlUnavailableSut.RestHttpClientAllowingHttpErrors
                .ExecuteAsync<Result<CrossAccountsTransferResponse>>(TransferRequest(fixture));

            response.IsSuccessful.Should().BeFalse("the SQL command store failed before durable acceptance");
            (await CountAsync(
                "SELECT COUNT(*) FROM dbo.TransferCommands WHERE SourceReference = @value",
                fixture.SourceReference)).Should().Be(0);
            (await HistoryAsync(fixture.SenderId)).Should().BeEmpty();
            (await HistoryAsync(fixture.RecipientId)).Should().BeEmpty();
        }

        [Test]
        public async Task Xfer002ResponseFailureAfterDurableRegistrationRecoversSameTwoSidedTransfer()
        {
            var fixture = await CreateFixtureAsync("XFER-002");
            var lostResponse = await _clientAllowingErrors.ExecuteAsync<Result<CrossAccountsTransferResponse>>(
                TransferRequest(fixture, discardResponse: true));

            lostResponse.IsSuccessful.Should().BeFalse("the test-only response filter aborts only after the action completed");
            var registration = await WaitForSqlRegistrationAsync(fixture.SourceReference);
            registration.TransferCount.Should().Be(1);
            registration.OutboxCount.Should().Be(2);

            var replay = await SubmitAsync(fixture);
            replay.Data.Payload.IsDuplicate.Should().BeTrue();
            replay.Data.Payload.PaymentOperationId.Should().Be(registration.TransferId);
            await AssertTerminalAsync(fixture, replay.Data.Payload);
        }

        [Test]
        public async Task Xfer005KafkaUnavailableRetainsDurableTransferAndRecoversBothSides()
        {
            var fixture = await CreateFixtureAsync("XFER-005");
            await TestContainers.KafkaContainer.PauseAsync();
            _kafkaPaused = true;

            var accepted = await SubmitAsync(fixture);
            var registration = await WaitForSqlRegistrationAsync(fixture.SourceReference);
            registration.TransferId.Should().Be(accepted.Data.Payload.PaymentOperationId);
            registration.OutboxCount.Should().Be(2);
            (await WaitForStatusAsync(accepted.Data.Payload, status => status is "Accepted" or "Published"))
                .Status.Should().NotBe("Projected");

            await UnpauseKafkaAsync();
            await TestContainers.KafkaContainer.WaitForKafkaReadyAsync(TimeSpan.FromMinutes(2));
            await AssertTerminalAsync(fixture, accepted.Data.Payload);
        }

        [Test]
        public async Task Xfer010WorkerRestartBeforePersistenceRecoversBothSides()
        {
            var fixture = await CreateFixtureAsync("XFER-010");
            await _sut.StopWorkersAsync();
            var accepted = await SubmitAsync(fixture);
            var boundary = await WaitForStatusAsync(accepted.Data.Payload, status => status is "Accepted" or "Published");

            boundary.Status.Should().BeOneOf("Accepted", "Published");
            boundary.PersistedAt.Should().BeNull();
            (await HistoryAsync(fixture.SenderId)).Should().BeEmpty();
            (await HistoryAsync(fixture.RecipientId)).Should().BeEmpty();

            await _sut.RestartWorkersAsync();
            await AssertTerminalAsync(fixture, accepted.Data.Payload);
        }

        [Test]
        public async Task Xfer011WorkerRestartDuringTransferRecoversPersistedSides()
        {
            var fixture = await CreateFixtureAsync("XFER-011");
            await _sut.RestartWorkersAsync(IntegrationWorkerProfile.PersistenceOnly);
            var accepted = await SubmitAsync(fixture);
            var persisted = await WaitForStatusAsync(
                accepted.Data.Payload,
                status => status == "Persisted",
                TimeSpan.FromSeconds(90));

            persisted.PersistedAt.Should().NotBeNull();
            persisted.ProjectedAt.Should().BeNull();
            await _sut.StopWorkersAsync();
            await _sut.RestartWorkersAsync();
            await AssertTerminalAsync(fixture, accepted.Data.Payload);
        }

        [Test]
        public async Task Xfer012ProjectionWorkerRestartReplaysPersistedSidesIntoMongo()
        {
            var fixture = await CreateFixtureAsync("XFER-012");
            await _sut.RestartWorkersAsync(IntegrationWorkerProfile.PersistenceOnly);
            var accepted = await SubmitAsync(fixture);
            var persisted = await WaitForStatusAsync(accepted.Data.Payload, status => status == "Persisted");
            persisted.Status.Should().Be("Persisted");
            persisted.PersistedAt.Should().NotBeNull();
            persisted.ProjectedAt.Should().BeNull();

            await _sut.StopWorkersAsync();
            await TestContainers.MongoDbContainer.PauseAsync();
            _mongoPaused = true;
            await _sut.RestartWorkersAsync(IntegrationWorkerProfile.ProjectionOnly);
            (await WaitForStatusAsync(accepted.Data.Payload, status => status == "Persisted")).Status.Should().Be("Persisted");
            await _sut.StopWorkersAsync();

            await UnpauseMongoAsync();
            await _sut.RestartWorkersAsync(IntegrationWorkerProfile.ProjectionOnly);
            await AssertTerminalAsync(fixture, accepted.Data.Payload);
        }

        [Test]
        public async Task Xfer017ConcurrentDuplicateRequestsRegisterOneTransferAndTwoSides()
        {
            var fixture = await CreateFixtureAsync("XFER-017");
            var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => SubmitAsync(fixture)));
            var first = responses[0].Data.Payload;

            responses.Should().OnlyContain(response => response.IsSuccessful && response.Data.IsSucceeded);
            responses.Select(response => response.Data.Payload.PaymentOperationId).Distinct().Should().ContainSingle();
            responses.Select(response => response.Data.Payload.CommandId).Distinct().Should().ContainSingle();
            responses.Count(response => response.Data.Payload.IsDuplicate).Should().BeGreaterThanOrEqualTo(7);
            var registration = await WaitForSqlRegistrationAsync(fixture.SourceReference);
            registration.TransferCount.Should().Be(1);
            registration.OutboxCount.Should().Be(2);
            await AssertTerminalAsync(fixture, first);
        }

        [Test]
        public async Task Xfer019ReplayAfterTransientKafkaRecoveryReusesIdentityAndDoesNotDuplicateEffects()
        {
            var fixture = await CreateFixtureAsync("XFER-019");
            await TestContainers.KafkaContainer.PauseAsync();
            _kafkaPaused = true;
            var accepted = await SubmitAsync(fixture);
            var registration = await WaitForSqlRegistrationAsync(fixture.SourceReference);
            registration.OutboxCount.Should().Be(2);

            await UnpauseKafkaAsync();
            await TestContainers.KafkaContainer.WaitForKafkaReadyAsync(TimeSpan.FromMinutes(2));
            var replay = await SubmitAsync(fixture);

            replay.Data.Payload.IsDuplicate.Should().BeTrue();
            replay.Data.Payload.PaymentOperationId.Should().Be(accepted.Data.Payload.PaymentOperationId);
            replay.Data.Payload.CommandId.Should().Be(accepted.Data.Payload.CommandId);
            await AssertTerminalAsync(fixture, replay.Data.Payload);
        }

        private async Task<TransferFixture> CreateFixtureAsync(string scenarioId)
        {
            var sender = await SaveAccountAsync(10000m, AccountTypes.Deposit, CurrencyTypes.Usd);
            var recipient = await SaveAccountAsync(100m, AccountTypes.Cash, CurrencyTypes.Byn);
            return new TransferFixture(
                sender,
                recipient,
                $"matrix-{scenarioId}-{Guid.NewGuid():N}",
                $"FamilyPro12:matrix:{scenarioId}:{Guid.NewGuid():N}");
        }

        private RestRequest TransferRequest(TransferFixture fixture, bool discardResponse = false)
        {
            var request = new RestRequest(TransferApi, Method.Post)
                .AddHeader("Idempotency-Key", fixture.IdempotencyKey)
                .AddJsonBody(new CrossAccountsTransferRequest
                {
                    Sender = fixture.SenderId,
                    Recipient = fixture.RecipientId,
                    SenderAmount = 1800m,
                    RecipientAmount = 6030m,
                    SenderCurrency = "USD",
                    RecipientCurrency = "BYN",
                    SourceReference = fixture.SourceReference,
                    OperationAt = new DateOnly(2025, 1, 24)
                });
            if (discardResponse)
            {
                request.AddHeader(DiscardResponseAfterAcceptedPaymentCommandFilter.HeaderName, "true");
            }

            return request;
        }

        private async Task<RestResponse<Result<CrossAccountsTransferResponse>>> SubmitAsync(TransferFixture fixture)
        {
            var response = await _client.ExecuteAsync<Result<CrossAccountsTransferResponse>>(TransferRequest(fixture));
            response.IsSuccessful.Should().BeTrue(Describe(response));
            response.Data.Should().NotBeNull(Describe(response));
            response.Data.IsSucceeded.Should().BeTrue(Describe(response));
            return response;
        }

        private async Task AssertTerminalAsync(TransferFixture fixture, CrossAccountsTransferResponse accepted)
        {
            var status = await WaitForStatusAsync(accepted, value => value == "Projected", TimeSpan.FromSeconds(90));
            var senderHistory = await WaitForHistoryAsync(fixture.SenderId, accepted.PaymentOperationId);
            var recipientHistory = await WaitForHistoryAsync(fixture.RecipientId, accepted.PaymentOperationId);
            var sender = senderHistory.Single(value => value.Record.Key == accepted.PaymentOperationId);
            var recipient = recipientHistory.Single(value => value.Record.Key == accepted.PaymentOperationId);

            Assert.Multiple(() =>
            {
                status.Status.Should().Be("Projected");
                status.TransferId.Should().Be(accepted.PaymentOperationId);
                status.SenderAccountId.Should().Be(fixture.SenderId);
                status.RecipientAccountId.Should().Be(fixture.RecipientId);
                status.SenderOperationId.Should().NotBe(Guid.Empty);
                status.RecipientOperationId.Should().NotBe(Guid.Empty);
                status.SenderAmount.Should().Be(1800m);
                status.RecipientAmount.Should().Be(6030m);
                status.SenderCurrency.Should().Be("USD");
                status.RecipientCurrency.Should().Be("BYN");
                sender.Record.PaymentAccountId.Should().Be(fixture.SenderId);
                sender.Record.RelatedPaymentAccountId.Should().Be(fixture.RecipientId);
                sender.Record.Amount.Should().Be(-1800m);
                sender.Record.ConversionMultiplier.Should().BeNull("exact-side transfers do not synthesize a multiplier");
                sender.Balance.Should().Be(8200m);
                recipient.Record.PaymentAccountId.Should().Be(fixture.RecipientId);
                recipient.Record.RelatedPaymentAccountId.Should().Be(fixture.SenderId);
                recipient.Record.Amount.Should().Be(6030m);
                recipient.Record.ConversionMultiplier.Should().BeNull("exact-side transfers do not synthesize a multiplier");
                recipient.Balance.Should().Be(6130m);
            });

            senderHistory.Count(value => value.Record.Key == accepted.PaymentOperationId).Should().Be(1);
            recipientHistory.Count(value => value.Record.Key == accepted.PaymentOperationId).Should().Be(1);
            (await CountAsync("SELECT COUNT(*) FROM dbo.TransferCommands WHERE TransferId = @value", accepted.PaymentOperationId))
                .Should().Be(1);
            (await CountAsync("SELECT COUNT(*) FROM dbo.OutboxAccountPayments WHERE OperationId = @value", accepted.PaymentOperationId))
                .Should().Be(2);
        }

        private async Task<TransferCommandStatusResponse> WaitForStatusAsync(
            CrossAccountsTransferResponse accepted,
            Func<string, bool> condition,
            TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
            TransferCommandStatusResponse last = null;
            while (DateTime.UtcNow < deadline)
            {
                var response = await _client.ExecuteAsync<Result<TransferCommandStatusResponse>>(
                    new RestRequest($"{TransferApi}/{accepted.PaymentOperationId}/commands/{accepted.CommandId}"));
                last = response.Data?.Payload;
                if (last is not null && condition(last.Status))
                {
                    return last;
                }

                await Task.Delay(250);
            }

            Assert.Fail($"Transfer {accepted.PaymentOperationId} did not reach the expected state. Last state: {last?.Status ?? "<none>"}.");
            return last;
        }

        private async Task<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>> WaitForHistoryAsync(
            Guid accountId,
            Guid transferId) => await PaymentProjectionWaiter.WaitForHistoryRecordsAsync(
                _client,
                accountId,
                records => records.Count(value => value.Record.Key == transferId) == 1,
                "exact transfer side becomes visible once",
                [transferId]);

        private async Task<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>> HistoryAsync(Guid accountId)
        {
            var response = await _client.ExecuteAsync<Result<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>>>(
                new RestRequest($"{HistoryApi}/{accountId}"));
            return response.Data?.Payload ?? [];
        }

        private async Task<Guid> SaveAccountAsync(decimal initialBalance, AccountTypes accountType, CurrencyTypes currency)
        {
            var response = await _client.ExecuteAsync<Result<Guid>>(
                new RestRequest($"/{Endpoints.PaymentAccounts}", Method.Post)
                    .AddJsonBody(new CreatePaymentAccountRequest
                    {
                        InitialBalance = initialBalance,
                        Description = "transfer-matrix",
                        AccountType = accountType.Key,
                        Agent = "release-verification",
                        Currency = currency.ToString()
                    }));
            response.IsSuccessful.Should().BeTrue(Describe(response));
            response.Data.IsSucceeded.Should().BeTrue(Describe(response));
            return response.Data.Payload;
        }

        private async Task<SqlRegistration> WaitForSqlRegistrationAsync(string sourceReference)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                await using var connection = new SqlConnection(TestContainers.AccountingDbConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT TransferId FROM dbo.TransferCommands WHERE SourceReference = @value";
                command.Parameters.AddWithValue("@value", sourceReference);
                var transfer = await command.ExecuteScalarAsync();
                if (transfer is Guid transferId)
                {
                    var transferCount = await CountAsync(
                        "SELECT COUNT(*) FROM dbo.TransferCommands WHERE TransferId = @value",
                        transferId);
                    var outboxCount = await CountAsync(
                        "SELECT COUNT(*) FROM dbo.OutboxAccountPayments WHERE OperationId = @value",
                        transferId);
                    return new SqlRegistration(transferId, transferCount, outboxCount);
                }

                await Task.Delay(100);
            }

            throw new TimeoutException($"No durable transfer registration was found for {sourceReference}.");
        }

        private async Task<int> WaitForCountAsync(
            string sql,
            object value,
            int minimum,
            TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            var count = 0;
            while (DateTime.UtcNow < deadline)
            {
                count = await CountAsync(sql, value);
                if (count >= minimum)
                {
                    return count;
                }

                await Task.Delay(250);
            }

            return count;
        }

        [SuppressMessage(
            "Security",
            "CA2100:Review SQL queries for security vulnerabilities",
            Justification = "SQL text is selected from test-owned literals; all scenario values remain parameters.")]
        private async Task<int> CountAsync(string sql, object value)
        {
            await using var connection = new SqlConnection(TestContainers.AccountingDbConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@value", value);
            return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        private async Task UnpauseMongoAsync()
        {
            if (!_mongoPaused)
            {
                return;
            }

            await TestContainers.MongoDbContainer.UnpauseAsync();
            _mongoPaused = false;
        }

        private async Task UnpauseKafkaAsync()
        {
            if (!_kafkaPaused)
            {
                return;
            }

            await TestContainers.KafkaContainer.UnpauseAsync();
            _kafkaPaused = false;
        }

        private static string Describe<T>(RestResponse<Result<T>> response) =>
            $"HTTP {(int)response.StatusCode} {response.StatusCode}; transport={response.IsSuccessful}; domain={response.Data?.IsSucceeded}; error={response.ErrorMessage}; status={response.Data?.StatusMessage}";

        private sealed class TransferFixture(
            Guid senderId,
            Guid recipientId,
            string idempotencyKey,
            string sourceReference)
        {
            public Guid SenderId { get; } = senderId;

            public Guid RecipientId { get; } = recipientId;

            public string IdempotencyKey { get; } = idempotencyKey;

            public string SourceReference { get; } = sourceReference;
        }

        private sealed class SqlRegistration(Guid transferId, int transferCount, int outboxCount)
        {
            public Guid TransferId { get; } = transferId;

            public int TransferCount { get; } = transferCount;

            public int OutboxCount { get; } = outboxCount;
        }

        private sealed class SqlUnavailableTransferCommandStore : ITransferCommandStore
        {
            public Task<TransferCommandRegistration> RegisterAsync(TransferCommandRegistrationRequest request) =>
                Task.FromException<TransferCommandRegistration>(Unavailable());

            public Task<TransferCommandRecord> GetByIdempotencyKeyAsync(string idempotencyKeyHash) =>
                Task.FromException<TransferCommandRecord>(Unavailable());

            public Task<TransferCommandRecord> GetAsync(Guid transferId, string commandId) =>
                Task.FromException<TransferCommandRecord>(Unavailable());

            public Task<TransferCommandRecord> GetByTransferIdAsync(Guid transferId) =>
                Task.FromException<TransferCommandRecord>(Unavailable());

            private static Exception Unavailable() => new SimulatedSqlUnavailableException();
        }

        private sealed class SimulatedSqlUnavailableException : DbException
        {
            public SimulatedSqlUnavailableException()
                : base("Simulated SQL unavailability before durable transfer registration.")
            {
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Microsoft.Data.Sqlite;

using HomeBudget.Accounting.Api.IntegrationTests;
using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.Extensions;
using HomeBudget.Accounting.Api.IntegrationTests.Release;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;

namespace HomeBudget.Accounting.ReleaseVerification
{
    [TestFixture]
    [NonParallelizable]
    [Category(TestTypes.Integration)]
    internal sealed class FamilyProDisposableReleaseTests
    {
        private const string DisposableIdentity = "disposable-familypro-release";
        private static readonly JsonSerializerOptions EvidenceJson = new() { WriteIndented = true };
        private TestContainersService _testContainers;

        [Test]
        [Explicit("Runs the full approved Family Pro dataset through real processes and disposable infrastructure.")]
        public async Task Full_approved_manifest_is_reconciled_and_second_run_has_zero_delta()
        {
            await FamilyProFullRunExecutionGate.ExecuteAsync(
                ReleaseInputs.FromEnvironment,
                ExecuteApprovedFullRunAsync);
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            if (_testContainers is not null)
            {
                await OperationsTestWebApp.ResetAsync();
                await _testContainers.DisposeAsync();
            }
        }

        private async Task ExecuteApprovedFullRunAsync(ReleaseInputs inputs)
        {
            var sessionDirectory = Path.Combine(
                inputs.EvidenceRoot,
                $"release-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(sessionDirectory);
            var validated = inputs.FullRunPlan.Materialize(Path.Combine(sessionDirectory, "validated-inputs"));
            inputs = inputs with { Manifest = validated.Manifest, Approval = validated.Approval };
            using var consoleCapture = ReleaseConsoleCapture.Start(
                Path.Combine(sessionDirectory, "testhost.stdout.log"),
                Path.Combine(sessionDirectory, "testhost.stderr.log"));

            await RunWithDisposableStackAsync(inputs, sessionDirectory, TimeSpan.FromHours(6), async (gatewayPort, _, token) =>
            {
                var stateDatabase = Path.Combine(sessionDirectory, "migration-state.sqlite");
                const string runId = "familypro-approved-disposable-release";
                const string batchId = "familypro-approved-disposable-release";
                CliResult first = null;
                await FamilyProFullRunExecutionGate.ExecuteSecondRunAsync(
                    async () =>
                    {
                        first = await RunCliAsync(
                            inputs,
                            new("import", gatewayPort, stateDatabase, Path.Combine(sessionDirectory, "run-1"),
                                runId, batchId, inputs.Manifest, inputs.Approval, ApprovedSubset: true),
                            token);
                        Assert.That(first.ExitCode, Is.Zero, first.Diagnostics);
                    },
                    () => ValidateReleaseEvidence(
                        first.OutputDirectory,
                        inputs.FullRunPlan.ExpectedResults,
                        expectSecondRunEvidence: false),
                    async () =>
                    {
                        var second = await RunCliAsync(
                            inputs,
                            new("import", gatewayPort, stateDatabase, Path.Combine(sessionDirectory, "run-2"),
                                runId, batchId, inputs.Manifest, inputs.Approval, ApprovedSubset: true),
                            token);
                        Assert.That(second.ExitCode, Is.Zero, second.Diagnostics);
                        ValidateReleaseEvidence(
                            second.OutputDirectory,
                            inputs.FullRunPlan.ExpectedResults,
                            expectSecondRunEvidence: true);
                    });
            });
        }

        [Test]
        [Explicit("Runs the sanitized small manifest through the real CLI and disposable distributed stack twice.")]
        public async Task Small_real_cli_fixture_reconciles_and_second_run_has_zero_delta()
        {
            var smallInputs = SmallReleaseInputs.FromEnvironment();
            var inputs = smallInputs.Runtime;
            var sessionDirectory = Path.Combine(
                inputs.EvidenceRoot,
                $"small-release-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(sessionDirectory);
            using var consoleCapture = ReleaseConsoleCapture.Start(
                Path.Combine(sessionDirectory, "testhost.stdout.log"),
                Path.Combine(sessionDirectory, "testhost.stderr.log"));

            var fixtureDirectory = await GenerateSmallFixtureAsync(
                smallInputs.FixtureDll,
                sessionDirectory,
                CancellationToken.None);
            var manifest = Path.Combine(fixtureDirectory, "approved-manifest.json");
            var approval = Path.Combine(fixtureDirectory, "approval.json");
            var expected = Path.Combine(fixtureDirectory, "independent-expected.json");

            await RunWithDisposableStackAsync(inputs, sessionDirectory, TimeSpan.FromMinutes(30), async (gatewayPort, _, token) =>
            {
                var stateDatabase = Path.Combine(sessionDirectory, "migration-state.sqlite");
                const string runId = "familypro-small-real-cli-release";
                const string batchId = "familypro-small-real-cli-release";
                var first = await RunCliAsync(
                    inputs,
                    new("import", gatewayPort, stateDatabase, Path.Combine(sessionDirectory, "run-1"),
                        runId, batchId, manifest, approval, ApprovedSubset: false),
                    token);
                Assert.That(first.ExitCode, Is.Zero, first.Diagnostics);
                ValidateSmallReleaseEvidence(first.OutputDirectory, expected, expectSecondRunEvidence: false);

                var second = await RunCliAsync(
                    inputs,
                    new("import", gatewayPort, stateDatabase, Path.Combine(sessionDirectory, "run-2"),
                        runId, batchId, manifest, approval, ApprovedSubset: false),
                    token);
                Assert.That(second.ExitCode, Is.Zero, second.Diagnostics);
                ValidateSmallReleaseEvidence(second.OutputDirectory, expected, expectSecondRunEvidence: true);
            });
        }

        [Test]
        [Explicit("Forcibly terminates the real CLI after durable acceptance, then resumes with the same journal and target.")]
        public async Task Actual_cli_process_kill_after_acceptance_resumes_without_duplicate_effects()
        {
            var smallInputs = SmallReleaseInputs.FromEnvironment();
            var inputs = smallInputs.Runtime;
            var sessionDirectory = Path.Combine(
                inputs.EvidenceRoot,
                $"process-recovery-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(sessionDirectory);
            using var consoleCapture = ReleaseConsoleCapture.Start(
                Path.Combine(sessionDirectory, "testhost.stdout.log"),
                Path.Combine(sessionDirectory, "testhost.stderr.log"));
            var fixtureDirectory = await GenerateSmallFixtureAsync(
                smallInputs.FixtureDll,
                sessionDirectory,
                CancellationToken.None);
            var manifest = Path.Combine(fixtureDirectory, "approved-manifest.json");
            var approval = Path.Combine(fixtureDirectory, "approval.json");
            var expected = Path.Combine(fixtureDirectory, "independent-expected.json");

            await RunWithDisposableStackAsync(inputs, sessionDirectory, TimeSpan.FromMinutes(30), async (gatewayPort, accounting, token) =>
            {
                var stateDatabase = Path.Combine(sessionDirectory, "migration-state.sqlite");
                const string runId = "familypro-small-process-recovery";
                const string batchId = "familypro-small-process-recovery";
                var interruptedInvocation = new CliInvocation(
                    "import",
                    gatewayPort,
                    stateDatabase,
                    Path.Combine(sessionDirectory, "interrupted-run"),
                    runId,
                    batchId,
                    manifest,
                    approval,
                    ApprovedSubset: false);

                await accounting.StopWorkersAsync();
                await using var interrupted = StartCliProcess(inputs, interruptedInvocation);
                var beforeKill = await WaitForDurablePaymentAsync(stateDatabase, TimeSpan.FromMinutes(2), token);
                await WriteJsonAsync(Path.Combine(sessionDirectory, "journal-before-kill.json"), beforeKill, token);
                var sourceAccountId = ExpectedAccountId(expected, beforeKill.SourcePrimaryKey);
                var targetAccountId = await ReadAccountMappingAsync(
                    stateDatabase,
                    $"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:SCHETA:{sourceAccountId}",
                    token);
                var serverStatus = await CapturePaymentCommandAsync(
                    gatewayPort,
                    targetAccountId,
                    beforeKill.CommandId,
                    Path.Combine(sessionDirectory, "server-command-before-kill.json"),
                    token);
                Assert.That(serverStatus, Is.Not.EqualTo("Projected").IgnoreCase);

                await interrupted.StopAsync();
                var afterKill = await RecoverAndReadMigrationStateAfterKillAsync(
                    stateDatabase,
                    beforeKill.IdempotencyKey,
                    beforeKill.TargetEntityType,
                    token);
                AssertStableReplayIdentity(beforeKill, afterKill);
                await WriteJsonAsync(Path.Combine(sessionDirectory, "journal-after-kill.json"), afterKill, token);

                await accounting.RestartWorkersAsync();
                var resumed = await RunCliAsync(
                    inputs,
                    interruptedInvocation with
                    {
                        Mode = "resume",
                        OutputDirectory = Path.Combine(sessionDirectory, "resumed-run")
                    },
                    token);
                Assert.That(resumed.ExitCode, Is.Zero, resumed.Diagnostics);
                ValidateSmallReleaseEvidence(resumed.OutputDirectory, expected, expectSecondRunEvidence: false);

                var terminal = await ReadMigrationStateAsync(
                    stateDatabase,
                    beforeKill.IdempotencyKey,
                    beforeKill.TargetEntityType,
                    token);
                AssertStableReplayIdentity(beforeKill, terminal);
                Assert.That(terminal.Status, Is.EqualTo("Verified"));
                await WriteJsonAsync(Path.Combine(sessionDirectory, "journal-after-resume.json"), terminal, token);
                await WriteJsonAsync(
                    Path.Combine(sessionDirectory, "process-recovery-evidence.json"),
                    new
                    {
                        InterruptedProcessExitCode = interrupted.ExitCode,
                        ServerStatusBeforeKill = serverStatus,
                        StableIdempotencyKey = terminal.IdempotencyKey,
                        StableRequestFingerprint = terminal.RequestFingerprint,
                        StableTargetId = terminal.TargetId,
                        StableCommandId = terminal.CommandId,
                        TerminalStatus = terminal.Status,
                        Reconciliation = "Passed"
                    },
                    token);
            });
        }

        [Test]
        [Explicit("Interrupts the real transfer workflow at durable acceptance and verifies two-sided convergence after resume.")]
        public async Task Actual_transfer_interruption_after_acceptance_resumes_to_one_two_sided_transfer()
        {
            var smallInputs = SmallReleaseInputs.FromEnvironment();
            var inputs = smallInputs.Runtime;
            var sessionDirectory = Path.Combine(
                inputs.EvidenceRoot,
                $"transfer-recovery-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(sessionDirectory);
            using var consoleCapture = ReleaseConsoleCapture.Start(
                Path.Combine(sessionDirectory, "testhost.stdout.log"),
                Path.Combine(sessionDirectory, "testhost.stderr.log"));
            var fixtureDirectory = await GenerateSmallFixtureAsync(
                smallInputs.FixtureDll,
                sessionDirectory,
                CancellationToken.None);
            var manifest = Path.Combine(fixtureDirectory, "approved-manifest.json");
            var approval = Path.Combine(fixtureDirectory, "approval.json");
            var expected = Path.Combine(fixtureDirectory, "independent-expected.json");

            await RunWithDisposableStackAsync(inputs, sessionDirectory, TimeSpan.FromMinutes(30), async (gatewayPort, accounting, token) =>
            {
                var stateDatabase = Path.Combine(sessionDirectory, "migration-state.sqlite");
                const string runId = "familypro-small-transfer-recovery";
                const string batchId = "familypro-small-transfer-recovery";
                var initialInvocation = new CliInvocation(
                    "import",
                    gatewayPort,
                    stateDatabase,
                    Path.Combine(sessionDirectory, "pre-transfer-run"),
                    runId,
                    batchId,
                    manifest,
                    approval,
                    ApprovedSubset: false);

                await using (var initial = StartCliProcess(inputs, initialInvocation))
                {
                    await WaitForExecutionPhaseAsync(
                        Path.Combine(initialInvocation.OutputDirectory, "execution-progress.jsonl"),
                        "transfers",
                        "Started",
                        TimeSpan.FromMinutes(2),
                        token);
                    await initial.StopAsync();
                }

                await accounting.StopWorkersAsync();
                var heldInvocation = initialInvocation with
                {
                    Mode = "resume",
                    OutputDirectory = Path.Combine(sessionDirectory, "held-transfer-run")
                };
                await using var held = StartCliProcess(inputs, heldInvocation);
                var beforeKill = await WaitForDurableTransferAsync(stateDatabase, TimeSpan.FromMinutes(2), token);
                await WriteJsonAsync(Path.Combine(sessionDirectory, "transfer-journal-before-kill.json"), beforeKill, token);
                var serverStatus = await CaptureTransferCommandAsync(
                    gatewayPort,
                    Guid.Parse(beforeKill.TargetId),
                    beforeKill.CommandId,
                    Path.Combine(sessionDirectory, "transfer-command-before-kill.json"),
                    token);
                Assert.That(serverStatus, Is.Not.EqualTo("Projected").IgnoreCase);

                await held.StopAsync();
                var afterKill = await RecoverAndReadMigrationStateAfterKillAsync(
                    stateDatabase,
                    beforeKill.IdempotencyKey,
                    beforeKill.TargetEntityType,
                    token);
                AssertStableReplayIdentity(beforeKill, afterKill);
                await WriteJsonAsync(Path.Combine(sessionDirectory, "transfer-journal-after-kill.json"), afterKill, token);

                await accounting.RestartWorkersAsync();
                var resumed = await RunCliAsync(
                    inputs,
                    heldInvocation with { OutputDirectory = Path.Combine(sessionDirectory, "resumed-run") },
                    token);
                Assert.That(resumed.ExitCode, Is.Zero, resumed.Diagnostics);
                ValidateSmallReleaseEvidence(resumed.OutputDirectory, expected, expectSecondRunEvidence: false);

                var terminal = await ReadMigrationStateAsync(
                    stateDatabase,
                    beforeKill.IdempotencyKey,
                    beforeKill.TargetEntityType,
                    token);
                AssertStableReplayIdentity(beforeKill, terminal);
                Assert.Multiple(() =>
                {
                    Assert.That(terminal.Status, Is.EqualTo("Verified"));
                    Assert.That(terminal.SenderOperationId, Is.Not.Null.And.Not.Empty);
                    Assert.That(terminal.RecipientOperationId, Is.Not.Null.And.Not.Empty);
                });
                await WriteJsonAsync(Path.Combine(sessionDirectory, "transfer-journal-after-resume.json"), terminal, token);
                await WriteJsonAsync(
                    Path.Combine(sessionDirectory, "transfer-recovery-evidence.json"),
                    new
                    {
                        InterruptedProcessExitCode = held.ExitCode,
                        ServerStatusBeforeKill = serverStatus,
                        StableIdempotencyKey = terminal.IdempotencyKey,
                        StableRequestFingerprint = terminal.RequestFingerprint,
                        StableTransferId = terminal.TargetId,
                        StableCommandId = terminal.CommandId,
                        terminal.SenderOperationId,
                        terminal.RecipientOperationId,
                        TerminalStatus = terminal.Status,
                        Reconciliation = "Passed",
                        LogicalTransfers = 1,
                        TransferSides = 2
                    },
                    token);
            });
        }

        private async Task RunWithDisposableStackAsync(
            ReleaseInputs inputs,
            string sessionDirectory,
            TimeSpan deadline,
            Func<int, ReleaseWorkerTestWebApp, CancellationToken, Task> execute)
        {
            _testContainers ??= await TestContainersService.InitAsync();

            // Release scenarios share the fixture-owned containers, but never target state.
            // Reset before starting the next scenario so deterministic source identities from
            // a previous test cannot satisfy a recovery boundary without new processing.
            await OperationsTestWebApp.ResetAsync();
            await using var accounting = new ReleaseWorkerTestWebApp();
            var apiPort = GetAvailablePort();
            const string notificationBaseUrlVariable = "NotificationPublisherOptions__AccountingApiBaseUrl";
            var previousNotificationBaseUrl = Environment.GetEnvironmentVariable(notificationBaseUrlVariable);
            try
            {
                Environment.SetEnvironmentVariable(notificationBaseUrlVariable, $"http://127.0.0.1:{apiPort}");
                Assert.That(
                    await accounting.InitAsync(workersMaxAmount: 1, requireIntegrationCategory: false),
                    Is.True,
                    "The disposable Accounting stack did not initialize.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(notificationBaseUrlVariable, previousNotificationBaseUrl);
            }

            await using var api = await StartAccountingApiAsync(inputs, _testContainers, apiPort, sessionDirectory);
            await WaitForApiAsync(apiPort, api, CancellationToken.None);
            var gatewayPort = GetAvailablePort();
            var gatewayDirectory = PrepareGatewayConfiguration(
                inputs.GatewayContentRoot,
                new Uri($"http://127.0.0.1:{apiPort}"),
                gatewayPort,
                sessionDirectory);
            await using var gateway = StartGateway(inputs.GatewayDll, gatewayDirectory, gatewayPort, sessionDirectory);
            try
            {
                using var timeout = new CancellationTokenSource(deadline);
                await WaitForGatewayAsync(gatewayPort, gateway, timeout.Token);
                await execute(gatewayPort, accounting, timeout.Token);
            }
            finally
            {
                await StopProcessAsync(gateway);
                await StopProcessAsync(api);
                await accounting.StopWorkersAsync();
                await ReleaseContainerDiagnostics.CaptureAsync(_testContainers, sessionDirectory, CancellationToken.None);
            }
        }

        private static string PrepareGatewayConfiguration(
            string sourceContentRoot,
            Uri accountingBaseAddress,
            int gatewayPort,
            string sessionDirectory)
        {
            var target = Path.Combine(sessionDirectory, "gateway");
            Directory.CreateDirectory(target);

            var sourceOcelot = Path.Combine(sourceContentRoot, "ocelot.json");
            var root = JsonNode.Parse(File.ReadAllText(sourceOcelot))?.AsObject()
                ?? throw new InvalidDataException($"Gateway route configuration is invalid: {sourceOcelot}");
            foreach (var route in root["Routes"]?.AsArray().OfType<JsonObject>() ?? [])
            {
                if (route["DownstreamHostAndPorts"] is not JsonArray destinations)
                {
                    continue;
                }

                foreach (var destination in destinations.OfType<JsonObject>())
                {
                    destination["Host"] = accountingBaseAddress.Host;
                    destination["Port"] = accountingBaseAddress.Port;
                }
            }

            var globalConfiguration = root["GlobalConfiguration"]?.AsObject()
                ?? throw new InvalidDataException("Gateway global configuration is missing.");
            globalConfiguration["BaseUrl"] = $"http://127.0.0.1:{gatewayPort}";
            File.WriteAllText(
                Path.Combine(target, "ocelot.json"),
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Copy(Path.Combine(sourceContentRoot, "appsettings.json"), Path.Combine(target, "appsettings.json"));
            File.Copy(
                Path.Combine(sourceContentRoot, "appsettings.Development.json"),
                Path.Combine(target, "appsettings.Development.json"));
            return target;
        }

        private static CapturedReleaseProcess StartGateway(
            string gatewayDll,
            string contentRoot,
            int port,
            string evidenceDirectory)
        {
            var start = CreateDotNetStartInfo(gatewayDll, contentRoot);
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["DOTNET_ENVIRONMENT"] = "Development";
            start.Environment["SslOptions__HttpPort"] = port.ToString(CultureInfo.InvariantCulture);
            start.Environment["MigrationContract__EnvironmentIdentity"] = DisposableIdentity;
            start.Environment["MigrationContract__InstanceIdentity"] = $"release-{Environment.ProcessId}";
            start.Environment["MigrationContract__Commit"] = "disposable-release-verification";

            return CapturedReleaseProcess.Start(
                start,
                Path.Combine(evidenceDirectory, "gateway.stdout.log"),
                Path.Combine(evidenceDirectory, "gateway.stderr.log"));
        }

        private static async Task<CapturedReleaseProcess> StartAccountingApiAsync(
            ReleaseInputs inputs,
            TestContainersService containers,
            int port,
            string evidenceDirectory)
        {
            var kafka = await containers.KafkaContainer.GetReachableBootstrapAsync();
            var start = CreateDotNetStartInfo(inputs.AccountingDll, inputs.AccountingContentRoot);
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Integration";
            start.Environment["DOTNET_ENVIRONMENT"] = "Integration";
            start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
            start.Environment["DatabaseConnectionOptions__ConnectionString"] = containers.AccountingDbConnectionString;
            start.Environment["DatabaseConnectionOptions__RedisConnectionString"] = "release-no-redis";
            start.Environment["KafkaOptions__ProducerSettings__BootstrapServers"] = kafka;
            start.Environment["KafkaOptions__ConsumerSettings__BootstrapServers"] = kafka;
            start.Environment["KafkaOptions__AdminSettings__BootstrapServers"] = kafka;
            start.Environment["MongoDbOptions__ConnectionString"] = containers.MongoDbContainer.GetConnectionString();
            start.Environment["MongoDbOptions__PaymentsHistory"] = "payments_history_test";
            start.Environment["MongoDbOptions__HandBooks"] = "handbooks_test";
            start.Environment["MongoDbOptions__PaymentAccounts"] = "payment_accounts_test";
            start.Environment["MongoDbOptions__LedgerDatabase"] = "ledger_test";
            start.Environment["EventStoreDb__Url"] = containers.EventSourceDbContainer.GetConnectionString();
            start.Environment["ElasticSearchOptions__IsEnabled"] = "false";
            start.Environment["SeqOptions__IsEnabled"] = "false";
            start.Environment["ObservabilityOptions__TelemetryEndpoint"] = string.Empty;
            start.Environment["ObservabilityOptions__LogsEndpoint"] = string.Empty;

            return CapturedReleaseProcess.Start(
                start,
                Path.Combine(evidenceDirectory, "accounting-api.stdout.log"),
                Path.Combine(evidenceDirectory, "accounting-api.stderr.log"));
        }

        private static async Task WaitForApiAsync(int port, CapturedReleaseProcess api, CancellationToken token)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var endpoint = new Uri($"http://127.0.0.1:{port}/payment-accounts");
            var deadline = DateTime.UtcNow.AddMinutes(2);
            Exception lastError = null;

            while (DateTime.UtcNow < deadline)
            {
                if (api.HasExited)
                {
                    Assert.Fail($"Accounting API exited during startup with code {api.ExitCode}.");
                }

                try
                {
                    using var response = await client.GetAsync(endpoint, token);
                    return;
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    lastError = exception;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), token);
            }

            Assert.Fail($"Accounting API did not become ready within two minutes. Last error: {lastError?.Message}");
        }

        private static async Task WaitForGatewayAsync(
            int port,
            CapturedReleaseProcess gateway,
            CancellationToken token)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var identity = new Uri($"http://127.0.0.1:{port}/gateway/meta/identity");
            var deadline = DateTime.UtcNow.AddMinutes(2);
            Exception lastError = null;

            while (DateTime.UtcNow < deadline)
            {
                if (gateway.HasExited)
                {
                    Assert.Fail($"Gateway exited during startup with code {gateway.ExitCode}.");
                }

                try
                {
                    using var response = await client.GetAsync(identity, token);
                    var body = await response.Content.ReadAsStringAsync(token);
                    if (response.StatusCode == HttpStatusCode.OK &&
                        body.Contains(DisposableIdentity, StringComparison.Ordinal))
                    {
                        return;
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    lastError = exception;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), token);
            }

            Assert.Fail($"Gateway did not become ready within two minutes. Last error: {lastError?.Message}");
        }

        private static async Task<CliResult> RunCliAsync(
            ReleaseInputs inputs,
            CliInvocation invocation,
            CancellationToken token)
        {
            await using var process = StartCliProcess(inputs, invocation);
            var progressPath = Path.Combine(invocation.OutputDirectory, "execution-progress.jsonl");
            await ReleaseProcessMonitor.WaitForExitAsync(
                process,
                () => File.Exists(progressPath) ? new FileInfo(progressPath).Length : 0,
                TimeSpan.FromMinutes(15),
                TimeSpan.FromSeconds(5),
                token);
            return new CliResult(
                process.ExitCode,
                invocation.OutputDirectory,
                process.StandardOutputTail,
                process.StandardErrorTail);
        }

        private static CapturedReleaseProcess StartCliProcess(ReleaseInputs inputs, CliInvocation invocation)
        {
            Directory.CreateDirectory(invocation.OutputDirectory);
            var start = CreateDotNetStartInfo(inputs.CliDll, inputs.CliWorkingDirectory);
            AddArguments(
                start,
                "--mode",
                invocation.Mode,
                "--manifest",
                invocation.Manifest,
                "--approval",
                invocation.Approval,
                "--approved-subset",
                invocation.ApprovedSubset ? "true" : "false",
                "--target-url",
                $"http://127.0.0.1:{invocation.GatewayPort}",
                "--expected-gateway-environment",
                DisposableIdentity,
                "--expected-target",
                DisposableIdentity,
                "--confirm-target",
                DisposableIdentity,
                "--state-db",
                invocation.StateDatabase,
                "--output",
                invocation.OutputDirectory,
                "--log-dir",
                Path.Combine(invocation.OutputDirectory, "logs"),
                "--log-format",
                "json",
                "--readback-attempts",
                "3000",
                "--readback-delay-ms",
                "25",
                "--max-in-flight",
                "128",
                "--progress-interval",
                "15",
                "--run-id",
                invocation.RunId,
                "--batch-id",
                invocation.BatchId,
                "--quiet",
                "true");
            return CapturedReleaseProcess.Start(
                start,
                Path.Combine(invocation.OutputDirectory, "cli.stdout.log"),
                Path.Combine(invocation.OutputDirectory, "cli.stderr.log"));
        }

        private static async Task<string> GenerateSmallFixtureAsync(
            string fixtureDll,
            string sessionDirectory,
            CancellationToken token)
        {
            var fixtureDirectory = Path.Combine(sessionDirectory, "fixture");
            var start = CreateDotNetStartInfo(
                fixtureDll,
                Path.GetDirectoryName(fixtureDll)
                    ?? throw new InvalidOperationException("The fixture generator assembly has no parent directory."));
            start.ArgumentList.Add(fixtureDirectory);
            await using var process = CapturedReleaseProcess.Start(
                start,
                Path.Combine(sessionDirectory, "fixture-generator.stdout.log"),
                Path.Combine(sessionDirectory, "fixture-generator.stderr.log"));
            await process.WaitForExitAsync(TimeSpan.FromMinutes(2), token);
            Assert.That(
                process.ExitCode,
                Is.Zero,
                $"Fixture generator failed.{Environment.NewLine}{process.StandardErrorTail}");
            foreach (var file in new[] { "approved-manifest.json", "approval.json", "independent-expected.json" })
            {
                Assert.That(File.Exists(Path.Combine(fixtureDirectory, file)), Is.True, $"Fixture artifact is missing: {file}");
            }

            return fixtureDirectory;
        }

        private static async Task<RecoveryStateSnapshot> WaitForDurablePaymentAsync(
            string stateDatabase,
            TimeSpan deadline,
            CancellationToken token) =>
            await WaitForDurableStateAsync(stateDatabase, "PaymentOperation", deadline, token);

        private static async Task<RecoveryStateSnapshot> WaitForDurableTransferAsync(
            string stateDatabase,
            TimeSpan deadline,
            CancellationToken token) =>
            await WaitForDurableStateAsync(stateDatabase, "Transfer", deadline, token);

        private static async Task<RecoveryStateSnapshot> WaitForDurableStateAsync(
            string stateDatabase,
            string targetEntityType,
            TimeSpan deadline,
            CancellationToken token)
        {
            var expiresAt = DateTime.UtcNow + deadline;
            Exception lastError = null;
            while (DateTime.UtcNow < expiresAt)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var record = await ReadFirstDurableStateAsync(stateDatabase, targetEntityType, token);
                    if (record is not null)
                    {
                        return record;
                    }
                }
                catch (Exception exception) when (exception is SqliteException or IOException)
                {
                    lastError = exception;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), token);
            }

            throw new TimeoutException($"No accepted, unprojected {targetEntityType} appeared within {deadline}. Last error: {lastError?.Message}");
        }

        private static async Task<RecoveryStateSnapshot> ReadFirstDurableStateAsync(
            string stateDatabase,
            string targetEntityType,
            CancellationToken token)
        {
            if (!File.Exists(stateDatabase))
            {
                return null;
            }

            await using var connection = new SqliteConnection(ReadOnlyConnectionString(stateDatabase));
            await connection.OpenAsync(token);
            var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT sourcePrimaryKey, targetEntityType, idempotencyKey, requestFingerprint,
                       status, targetId, targetOperationId, commandId,
                       senderOperationId, recipientOperationId, updatedAtUtc
                  FROM migration_records
                 WHERE targetEntityType = $targetEntityType
                   AND status IN ('Accepted', 'WaitingForProjection')
                   AND targetId IS NOT NULL
                   AND commandId IS NOT NULL
                 ORDER BY updatedAtUtc
                 LIMIT 1
                """;
            command.Parameters.AddWithValue("$targetEntityType", targetEntityType);
            await using var reader = await command.ExecuteReaderAsync(token);
            return await reader.ReadAsync(token) ? Snapshot(reader) : null;
        }

        private static Task<RecoveryStateSnapshot> RecoverAndReadMigrationStateAfterKillAsync(
            string stateDatabase,
            string idempotencyKey,
            string targetEntityType,
            CancellationToken token)
        {
            // A forcibly terminated CLI can leave a hot journal. Only this immediate post-kill
            // lookup opens read/write so SQLite can perform crash recovery before the SELECT.
            return ReadMigrationStateWithConnectionAsync(
                RecoveryReadableConnectionString(stateDatabase),
                idempotencyKey,
                targetEntityType,
                token);
        }

        private static Task<RecoveryStateSnapshot> ReadMigrationStateAsync(
            string stateDatabase,
            string idempotencyKey,
            string targetEntityType,
            CancellationToken token) =>
            ReadMigrationStateWithConnectionAsync(
                ReadOnlyConnectionString(stateDatabase),
                idempotencyKey,
                targetEntityType,
                token);

        private static async Task<RecoveryStateSnapshot> ReadMigrationStateWithConnectionAsync(
            string connectionString,
            string idempotencyKey,
            string targetEntityType,
            CancellationToken token)
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(token);
            var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT sourcePrimaryKey, targetEntityType, idempotencyKey, requestFingerprint,
                       status, targetId, targetOperationId, commandId,
                       senderOperationId, recipientOperationId, updatedAtUtc
                  FROM migration_records
                 WHERE idempotencyKey = $idempotencyKey
                   AND targetEntityType = $targetEntityType
                """;
            command.Parameters.AddWithValue("$idempotencyKey", idempotencyKey);
            command.Parameters.AddWithValue("$targetEntityType", targetEntityType);
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token))
            {
                throw new InvalidOperationException($"Migration state '{targetEntityType}:{idempotencyKey}' disappeared.");
            }

            return Snapshot(reader);
        }

        private static RecoveryStateSnapshot Snapshot(SqliteDataReader reader) => new(
            reader.GetString(reader.GetOrdinal("sourcePrimaryKey")),
            reader.GetString(reader.GetOrdinal("targetEntityType")),
            reader.GetString(reader.GetOrdinal("idempotencyKey")),
            reader.GetString(reader.GetOrdinal("requestFingerprint")),
            reader.GetString(reader.GetOrdinal("status")),
            ReadNullable(reader, "targetId"),
            ReadNullable(reader, "targetOperationId"),
            reader.GetString(reader.GetOrdinal("commandId")),
            ReadNullable(reader, "senderOperationId"),
            ReadNullable(reader, "recipientOperationId"),
            reader.GetString(reader.GetOrdinal("updatedAtUtc")));

        private static async Task<Guid> ReadAccountMappingAsync(
            string stateDatabase,
            string sourceIdentity,
            CancellationToken token)
        {
            await using var connection = new SqliteConnection(ReadOnlyConnectionString(stateDatabase));
            await connection.OpenAsync(token);
            var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT targetGuid
                  FROM migration_mappings
                 WHERE targetEntityType = 'PaymentAccount'
                   AND sourceIdentity = $sourceIdentity
                   AND status = 'Verified'
                """;
            command.Parameters.AddWithValue("$sourceIdentity", sourceIdentity);
            var value = await command.ExecuteScalarAsync(token)
                ?? throw new InvalidOperationException($"Verified account mapping is missing for '{sourceIdentity}'.");
            return Guid.Parse((string)value);
        }

        private static int ExpectedAccountId(string expectedPath, string sourceIdentity)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(expectedPath));
            var operation = document.RootElement.GetProperty("Ordinary").EnumerateArray()
                .Single(value => string.Equals(
                    value.GetProperty("SourceIdentity").GetString(),
                    sourceIdentity,
                    StringComparison.Ordinal));
            return operation.GetProperty("Account").GetInt32();
        }

        private static async Task<string> CapturePaymentCommandAsync(
            int gatewayPort,
            Guid accountId,
            string commandId,
            string evidencePath,
            CancellationToken token)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var endpoint = new Uri(
                $"http://127.0.0.1:{gatewayPort}/gateway/accounting/payment-operations/{accountId}/commands/{Uri.EscapeDataString(commandId)}");
            using var response = await client.GetAsync(endpoint, token);
            var body = await response.Content.ReadAsStringAsync(token);
            await File.WriteAllTextAsync(evidencePath, body, token);
            Assert.That(response.IsSuccessStatusCode, Is.True, body);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("payload").GetProperty("status").GetString();
        }

        private static async Task<string> CaptureTransferCommandAsync(
            int gatewayPort,
            Guid transferId,
            string commandId,
            string evidencePath,
            CancellationToken token)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var endpoint = new Uri(
                $"http://127.0.0.1:{gatewayPort}/gateway/accounting/cross-accounts-transfer/{transferId}/commands/{Uri.EscapeDataString(commandId)}");
            using var response = await client.GetAsync(endpoint, token);
            var body = await response.Content.ReadAsStringAsync(token);
            await File.WriteAllTextAsync(evidencePath, body, token);
            Assert.That(response.IsSuccessStatusCode, Is.True, body);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("payload").GetProperty("status").GetString();
        }

        private static async Task WaitForExecutionPhaseAsync(
            string progressPath,
            string phase,
            string status,
            TimeSpan deadline,
            CancellationToken token)
        {
            var expiresAt = DateTime.UtcNow + deadline;
            while (DateTime.UtcNow < expiresAt)
            {
                token.ThrowIfCancellationRequested();
                var contents = await ReadProgressFileAsync(progressPath, token);
                if (contents is not null && ContainsCheckpoint(contents, phase, status))
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(10), token);
            }

            throw new TimeoutException($"Execution phase '{phase}:{status}' was not observed within {deadline}.");
        }

        private static async Task<string> ReadProgressFileAsync(string progressPath, CancellationToken token)
        {
            if (!File.Exists(progressPath))
            {
                return null;
            }

            try
            {
                await using var stream = new FileStream(
                    progressPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync(token);
            }
            catch (IOException)
            {
                // The producer owns the file and may briefly hold it while appending.
                return null;
            }
        }

        private static bool ContainsCheckpoint(string contents, string phase, string status)
        {
            foreach (var line in contents.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (string.Equals(root.GetProperty("Phase").GetString(), phase, StringComparison.Ordinal) &&
                        string.Equals(root.GetProperty("Status").GetString(), status, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                catch (JsonException)
                {
                    // Ignore only the incomplete tail record while the producer is flushing it.
                }
            }

            return false;
        }

        private static void AssertStableReplayIdentity(
            RecoveryStateSnapshot expected,
            RecoveryStateSnapshot actual)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual.SourcePrimaryKey, Is.EqualTo(expected.SourcePrimaryKey));
                Assert.That(actual.IdempotencyKey, Is.EqualTo(expected.IdempotencyKey));
                Assert.That(actual.RequestFingerprint, Is.EqualTo(expected.RequestFingerprint));
                Assert.That(actual.TargetId, Is.EqualTo(expected.TargetId));
                Assert.That(actual.TargetOperationId, Is.EqualTo(expected.TargetOperationId));
                Assert.That(actual.CommandId, Is.EqualTo(expected.CommandId));
            });
        }

        private static Task WriteJsonAsync(string path, object value, CancellationToken token) =>
            File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, EvidenceJson), token);

        private static string ReadOnlyConnectionString(string path) => new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        private static string RecoveryReadableConnectionString(string path) => new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        private static string ReadNullable(SqliteDataReader reader, string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        private static void ValidateReleaseEvidence(
            string outputDirectory,
            FamilyProExpectedResults expected,
            bool expectSecondRunEvidence)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(outputDirectory, "approved-release-evidence.json")));
            var root = document.RootElement;
            var plan = root.GetProperty("Plan");
            Assert.That(plan.GetProperty("Accounts").GetInt32(), Is.EqualTo(expected.Accounts));
            Assert.That(plan.GetProperty("Categories").GetInt32(), Is.EqualTo(expected.Categories));
            Assert.That(plan.GetProperty("Contractors").GetInt32(), Is.EqualTo(expected.Contractors));
            Assert.That(plan.GetProperty("OrdinaryPayments").GetInt32(), Is.EqualTo(expected.OrdinaryPayments));
            Assert.That(plan.GetProperty("SplitLines").GetInt32(), Is.EqualTo(expected.SplitPayments));
            Assert.That(plan.GetProperty("MigrationAdjustments").GetInt32(), Is.EqualTo(expected.MigrationAdjustments));
            Assert.That(plan.GetProperty("LogicalTransfers").GetInt32(), Is.EqualTo(expected.Transfers));
            Assert.That(plan.GetProperty("TransferSides").GetInt32(), Is.EqualTo(expected.TransferSides));
            Assert.That(plan.GetProperty("AdjustmentSignedTotal").GetDecimal(), Is.EqualTo(expected.AdjustmentSignedTotal));
            Assert.That(plan.GetProperty("AdjustmentAbsoluteTotal").GetDecimal(), Is.EqualTo(expected.AdjustmentAbsoluteTotal));
            Assert.That(plan.GetProperty("UnresolvedDecisionKeys").GetArrayLength(), Is.EqualTo(expected.UnresolvedRecords));

            var snapshot = root.GetProperty("TargetSnapshot");
            Assert.That(snapshot.GetProperty("Accounts").GetInt32(), Is.EqualTo(expected.Accounts));
            Assert.That(snapshot.GetProperty("Categories").GetInt32(), Is.EqualTo(expected.Categories));
            Assert.That(snapshot.GetProperty("Contractors").GetInt32(), Is.EqualTo(expected.Contractors));
            Assert.That(snapshot.GetProperty("OrdinaryPayments").GetInt32(), Is.EqualTo(expected.OrdinaryPayments));
            Assert.That(snapshot.GetProperty("SplitPayments").GetInt32(), Is.EqualTo(expected.SplitPayments));
            Assert.That(snapshot.GetProperty("MigrationAdjustments").GetInt32(), Is.EqualTo(expected.MigrationAdjustments));
            Assert.That(snapshot.GetProperty("Transfers").GetInt32(), Is.EqualTo(expected.Transfers));
            Assert.That(snapshot.GetProperty("TransferSides").GetInt32(), Is.EqualTo(expected.TransferSides));

            var reconciliation = root.GetProperty("Reconciliation");
            Assert.That(reconciliation.GetProperty("Passed").GetBoolean(), Is.True);
            Assert.That(reconciliation.GetProperty("Issues").GetArrayLength(), Is.Zero);
            Assert.That(reconciliation.GetProperty("AccountCount").GetInt32(), Is.EqualTo(expected.Accounts));
            Assert.That(reconciliation.GetProperty("AccountsVerified").GetInt32(), Is.EqualTo(expected.BalancedAccounts));
            Assert.That(reconciliation.GetProperty("AccountBalances").GetArrayLength(), Is.EqualTo(expected.Accounts));
            Assert.That(
                reconciliation.GetProperty("Adjustments").GetProperty("VerifiedCount").GetInt32(),
                Is.EqualTo(expected.MigrationAdjustments));

            if (!expectSecondRunEvidence)
            {
                return;
            }

            using var idempotency = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(outputDirectory, "second-run-idempotency.json")));
            var idempotencyRoot = idempotency.RootElement;
            Assert.That(idempotencyRoot.GetProperty("ZeroAdditionalLogicalEffects").GetBoolean(), Is.True);
            foreach (var delta in idempotencyRoot.GetProperty("Delta").EnumerateObject())
            {
                Assert.That(delta.Value.GetInt32(), Is.Zero, $"Second-run delta {delta.Name} was not zero.");
            }
        }

        private static void ValidateSmallReleaseEvidence(
            string outputDirectory,
            string independentExpectedPath,
            bool expectSecondRunEvidence)
        {
            using var expectedDocument = JsonDocument.Parse(File.ReadAllText(independentExpectedPath));
            var expected = expectedDocument.RootElement;
            using var evidenceDocument = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(outputDirectory, "approved-release-evidence.json")));
            var evidence = evidenceDocument.RootElement;
            var plan = evidence.GetProperty("Plan");
            Assert.That(plan.GetProperty("Accounts").GetInt32(), Is.EqualTo(expected.GetProperty("Accounts").GetArrayLength()));
            Assert.That(plan.GetProperty("OrdinaryPayments").GetInt32(), Is.EqualTo(expected.GetProperty("Ordinary").GetArrayLength()));
            Assert.That(plan.GetProperty("SplitLines").GetInt32(), Is.EqualTo(expected.GetProperty("Splits").GetArrayLength()));
            Assert.That(plan.GetProperty("MigrationAdjustments").GetInt32(), Is.EqualTo(expected.GetProperty("Adjustments").GetArrayLength()));
            Assert.That(plan.GetProperty("LogicalTransfers").GetInt32(), Is.EqualTo(expected.GetProperty("Transfers").GetArrayLength()));
            Assert.That(plan.GetProperty("TransferSides").GetInt32(), Is.EqualTo(2));
            Assert.That(plan.GetProperty("UnresolvedDecisionKeys").GetArrayLength(), Is.Zero);

            var reconciliation = evidence.GetProperty("Reconciliation");
            Assert.That(reconciliation.GetProperty("Passed").GetBoolean(), Is.True);
            Assert.That(reconciliation.GetProperty("Issues").GetArrayLength(), Is.Zero);
            Assert.That(reconciliation.GetProperty("AccountsVerified").GetInt32(), Is.EqualTo(2));
            Assert.That(reconciliation.GetProperty("Adjustments").GetProperty("VerifiedCount").GetInt32(), Is.EqualTo(1));

            var actualBalances = reconciliation.GetProperty("AccountBalances").EnumerateArray()
                .ToDictionary(value => value.GetProperty("SourceAccountId").GetInt32());
            foreach (var expectedAccount in expected.GetProperty("Accounts").EnumerateArray())
            {
                var sourceAccountId = expectedAccount.GetProperty("SourceAccountId").GetInt32();
                var actual = actualBalances[sourceAccountId];
                Assert.That(actual.GetProperty("Currency").GetString(), Is.EqualTo(expectedAccount.GetProperty("Currency").GetString()));
                Assert.That(actual.GetProperty("ExpectedApprovedBalance").GetDecimal(), Is.EqualTo(expectedAccount.GetProperty("ExpectedBalance").GetDecimal()));
                Assert.That(actual.GetProperty("TargetHistoryDerivedBalance").GetDecimal(), Is.EqualTo(expectedAccount.GetProperty("ExpectedBalance").GetDecimal()));
                Assert.That(actual.GetProperty("TargetCurrentBalance").GetDecimal(), Is.EqualTo(expectedAccount.GetProperty("ExpectedBalance").GetDecimal()));
            }

            var stableIdentities = evidence.GetProperty("StableMigrationIdentities").EnumerateArray()
                .Select(value => value.GetProperty("SourceIdentity").GetString())
                .ToHashSet(StringComparer.Ordinal);
            foreach (var groupName in new[] { "Ordinary", "Splits", "Adjustments", "Transfers" })
            {
                foreach (var item in expected.GetProperty(groupName).EnumerateArray())
                {
                    Assert.That(stableIdentities, Does.Contain(item.GetProperty("SourceIdentity").GetString()));
                }
            }

            if (expectSecondRunEvidence)
            {
                ValidateZeroDelta(Path.Combine(outputDirectory, "second-run-idempotency.json"));
            }
        }

        private static void ValidateZeroDelta(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            Assert.That(root.GetProperty("ZeroAdditionalLogicalEffects").GetBoolean(), Is.True);
            foreach (var delta in root.GetProperty("Delta").EnumerateObject())
            {
                Assert.That(delta.Value.GetInt32(), Is.Zero, $"Second-run delta {delta.Name} was not zero.");
            }
        }

        private static ProcessStartInfo CreateDotNetStartInfo(string dll, string workingDirectory)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(dll);
            return start;
        }

        private static void AddArguments(ProcessStartInfo start, params string[] arguments)
        {
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }
        }

        private static Task StopProcessAsync(CapturedReleaseProcess process)
        {
            return process.StopAsync();
        }

        private static int GetAvailablePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private sealed record CliResult(int ExitCode, string OutputDirectory, string StandardOutput, string StandardError)
        {
            public string Diagnostics =>
                $"CLI exited with {ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{StandardError}";
        }

        private sealed record CliInvocation(
            string Mode,
            int GatewayPort,
            string StateDatabase,
            string OutputDirectory,
            string RunId,
            string BatchId,
            string Manifest,
            string Approval,
            bool ApprovedSubset);

        private sealed record RecoveryStateSnapshot(
            string SourcePrimaryKey,
            string TargetEntityType,
            string IdempotencyKey,
            string RequestFingerprint,
            string Status,
            string TargetId,
            string TargetOperationId,
            string CommandId,
            string SenderOperationId,
            string RecipientOperationId,
            string UpdatedAtUtc);

        internal sealed record ReleaseInputs(
            string AccountingDll,
            string AccountingContentRoot,
            string GatewayDll,
            string GatewayContentRoot,
            string CliDll,
            string CliWorkingDirectory,
            string Manifest,
            string Approval,
            string EvidenceRoot,
            FamilyProFullRunPlan FullRunPlan)
        {
            public static ReleaseInputs FromEnvironment()
            {
                var runtime = RuntimeFromEnvironment();
                var selection = FamilyProFullRunSelection.FromEnvironment();
                var plan = FamilyProFullRunPlan.Load(selection);
                plan.RequireRuntimeLayout(
                    runtime.AccountingDll,
                    runtime.AccountingContentRoot,
                    runtime.GatewayDll,
                    runtime.GatewayContentRoot,
                    runtime.CliDll,
                    typeof(HomeBudget.Accounting.Workers.OperationsConsumer.Program).Assembly.Location);
                plan.RequireRuntimeArtifact(runtime.AccountingDll, RequiredValue("FAMILYPRO_RELEASE_ACCOUNTING_SHA256"));
                plan.RequireRuntimeArtifact(runtime.GatewayDll, RequiredValue("FAMILYPRO_RELEASE_GATEWAY_SHA256"));
                plan.RequireRuntimeArtifact(runtime.CliDll, RequiredValue("FAMILYPRO_RELEASE_CLI_SHA256"));
                plan.RequireRuntimeArtifact(
                    typeof(HomeBudget.Accounting.Workers.OperationsConsumer.Program).Assembly.Location,
                    RequiredValue("FAMILYPRO_RELEASE_WORKER_SHA256"));
                return runtime with
                {
                    Manifest = selection.ManifestPath,
                    Approval = selection.ApprovalPath,
                    FullRunPlan = plan
                };
            }

            public static ReleaseInputs RuntimeFromEnvironment()
            {
                var gatewayDll = RequiredFile("FAMILYPRO_RELEASE_GATEWAY_DLL");
                var cliDll = RequiredFile("FAMILYPRO_RELEASE_CLI_DLL");
                return new ReleaseInputs(
                    RequiredFile("FAMILYPRO_RELEASE_ACCOUNTING_DLL"),
                    RequiredDirectory("FAMILYPRO_RELEASE_ACCOUNTING_CONTENT_ROOT"),
                    gatewayDll,
                    RequiredDirectory("FAMILYPRO_RELEASE_GATEWAY_CONTENT_ROOT"),
                    cliDll,
                    Path.GetDirectoryName(cliDll)
                        ?? throw new InvalidOperationException("The CLI assembly has no parent directory."),
                    string.Empty,
                    string.Empty,
                    RequiredDirectory("FAMILYPRO_RELEASE_EVIDENCE_ROOT"),
                    null);
            }

            private static string RequiredFile(string name)
            {
                var value = RequiredValue(name);
                return File.Exists(value)
                    ? Path.GetFullPath(value)
                    : throw new FileNotFoundException($"Environment variable {name} does not identify a file.", value);
            }

            private static string RequiredDirectory(string name)
            {
                var value = RequiredValue(name);
                return Directory.Exists(value)
                    ? Path.GetFullPath(value)
                    : throw new DirectoryNotFoundException($"Environment variable {name} does not identify a directory: {value}");
            }

            private static string RequiredValue(string name) =>
                Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                    ? value
                    : throw new InvalidOperationException($"Required environment variable {name} is not set.");
        }

        private sealed record SmallReleaseInputs(ReleaseInputs Runtime, string FixtureDll)
        {
            public static SmallReleaseInputs FromEnvironment() => new(
                ReleaseInputs.RuntimeFromEnvironment(),
                RequiredFile("FAMILYPRO_RELEASE_FIXTURE_DLL"));

            private static string RequiredFile(string name)
            {
                var value = Environment.GetEnvironmentVariable(name);
                return value is { Length: > 0 } && File.Exists(value)
                    ? Path.GetFullPath(value)
                    : throw new FileNotFoundException($"Environment variable {name} does not identify a file.", value);
            }
        }
    }
}

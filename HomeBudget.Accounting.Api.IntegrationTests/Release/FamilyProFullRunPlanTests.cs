using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests;
using HomeBudget.Accounting.ReleaseVerification;

namespace HomeBudget.Accounting.ReleaseVerification.Tests
{
    [TestFixture]
    internal sealed class FamilyProFullRunPlanTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), $"familypro-plan-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_directory, recursive: true);
        }

        [Test]
        public void Approved_candidate_47_v2_input_drives_expectations()
        {
            var fixture = FamilyProFullRunPlanFixture.Create(_directory);

            var plan = FamilyProFullRunPlan.Load(fixture.Selection);

            Assert.Multiple(() =>
            {
                Assert.That(plan.ApplicationBaselineId, Is.EqualTo("final-candidate-20260920-47"));
                Assert.That(plan.ExpectedResults.OrdinaryPayments, Is.EqualTo(18_572));
                Assert.That(plan.ExpectedResults.MigrationAdjustments, Is.EqualTo(30));
                Assert.That(plan.ExpectedResults.Transfers, Is.EqualTo(834));
                Assert.That(plan.ExpectedResults.TransferSides, Is.EqualTo(1_668));
                Assert.That(plan.ExpectedResults.UnresolvedRecords, Is.Zero);
            });
        }

        [Test]
        public void Stale_candidate_40_expectations_are_rejected()
        {
            var fixture = FamilyProFullRunPlanFixture.Create(_directory);
            fixture.ChangeExpectedCounts(18_545, 29, 833, 1_666, 5);

            var action = () => FamilyProFullRunPlan.Load(fixture.Selection);

            Assert.That(action, Throws.TypeOf<InvalidDataException>());
        }

        [TestCase("missing")]
        [TestCase("malformed")]
        [TestCase("unsupported-schema")]
        [TestCase("missing-count")]
        [TestCase("missing-unresolved")]
        [TestCase("negative-count")]
        [TestCase("duplicate-unresolved")]
        public void Invalid_expected_results_fail_closed(string defect)
        {
            var fixture = FamilyProFullRunPlanFixture.Create(_directory);
            fixture.ApplyExpectedResultsDefect(defect);

            var action = () => FamilyProFullRunPlan.Load(fixture.Selection);

            Assert.That(action, Throws.Exception);
        }

        [TestCase("source")]
        [TestCase("policy")]
        [TestCase("manifest")]
        [TestCase("expected-digest")]
        [TestCase("application")]
        [TestCase("harness")]
        public void Binding_or_execution_identity_failure_is_rejected(string binding)
        {
            var fixture = FamilyProFullRunPlanFixture.Create(_directory);
            fixture.ApplyBindingDefect(binding);

            var action = () => FamilyProFullRunPlan.Load(fixture.Selection);

            Assert.That(action, Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void Runtime_artifact_must_match_the_retained_runtime_attempt()
        {
            var fixture = FamilyProFullRunPlanFixture.Create(_directory);
            var runtime = fixture.AddRuntimeArtifact();
            var plan = FamilyProFullRunPlan.Load(fixture.Selection);

            Assert.That(() => plan.RequireRuntimeArtifact(runtime.Path, runtime.Sha256), Throws.Nothing);
            Assert.That(
                () => plan.RequireRuntimeArtifact(runtime.Path, new string('0', 64)),
                Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void Runtime_artifact_copy_outside_its_sealed_path_is_rejected()
        {
            var fixture = FamilyProFullRunPlanFixture.Create(_directory);
            var runtime = fixture.AddRuntimeArtifact();
            var copiedPath = Path.Combine(_directory, "substitute", "runtime.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(copiedPath));
            File.Copy(runtime.Path, copiedPath);
            var plan = FamilyProFullRunPlan.Load(fixture.Selection);

            Assert.That(
                () => plan.RequireRuntimeArtifact(copiedPath, runtime.Sha256),
                Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void Runtime_layout_requires_exact_content_roots_and_complete_inventory()
        {
            var fixture = FamilyProFullRunPlanFixture.Create(_directory);
            var runtime = fixture.AddRuntimeLayout();
            var plan = FamilyProFullRunPlan.Load(fixture.Selection);

            Assert.That(
                () => plan.RequireRuntimeLayout(
                    runtime.AccountingDll,
                    runtime.AccountingRoot,
                    runtime.GatewayDll,
                    runtime.GatewayRoot,
                    runtime.CliDll,
                    runtime.WorkerDll),
                Throws.Nothing);
            Assert.That(
                () => plan.RequireRuntimeLayout(
                    runtime.AccountingDll,
                    runtime.GatewayRoot,
                    runtime.GatewayDll,
                    runtime.GatewayRoot,
                    runtime.CliDll,
                    runtime.WorkerDll),
                Throws.TypeOf<InvalidDataException>());

            fixture.AddUninventoriedRuntimeFile();

            Assert.That(
                () => plan.RequireRuntimeLayout(
                    runtime.AccountingDll,
                    runtime.AccountingRoot,
                    runtime.GatewayDll,
                    runtime.GatewayRoot,
                    runtime.CliDll,
                    runtime.WorkerDll),
                Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void Validated_bytes_are_materialized_even_if_selected_file_changes()
        {
            var fixture = FamilyProFullRunPlanFixture.Create(_directory);
            var plan = FamilyProFullRunPlan.Load(fixture.Selection);
            fixture.ChangeExpectedCounts(7, 2, 3, 6, 0);

            var inputs = plan.Materialize(Path.Combine(_directory, "validated"));
            using var expected = JsonDocument.Parse(File.ReadAllBytes(inputs.ExpectedResults));

            Assert.That(
                expected.RootElement.GetProperty("approvedTargetCounts").GetProperty("ordinaryPayments").GetInt32(),
                Is.EqualTo(18_572));
            Assert.That(FamilyProFullRunPlan.ComputeSha256(inputs.ExpectedResults), Is.EqualTo(plan.ExpectedResultsSha256));
        }

        [Test]
        public async Task Invalid_eligibility_never_invokes_provisioning()
        {
            var provisionCalls = 0;

            var action = async () => await FamilyProFullRunExecutionGate.ExecuteAsync<object>(
                () => throw new InvalidDataException("invalid plan"),
                _ =>
                {
                    provisionCalls++;
                    return Task.CompletedTask;
                });

            await Assert.ThatAsync(action, Throws.TypeOf<InvalidDataException>());
            Assert.That(provisionCalls, Is.Zero);
        }

        [Test]
        public void Release_fixture_has_no_eager_container_base_or_parent_setup_scope()
        {
            Assert.Multiple(() =>
            {
                Assert.That(typeof(FamilyProDisposableReleaseTests).IsSubclassOf(typeof(BaseIntegrationTests)), Is.False);
                Assert.That(
                    typeof(FamilyProDisposableReleaseTests).Namespace,
                    Does.Not.StartWith(typeof(GlobalTestContainerSetup).Namespace));
            });
        }

        [Test]
        public void Run_two_is_blocked_when_terminal_run_one_reconciliation_fails()
        {
            var secondRunCalls = 0;

            var action = async () => await FamilyProFullRunExecutionGate.ExecuteSecondRunAsync(
                () => Task.CompletedTask,
                () => throw new AssertionException("identity reconciliation failed"),
                () =>
                {
                    secondRunCalls++;
                    return Task.CompletedTask;
                });

            Assert.That(action, Throws.TypeOf<AssertionException>());
            Assert.That(secondRunCalls, Is.Zero);
        }

        [Test]
        public void Synthetic_counts_prove_loader_is_parameterized()
        {
            var fixture = FamilyProFullRunPlanFixture.Create(_directory);
            fixture.ChangeManifestAndExpectedCounts(7, 2, 3, 6);

            var plan = FamilyProFullRunPlan.Load(fixture.Selection);

            Assert.Multiple(() =>
            {
                Assert.That(plan.ExpectedResults.OrdinaryPayments, Is.EqualTo(7));
                Assert.That(plan.ExpectedResults.MigrationAdjustments, Is.EqualTo(2));
                Assert.That(plan.ExpectedResults.Transfers, Is.EqualTo(3));
                Assert.That(plan.ExpectedResults.TransferSides, Is.EqualTo(6));
            });
        }

        [Test]
        [Explicit("Validates the selected real full-run plan and runtime hashes without provisioning a target.")]
        public void Selected_environment_plan_is_eligible_without_target_side_effects()
        {
            var inputs = FamilyProDisposableReleaseTests.ReleaseInputs.FromEnvironment();

            Assert.Multiple(() =>
            {
                Assert.That(
                    inputs.FullRunPlan.ApplicationBaselineId,
                    Does.StartWith("application-runtime-baseline-"));
                Assert.That(inputs.FullRunPlan.ExpectedResults.OrdinaryPayments, Is.EqualTo(18_572));
                Assert.That(inputs.FullRunPlan.ExpectedResults.MigrationAdjustments, Is.EqualTo(30));
                Assert.That(inputs.FullRunPlan.ExpectedResults.Transfers, Is.EqualTo(834));
                Assert.That(inputs.FullRunPlan.ExpectedResults.TransferSides, Is.EqualTo(1_668));
                Assert.That(inputs.FullRunPlan.ExpectedResults.UnresolvedRecords, Is.Zero);
            });
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HomeBudget.Accounting.ReleaseVerification.Tests
{
    internal sealed class FamilyProFullRunPlanFixture
    {
        private const string SourceHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string SchemaHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string BaseHash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        private const string PolicyHash = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
        private const string ManifestHash = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

        private readonly string _expectedPath;
        private readonly string _manifestPath;
        private readonly string _approvalPath;
        private readonly string _applicationPath;
        private readonly string _runtimePath;
        private readonly string _runtimeInventoryPath;

        private FamilyProFullRunPlanFixture(string directory)
        {
            _expectedPath = Path.Combine(directory, "expected.json");
            _manifestPath = Path.Combine(directory, "manifest.json");
            _approvalPath = Path.Combine(directory, "approval.json");
            _applicationPath = Path.Combine(directory, "application-attempt.json");
            _runtimePath = Path.Combine(directory, "runtime-attempt.json");
            _runtimeInventoryPath = Path.Combine(directory, "runtime-files.sha256.json");
            WriteManifest(18_572, 30, 834, 1_668);
            WriteApproval();
            WriteExpected(18_572, 30, 834, 1_668, 0);
            WriteRuntime();
            WriteApplication();
            RefreshSelection();
        }

        public FamilyProFullRunSelection Selection { get; private set; }

        public static FamilyProFullRunPlanFixture Create(string directory) => new(directory);

        public (string Path, string Sha256) AddRuntimeArtifact()
        {
            var path = Path.Combine(Path.GetDirectoryName(_runtimePath), "runtime", "runtime.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "retained runtime artifact");
            var sha256 = FamilyProFullRunPlan.ComputeSha256(path);
            WriteRuntimeArtifacts(path);
            WriteApplication();
            RefreshSelection();
            return (path, sha256);
        }

        public (
            string AccountingDll,
            string AccountingRoot,
            string GatewayDll,
            string GatewayRoot,
            string CliDll,
            string WorkerDll) AddRuntimeLayout()
        {
            var runtimeRoot = Path.Combine(Path.GetDirectoryName(_runtimePath), "runtime");
            var accountingRoot = Path.Combine(runtimeRoot, "accounting-api");
            var gatewayRoot = Path.Combine(runtimeRoot, "gateway");
            var cliRoot = Path.Combine(runtimeRoot, "cli");
            var testHostRoot = Path.Combine(runtimeRoot, "testhost");
            Directory.CreateDirectory(accountingRoot);
            Directory.CreateDirectory(gatewayRoot);
            Directory.CreateDirectory(cliRoot);
            Directory.CreateDirectory(testHostRoot);
            var accountingDll = WriteRuntimeFile(accountingRoot, "HomeBudget.Accounting.Api.dll");
            var gatewayDll = WriteRuntimeFile(gatewayRoot, "HomeBudget.Backend.Gateway.dll");
            var cliDll = WriteRuntimeFile(cliRoot, "FireBirdV25Client.dll");
            var workerDll = WriteRuntimeFile(testHostRoot, "HomeBudget.Accounting.Workers.OperationsConsumer.dll");
            WriteRuntimeArtifacts(accountingDll, gatewayDll, cliDll, workerDll);
            WriteApplication();
            RefreshSelection();
            return (accountingDll, accountingRoot, gatewayDll, gatewayRoot, cliDll, workerDll);
        }

        public string AddUninventoriedRuntimeFile()
        {
            var path = Path.Combine(Path.GetDirectoryName(_runtimePath), "runtime", "unexpected.txt");
            File.WriteAllText(path, "not inventoried");
            return path;
        }

        public void ChangeExpectedCounts(int ordinary, int adjustments, int transfers, int sides, int unresolved)
        {
            WriteExpected(ordinary, adjustments, transfers, sides, unresolved);
            RefreshSelection();
        }

        public void ChangeManifestAndExpectedCounts(int ordinary, int adjustments, int transfers, int sides)
        {
            WriteManifest(ordinary, adjustments, transfers, sides);
            WriteExpected(ordinary, adjustments, transfers, sides, 0);
            WriteApplication();
            RefreshSelection();
        }

        public void ApplyExpectedResultsDefect(string defect)
        {
            if (defect == "missing")
            {
                File.Delete(_expectedPath);
                return;
            }

            if (defect == "malformed")
            {
                File.WriteAllText(_expectedPath, "{");
            }
            else if (defect == "duplicate-unresolved")
            {
                var json = File.ReadAllText(_expectedPath).Replace(
                    "\"unresolvedRecords\":0",
                    "\"unresolvedRecords\":0,\"unresolvedRecords\":0",
                    StringComparison.Ordinal);
                File.WriteAllText(_expectedPath, json);
            }
            else
            {
                var root = JsonNode.Parse(File.ReadAllText(_expectedPath)).AsObject();
                var counts = root["approvedTargetCounts"].AsObject();
                if (defect == "unsupported-schema")
                {
                    root["manifestVersion"] = "family-pro-expected-results-v1";
                }
                else if (defect == "missing-count")
                {
                    counts.Remove("ordinaryPayments");
                }
                else if (defect == "missing-unresolved")
                {
                    counts.Remove("unresolvedRecords");
                }
                else
                {
                    counts["ordinaryPayments"] = -1;
                }

                File.WriteAllText(_expectedPath, root.ToJsonString());
            }

            RefreshSelection();
        }

        public void ApplyBindingDefect(string binding)
        {
            if (binding == "expected-digest")
            {
                Selection = Selection with { ExpectedResultsSha256 = new string('0', 64) };
                return;
            }

            if (binding == "application")
            {
                Selection = Selection with { ApplicationBaselineId = "final-candidate-20260920-40" };
                return;
            }

            if (binding == "harness")
            {
                Selection = Selection with { HarnessId = "predecessor" };
                return;
            }

            var root = JsonNode.Parse(File.ReadAllText(_expectedPath)).AsObject();
            if (binding == "source")
            {
                root["sourceEvidence"]["sourceHash"] = new string('1', 64);
            }
            else if (binding == "policy")
            {
                root["approvalProfileHash"] = new string('2', 64);
            }
            else
            {
                root["approvedManifestHash"] = new string('3', 64);
            }

            File.WriteAllText(_expectedPath, root.ToJsonString());
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            Selection = new(
                _expectedPath,
                File.Exists(_expectedPath) ? FamilyProFullRunPlan.ComputeSha256(_expectedPath) : new string('0', 64),
                _manifestPath,
                FamilyProFullRunPlan.ComputeSha256(_manifestPath),
                _approvalPath,
                FamilyProFullRunPlan.ComputeSha256(_approvalPath),
                _applicationPath,
                FamilyProFullRunPlan.ComputeSha256(_applicationPath),
                _runtimePath,
                FamilyProFullRunPlan.ComputeSha256(_runtimePath),
                "final-candidate-20260920-47",
                FamilyProFullRunPlan.SupportedHarnessId);
        }

        private void WriteExpected(int ordinary, int adjustments, int transfers, int sides, int unresolved)
        {
            var value = new
            {
                manifestVersion = FamilyProFullRunPlan.SupportedExpectedResultsVersion,
                sourceEvidence = new { sourceHash = SourceHash, schemaSignature = SchemaHash, baseManifestHash = BaseHash },
                approvalProfileHash = PolicyHash,
                approvedManifestHash = ManifestHash,
                sourceCounts = new { accounts = 14, categories = 108, payees = 582, rawOperations = 20_940, rawSplitDetails = 1_594, catType0Categories = 43, catType0SpecialOperations = 38 },
                approvedTargetCounts = new { accounts = 14, categories = 70, contractors = 582, ordinaryPayments = ordinary, splitPayments = 1_593, migrationAdjustments = adjustments, nonTransferEffects = ordinary + 1_593 + adjustments, transfers, transferSides = sides, excludedOperationRows = 12, excludedDetailRows = 1, unresolvedRecords = unresolved },
                adjustmentReconciliation = new { count = adjustments, signedTotal = -1_728.5275m, absoluteTotal = 11_428.1525m },
                accountReconciliation = new { totalAccounts = 14, balancedAccountsAtDeclaredScale = 14, accountsWithUnresolvedEffect = 0 }
            };
            File.WriteAllText(_expectedPath, JsonSerializer.Serialize(value));
        }

        private void WriteManifest(int ordinary, int adjustments, int transfers, int sides)
        {
            var value = new
            {
                manifestHash = ManifestHash,
                approvalProfileHash = PolicyHash,
                approvalBaseManifestHash = BaseHash,
                sourceEvidence = new { sourceHash = SourceHash, schemaSignature = SchemaHash },
                summary = new { canonicalOrdinaryOperations = ordinary, canonicalSplitLines = 1_593, migrationAdjustmentRows = adjustments, canonicalTransferPairs = transfers, canonicalTransferSides = sides, approvedExcludedOperationRows = 12, approvedExcludedDetailRows = 1 },
                invalidRecords = Array.Empty<object>(),
                specialOperations = Array.Empty<object>()
            };
            File.WriteAllText(_manifestPath, JsonSerializer.Serialize(value));
        }

        private void WriteApproval()
        {
            var value = new
            {
                profileHash = PolicyHash,
                sourceEvidence = new { sourceHash = SourceHash, schemaSignature = SchemaHash, canonicalManifestHash = BaseHash }
            };
            File.WriteAllText(_approvalPath, JsonSerializer.Serialize(value));
        }

        private void WriteApplication()
        {
            var fingerprints = new[]
            {
                new { path = _expectedPath, sha256 = FamilyProFullRunPlan.ComputeSha256(_expectedPath) },
                new { path = _manifestPath, sha256 = FamilyProFullRunPlan.ComputeSha256(_manifestPath) },
                new { path = _approvalPath, sha256 = FamilyProFullRunPlan.ComputeSha256(_approvalPath) },
                new { path = _runtimePath, sha256 = FamilyProFullRunPlan.ComputeSha256(_runtimePath) }
            };
            File.WriteAllText(
                _applicationPath,
                JsonSerializer.Serialize(new
                {
                    attemptId = "final-candidate-20260920-47",
                    build = new { artifacts = Array.Empty<object>() },
                    fingerprints
                }));
        }

        private void WriteRuntime() => WriteRuntimeArtifacts(Array.Empty<string>());

        private void WriteRuntimeArtifacts(params string[] artifactPaths)
        {
            var artifacts = artifactPaths
                .Select(path => new
                {
                    path = Path.GetRelativePath(Path.GetDirectoryName(_runtimePath), path),
                    sha256 = FamilyProFullRunPlan.ComputeSha256(path),
                    length = new FileInfo(path).Length
                })
                .ToArray();
            File.WriteAllText(_runtimeInventoryPath, JsonSerializer.Serialize(artifacts));
            File.WriteAllText(
                _runtimePath,
                JsonSerializer.Serialize(new
                {
                    attemptId = "final-candidate-20260920-40",
                    runtimeInventory = new
                    {
                        path = _runtimeInventoryPath,
                        sha256 = FamilyProFullRunPlan.ComputeSha256(_runtimeInventoryPath)
                    },
                    build = new { artifacts }
                }));
        }

        private static string WriteRuntimeFile(string directory, string name)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, name);
            return path;
        }
    }
}

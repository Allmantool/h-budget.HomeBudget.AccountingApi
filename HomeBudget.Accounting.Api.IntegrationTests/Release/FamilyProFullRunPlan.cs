using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace HomeBudget.Accounting.ReleaseVerification
{
    internal sealed class FamilyProFullRunPlan
    {
        public const string SupportedExpectedResultsVersion = "family-pro-expected-results-v2";
        public const string SupportedHarnessId = "family-pro-full-run-harness-v2";

        private readonly FrozenFile _expectedResults;
        private readonly FrozenFile _manifest;
        private readonly FrozenFile _approval;
        private readonly FrozenFile _applicationAttempt;
        private readonly FrozenFile _runtimeAttempt;
        private readonly string _runtimeRoot;
        private readonly IReadOnlyDictionary<string, RuntimeArtifact> _runtimeArtifacts;

        private FamilyProFullRunPlan(
            FamilyProFullRunSelection selection,
            FamilyProExpectedResults expectedResults,
            FrozenInputs inputs,
            string runtimeBaselineId,
            string runtimeRoot,
            IReadOnlyDictionary<string, RuntimeArtifact> runtimeArtifacts)
        {
            ApplicationBaselineId = selection.ApplicationBaselineId;
            ExpectedResults = expectedResults;
            _expectedResults = inputs.ExpectedResults;
            _manifest = inputs.Manifest;
            _approval = inputs.Approval;
            _applicationAttempt = inputs.ApplicationAttempt;
            _runtimeAttempt = inputs.RuntimeAttempt;
            RuntimeBaselineId = runtimeBaselineId;
            _runtimeRoot = runtimeRoot;
            _runtimeArtifacts = runtimeArtifacts;
        }

        public string ApplicationBaselineId { get; }

        public FamilyProExpectedResults ExpectedResults { get; }

        public string ExpectedResultsSha256 => _expectedResults.Sha256;

        public string RuntimeBaselineId { get; }

        public static FamilyProFullRunPlan Load(FamilyProFullRunSelection selection)
        {
            ArgumentNullException.ThrowIfNull(selection);
            ValidateExecutionIdentity(selection);

            var expectedFile = FrozenFile.Read(selection.ExpectedResultsPath, selection.ExpectedResultsSha256);
            var manifestFile = FrozenFile.Read(selection.ManifestPath, selection.ManifestSha256);
            var approvalFile = FrozenFile.Read(selection.ApprovalPath, selection.ApprovalSha256);
            var applicationFile = FrozenFile.Read(selection.ApplicationAttemptPath, selection.ApplicationAttemptSha256);
            var runtimeFile = FrozenFile.Read(selection.RuntimeAttemptPath, selection.RuntimeAttemptSha256);

            using var expectedDocument = ParseUniqueJson(expectedFile);
            using var manifestDocument = ParseUniqueJson(manifestFile);
            using var approvalDocument = ParseUniqueJson(approvalFile);
            using var applicationDocument = ParseUniqueJson(applicationFile);
            using var runtimeDocument = ParseUniqueJson(runtimeFile);
            var expected = ReadExpectedResults(expectedDocument.RootElement);
            ValidateManifestBindings(expectedDocument.RootElement, manifestDocument.RootElement, expected);
            ValidateApprovalBindings(expectedDocument.RootElement, approvalDocument.RootElement);
            ValidateApplicationBinding(
                selection,
                applicationDocument.RootElement,
                expectedFile,
                manifestFile,
                approvalFile,
                runtimeFile);
            var runtimeBaselineId = ReadString(runtimeDocument.RootElement, "attemptId");
            var runtimeInventory = ReadRuntimeInventory(runtimeDocument.RootElement, runtimeFile);

            return new(
                selection,
                expected,
                new(expectedFile, manifestFile, approvalFile, applicationFile, runtimeFile),
                runtimeBaselineId,
                runtimeInventory.RuntimeRoot,
                runtimeInventory.Artifacts);
        }

        public FamilyProMaterializedInputs Materialize(string directory)
        {
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            {
                throw new IOException($"Validated-input directory is not empty: {directory}");
            }

            Directory.CreateDirectory(directory);
            return new(
                _expectedResults.Materialize(directory, "family-pro-expected-results-v2.json"),
                _manifest.Materialize(directory, "approved-manifest.json"),
                _approval.Materialize(directory, "approval.json"),
                _applicationAttempt.Materialize(directory, "application-attempt.json"),
                _runtimeAttempt.Materialize(directory, "runtime-attempt.json"));
        }

        public void RequireRuntimeArtifact(string path, string expectedSha256)
        {
            var fullPath = Path.GetFullPath(path);
            if (!_runtimeArtifacts.TryGetValue(fullPath, out var retained))
            {
                throw new InvalidDataException(
                    $"Runtime artifact path is not bound by {RuntimeBaselineId}: {fullPath}.");
            }

            var actual = ComputeSha256(fullPath);
            if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Runtime artifact SHA-256 mismatch for {fullPath}: expected {expectedSha256}, actual {actual}.");
            }

            if (!string.Equals(retained.Sha256, actual, StringComparison.OrdinalIgnoreCase) ||
                new FileInfo(fullPath).Length != retained.Length)
            {
                throw new InvalidDataException(
                    $"Runtime artifact bytes are not bound by {RuntimeBaselineId}: {fullPath} ({actual}).");
            }
        }

        public void RequireRuntimeLayout(
            string accountingDll,
            string accountingContentRoot,
            string gatewayDll,
            string gatewayContentRoot,
            string cliDll,
            string workerDll)
        {
            RequireExactPath(accountingDll, "runtime", "accounting-api", "HomeBudget.Accounting.Api.dll");
            RequireExactDirectory(accountingContentRoot, "runtime", "accounting-api");
            RequireExactPath(gatewayDll, "runtime", "gateway", "HomeBudget.Backend.Gateway.dll");
            RequireExactDirectory(gatewayContentRoot, "runtime", "gateway");
            RequireExactPath(cliDll, "runtime", "cli", "FireBirdV25Client.dll");
            RequireExactPath(
                workerDll,
                "runtime",
                "testhost",
                "HomeBudget.Accounting.Workers.OperationsConsumer.dll");
            ValidateCompleteRuntimeInventory();
        }

        public static string ComputeSha256(string path) =>
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        private void RequireExactPath(string actual, params string[] relativeParts)
        {
            var expected = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(_runtimeRoot)
                    ?? throw new InvalidDataException("Runtime root has no parent directory."),
                Path.Combine(relativeParts)));
            if (!PathsEqual(actual, expected))
            {
                throw new InvalidDataException($"Runtime path mismatch: expected {expected}, actual {Path.GetFullPath(actual)}.");
            }
        }

        private void RequireExactDirectory(string actual, params string[] relativeParts)
        {
            var expected = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(_runtimeRoot)
                    ?? throw new InvalidDataException("Runtime root has no parent directory."),
                Path.Combine(relativeParts)));
            if (!PathsEqual(actual, expected))
            {
                throw new InvalidDataException($"Runtime content-root mismatch: expected {expected}, actual {Path.GetFullPath(actual)}.");
            }
        }

        private void ValidateCompleteRuntimeInventory()
        {
            var actualFiles = Directory.EnumerateFiles(_runtimeRoot, "*", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var expectedFiles = _runtimeArtifacts.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!actualFiles.SetEquals(expectedFiles))
            {
                var missing = expectedFiles.Except(actualFiles, StringComparer.OrdinalIgnoreCase).Count();
                var extra = actualFiles.Except(expectedFiles, StringComparer.OrdinalIgnoreCase).Count();
                throw new InvalidDataException(
                    $"Runtime inventory membership mismatch for {RuntimeBaselineId}: missing {missing}, extra {extra}.");
            }

            foreach (var artifact in _runtimeArtifacts.Values)
            {
                var file = new FileInfo(artifact.FullPath);
                if (file.Length != artifact.Length ||
                    !string.Equals(ComputeSha256(artifact.FullPath), artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Runtime inventory mismatch: {artifact.FullPath}.");
                }
            }
        }

        private static void ValidateExecutionIdentity(FamilyProFullRunSelection selection)
        {
            if (!string.Equals(selection.HarnessId, SupportedHarnessId, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsupported full-run harness identity: {selection.HarnessId}");
            }

            RequireValue(selection.ApplicationBaselineId, nameof(selection.ApplicationBaselineId));
        }

        private static JsonDocument ParseUniqueJson(FrozenFile file)
        {
            try
            {
                var document = JsonDocument.Parse(file.Bytes);
                EnsureUniqueProperties(document.RootElement, "$", file.Path);
                return document;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException($"JSON input is malformed: {file.Path}", exception);
            }
        }

        private static void EnsureUniqueProperties(JsonElement element, string location, string path)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw new InvalidDataException($"Duplicate JSON property at {location}.{property.Name}: {path}");
                    }

                    EnsureUniqueProperties(property.Value, $"{location}.{property.Name}", path);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    EnsureUniqueProperties(item, $"{location}[{index++}]", path);
                }
            }
        }

        private static FamilyProExpectedResults ReadExpectedResults(JsonElement root)
        {
            RequireEqual(ReadString(root, "manifestVersion"), SupportedExpectedResultsVersion, "expected-results schema");
            var counts = ReadObject(root, "approvedTargetCounts");
            var result = new FamilyProExpectedResults(
                ReadCount(counts, "accounts"),
                ReadCount(counts, "categories"),
                ReadCount(counts, "contractors"),
                ReadCount(counts, "ordinaryPayments"),
                ReadCount(counts, "splitPayments"),
                ReadCount(counts, "migrationAdjustments"),
                ReadCount(counts, "nonTransferEffects"),
                ReadCount(counts, "transfers"),
                ReadCount(counts, "transferSides"),
                ReadCount(counts, "excludedOperationRows"),
                ReadCount(counts, "excludedDetailRows"),
                ReadCount(counts, "unresolvedRecords"),
                ReadDecimal(ReadObject(root, "adjustmentReconciliation"), "signedTotal"),
                ReadDecimal(ReadObject(root, "adjustmentReconciliation"), "absoluteTotal"),
                ReadCount(ReadObject(root, "accountReconciliation"), "balancedAccountsAtDeclaredScale"));

            ValidateCountRelationships(root, result);
            return result;
        }

        private static void ValidateCountRelationships(JsonElement root, FamilyProExpectedResults expected)
        {
            if (expected.UnresolvedRecords != 0)
            {
                throw new InvalidDataException("Full-run eligibility requires zero unresolved records.");
            }

            RequireEqual(expected.TransferSides, checked(expected.Transfers * 2), "two-sided transfer count");
            RequireEqual(
                expected.NonTransferEffects,
                checked(expected.OrdinaryPayments + expected.SplitPayments + expected.MigrationAdjustments),
                "non-transfer effect count");
            var adjustments = ReadObject(root, "adjustmentReconciliation");
            RequireEqual(ReadCount(adjustments, "count"), expected.MigrationAdjustments, "adjustment reconciliation count");
            if (expected.AdjustmentAbsoluteTotal < 0)
            {
                throw new InvalidDataException("Adjustment absolute total cannot be negative.");
            }

            var accounts = ReadObject(root, "accountReconciliation");
            RequireEqual(ReadCount(accounts, "totalAccounts"), expected.Accounts, "account reconciliation total");
            RequireEqual(expected.BalancedAccounts, expected.Accounts, "balanced account count");
            RequireEqual(ReadCount(accounts, "accountsWithUnresolvedEffect"), 0, "accounts with unresolved effect");
            RequireEqual(ReadCount(ReadObject(root, "sourceCounts"), "accounts"), expected.Accounts, "source account count");
        }

        private static void ValidateManifestBindings(
            JsonElement expectedRoot,
            JsonElement manifest,
            FamilyProExpectedResults expected)
        {
            var expectedSource = ReadObject(expectedRoot, "sourceEvidence");
            var manifestSource = ReadObject(manifest, "sourceEvidence");
            RequireEqual(ReadHash(manifestSource, "sourceHash"), ReadHash(expectedSource, "sourceHash"), "source hash");
            RequireEqual(ReadHash(manifestSource, "schemaSignature"), ReadHash(expectedSource, "schemaSignature"), "schema signature");
            RequireEqual(ReadHash(manifest, "approvalBaseManifestHash"), ReadHash(expectedSource, "baseManifestHash"), "base manifest hash");
            RequireEqual(ReadHash(manifest, "approvalProfileHash"), ReadHash(expectedRoot, "approvalProfileHash"), "approval profile hash");
            RequireEqual(ReadHash(manifest, "manifestHash"), ReadHash(expectedRoot, "approvedManifestHash"), "approved manifest hash");

            var summary = ReadObject(manifest, "summary");
            RequireEqual(ReadCount(summary, "canonicalOrdinaryOperations"), expected.OrdinaryPayments, "ordinary payment count");
            RequireEqual(ReadCount(summary, "canonicalSplitLines"), expected.SplitPayments, "split payment count");
            RequireEqual(ReadCount(summary, "migrationAdjustmentRows"), expected.MigrationAdjustments, "migration adjustment count");
            RequireEqual(ReadCount(summary, "canonicalTransferPairs"), expected.Transfers, "transfer count");
            RequireEqual(ReadCount(summary, "canonicalTransferSides"), expected.TransferSides, "transfer-side count");
            RequireEqual(ReadCount(summary, "approvedExcludedOperationRows"), expected.ExcludedOperationRows, "excluded operation count");
            RequireEqual(ReadCount(summary, "approvedExcludedDetailRows"), expected.ExcludedDetailRows, "excluded detail count");
            RequireEqual(ReadArray(manifest, "invalidRecords").GetArrayLength(), 0, "manifest invalid-record count");
            RequireEqual(ReadArray(manifest, "specialOperations").GetArrayLength(), 0, "manifest special-operation count");
        }

        private static void ValidateApprovalBindings(JsonElement expectedRoot, JsonElement approval)
        {
            var expectedSource = ReadObject(expectedRoot, "sourceEvidence");
            var approvalSource = ReadObject(approval, "sourceEvidence");
            RequireEqual(ReadHash(approval, "profileHash"), ReadHash(expectedRoot, "approvalProfileHash"), "approval profile hash");
            RequireEqual(ReadHash(approvalSource, "sourceHash"), ReadHash(expectedSource, "sourceHash"), "approval source hash");
            RequireEqual(ReadHash(approvalSource, "schemaSignature"), ReadHash(expectedSource, "schemaSignature"), "approval schema signature");
            RequireEqual(ReadHash(approvalSource, "canonicalManifestHash"), ReadHash(expectedSource, "baseManifestHash"), "approval base manifest hash");
        }

        private static void ValidateApplicationBinding(
            FamilyProFullRunSelection selection,
            JsonElement application,
            params FrozenFile[] selectedFiles)
        {
            RequireEqual(ReadString(application, "attemptId"), selection.ApplicationBaselineId, "application baseline identity");
            var fingerprints = ReadArray(application, "fingerprints").EnumerateArray()
                .Select(ReadFingerprint)
                .ToArray();
            foreach (var selectedFile in selectedFiles)
            {
                var match = fingerprints.Any(fingerprint =>
                    PathsEqual(fingerprint.Path, selectedFile.Path) &&
                    string.Equals(fingerprint.Sha256, selectedFile.Sha256, StringComparison.OrdinalIgnoreCase));
                if (!match)
                {
                    throw new InvalidDataException($"Selected input is not bound by application baseline {selection.ApplicationBaselineId}: {selectedFile.Path}");
                }
            }
        }

        private static Fingerprint ReadFingerprint(JsonElement value) =>
            new(ReadString(value, "path"), ReadHash(value, "sha256"));

        private static RuntimeInventory ReadRuntimeInventory(JsonElement runtimeAttempt, FrozenFile runtimeAttemptFile)
        {
            var attemptRoot = Path.GetDirectoryName(runtimeAttemptFile.Path)
                ?? throw new InvalidDataException("Runtime attempt has no parent directory.");
            var runtimeRoot = Path.GetFullPath(Path.Combine(attemptRoot, "runtime"));
            var inventoryFingerprint = ReadFingerprint(ReadObject(runtimeAttempt, "runtimeInventory"));
            var expectedInventoryPath = Path.Combine(attemptRoot, "runtime-files.sha256.json");
            if (!PathsEqual(inventoryFingerprint.Path, expectedInventoryPath))
            {
                throw new InvalidDataException(
                    $"Runtime inventory path mismatch: expected {expectedInventoryPath}, actual {inventoryFingerprint.Path}.");
            }

            var inventoryFile = FrozenFile.Read(inventoryFingerprint.Path, inventoryFingerprint.Sha256);
            using var inventoryDocument = ParseUniqueJson(inventoryFile);
            if (inventoryDocument.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Runtime inventory root must be an array.");
            }

            var inventory = ReadRuntimeArtifactRecords(inventoryDocument.RootElement, attemptRoot, runtimeRoot);
            var embedded = ReadRuntimeArtifactRecords(
                ReadArray(ReadObject(runtimeAttempt, "build"), "artifacts"),
                attemptRoot,
                runtimeRoot);
            if (inventory.Count != embedded.Count || inventory.Any(item =>
                !embedded.TryGetValue(item.Key, out var other) || item.Value != other))
            {
                throw new InvalidDataException("Runtime attempt artifacts do not exactly match the frozen runtime inventory.");
            }

            return new(runtimeRoot, inventory);
        }

        private static IReadOnlyDictionary<string, RuntimeArtifact> ReadRuntimeArtifactRecords(
            JsonElement artifacts,
            string attemptRoot,
            string runtimeRoot)
        {
            var result = new Dictionary<string, RuntimeArtifact>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in artifacts.EnumerateArray())
            {
                var relativePath = ReadString(value, "path");
                if (Path.IsPathRooted(relativePath))
                {
                    throw new InvalidDataException($"Runtime inventory path must be relative: {relativePath}.");
                }

                var fullPath = Path.GetFullPath(Path.Combine(attemptRoot, relativePath));
                if (!IsDescendantOf(fullPath, runtimeRoot))
                {
                    throw new InvalidDataException($"Runtime inventory path escapes the sealed runtime root: {relativePath}.");
                }

                var artifact = new RuntimeArtifact(
                    relativePath.Replace('/', '\\'),
                    fullPath,
                    ReadHash(value, "sha256"),
                    ReadLength(value, "length"));
                if (!result.TryAdd(fullPath, artifact))
                {
                    throw new InvalidDataException($"Duplicate runtime inventory path: {relativePath}.");
                }
            }

            return result;
        }

        private static bool IsDescendantOf(string path, string directory)
        {
            var prefix = directory.EndsWith(Path.DirectorySeparatorChar)
                ? directory
                : directory + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathsEqual(string first, string second) =>
            string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

        private static JsonElement ReadObject(JsonElement parent, string name) => Read(parent, name, JsonValueKind.Object);

        private static JsonElement ReadArray(JsonElement parent, string name) => Read(parent, name, JsonValueKind.Array);

        private static JsonElement Read(JsonElement parent, string name, JsonValueKind kind)
        {
            if (!parent.TryGetProperty(name, out var value) || value.ValueKind != kind)
            {
                throw new InvalidDataException($"Required {kind} property is missing or invalid: {name}");
            }

            return value;
        }

        private static string ReadHash(JsonElement parent, string name)
        {
            var value = ReadString(parent, name);
            if (value.Length != 64 || !value.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException($"Required complete SHA-256 property is invalid: {name}");
            }

            return value.ToUpperInvariant();
        }

        private static string ReadString(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var value) ||
                value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(value.GetString()))
            {
                throw new InvalidDataException($"Required string property is missing or invalid: {name}");
            }

            return value.GetString();
        }

        private static int ReadCount(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var count) || count < 0)
            {
                throw new InvalidDataException($"Required non-negative integer count is missing or invalid: {name}");
            }

            return count;
        }

        private static long ReadLength(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt64(out var length) || length < 0)
            {
                throw new InvalidDataException($"Required non-negative file length is missing or invalid: {name}");
            }

            return length;
        }

        private static decimal ReadDecimal(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var value) || !value.TryGetDecimal(out var result))
            {
                throw new InvalidDataException($"Required decimal is missing or invalid: {name}");
            }

            return result;
        }

        private static void RequireValue(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"Required value is missing: {name}");
            }
        }

        private static void RequireEqual<T>(T actual, T expected, string description)
        {
            if (!EqualityComparer<T>.Default.Equals(actual, expected))
            {
                throw new InvalidDataException($"Mismatched {description}: expected {expected}, actual {actual}.");
            }
        }

        private sealed record Fingerprint(string Path, string Sha256);

        private sealed record RuntimeArtifact(string RelativePath, string FullPath, string Sha256, long Length);

        private sealed record RuntimeInventory(
            string RuntimeRoot,
            IReadOnlyDictionary<string, RuntimeArtifact> Artifacts);

        private sealed record FrozenInputs(
            FrozenFile ExpectedResults,
            FrozenFile Manifest,
            FrozenFile Approval,
            FrozenFile ApplicationAttempt,
            FrozenFile RuntimeAttempt);

        private sealed record FrozenFile(string Path, byte[] Bytes, string Sha256)
        {
            public static FrozenFile Read(string path, string expectedSha256)
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("Required full-run input does not exist.", path);
                }

                if (expectedSha256 is null || expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit))
                {
                    throw new InvalidDataException($"Required complete SHA-256 fingerprint is invalid: {path}");
                }

                var fullPath = System.IO.Path.GetFullPath(path);
                var bytes = File.ReadAllBytes(fullPath);
                var actual = Convert.ToHexString(SHA256.HashData(bytes));
                if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"SHA-256 mismatch for {fullPath}: expected {expectedSha256}, actual {actual}.");
                }

                return new(fullPath, bytes, actual);
            }

            public string Materialize(string directory, string name)
            {
                var destination = System.IO.Path.Combine(directory, name);
                using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                    stream.Write(Bytes);
                    stream.Flush(flushToDisk: true);
                }

                var actual = ComputeSha256(destination);
                if (!string.Equals(actual, Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException($"Materialized validated input hash mismatch: {destination}");
                }

                return destination;
            }
        }
    }
}

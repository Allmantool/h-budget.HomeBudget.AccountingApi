using System;
using System.IO;

namespace HomeBudget.Accounting.ReleaseVerification
{
    internal sealed record FamilyProFullRunSelection(
        string ExpectedResultsPath,
        string ExpectedResultsSha256,
        string ManifestPath,
        string ManifestSha256,
        string ApprovalPath,
        string ApprovalSha256,
        string ApplicationAttemptPath,
        string ApplicationAttemptSha256,
        string RuntimeAttemptPath,
        string RuntimeAttemptSha256,
        string ApplicationBaselineId,
        string HarnessId)
    {
        public static FamilyProFullRunSelection FromEnvironment() => new(
            RequiredFile("FAMILYPRO_RELEASE_EXPECTED_RESULTS"),
            RequiredValue("FAMILYPRO_RELEASE_EXPECTED_RESULTS_SHA256"),
            RequiredFile("FAMILYPRO_RELEASE_MANIFEST"),
            RequiredValue("FAMILYPRO_RELEASE_MANIFEST_SHA256"),
            RequiredFile("FAMILYPRO_RELEASE_APPROVAL"),
            RequiredValue("FAMILYPRO_RELEASE_APPROVAL_SHA256"),
            RequiredFile("FAMILYPRO_RELEASE_APPLICATION_ATTEMPT"),
            RequiredValue("FAMILYPRO_RELEASE_APPLICATION_ATTEMPT_SHA256"),
            RequiredFile("FAMILYPRO_RELEASE_RUNTIME_ATTEMPT"),
            RequiredValue("FAMILYPRO_RELEASE_RUNTIME_ATTEMPT_SHA256"),
            RequiredValue("FAMILYPRO_RELEASE_APPLICATION_BASELINE_ID"),
            RequiredValue("FAMILYPRO_RELEASE_HARNESS_ID"));

        private static string RequiredFile(string name)
        {
            var value = RequiredValue(name);
            return File.Exists(value)
                ? Path.GetFullPath(value)
                : throw new FileNotFoundException($"Environment variable {name} does not identify a file.", value);
        }

        private static string RequiredValue(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"Required environment variable {name} is not set.");
    }
}

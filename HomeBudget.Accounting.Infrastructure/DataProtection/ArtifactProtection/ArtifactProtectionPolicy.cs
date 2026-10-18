using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Artifacts
{
    public sealed record ArtifactProtectionPolicy(
        ArtifactProtectionProfile Profile,
        string EnvironmentId,
        string SourceHostIdentity,
        string SourceFailureDomainIdentity,
        int MaximumArtifactCount,
        long MaximumArtifactBytes)
    {
        internal static ArtifactProtectionPolicy Disposable(
            string environmentId,
            string sourceHostIdentity,
            string sourceFailureDomainIdentity) => new(
                ArtifactProtectionProfile.DisposableTest,
                environmentId,
                sourceHostIdentity,
                sourceFailureDomainIdentity,
                128,
                1024L * 1024L * 1024L);
    }
}

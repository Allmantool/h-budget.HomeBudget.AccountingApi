using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    internal sealed record CapturedArtifactEvidence(Guid RecoverySetId, string InventoryJson);

    internal sealed record IndependentCopyEvidence(string CopyIdentity, bool IsIndependent, bool IsImmutable);

    internal sealed record IntegrityVerificationEvidence(bool Succeeded, DateTimeOffset VerifiedUtc);
}

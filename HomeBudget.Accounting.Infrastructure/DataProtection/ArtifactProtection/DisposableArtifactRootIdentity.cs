namespace HomeBudget.Accounting.Infrastructure.DataProtection.Artifacts
{
    internal sealed record DisposableArtifactRootIdentity(
        string RootPath,
        string ExpectedParentPath,
        string RootId,
        string OwnershipToken);
}

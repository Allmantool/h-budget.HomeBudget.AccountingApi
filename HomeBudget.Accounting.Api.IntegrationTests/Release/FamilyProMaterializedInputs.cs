namespace HomeBudget.Accounting.ReleaseVerification
{
    internal sealed record FamilyProMaterializedInputs(
        string ExpectedResults,
        string Manifest,
        string Approval,
        string ApplicationAttempt,
        string RuntimeAttempt);
}

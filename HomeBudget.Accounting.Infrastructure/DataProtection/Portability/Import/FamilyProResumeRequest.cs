using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed record FamilyProResumeRequest(
        Guid OperationId,
        string ActorId,
        string ExternalJournalId,
        string SourceBackupReference,
        string ApprovedManifestReference,
        PortabilityImportRequest Import);
}

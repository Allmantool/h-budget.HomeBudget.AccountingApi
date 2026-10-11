using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed record FamilyProRemigrationRequest(
        Guid PriorOperationId,
        Guid NewOperationId,
        string ActorId,
        string PriorExternalJournalId,
        string NewExternalJournalId,
        string SourceBackupReference,
        string ApprovedManifestReference,
        PortabilityImportRequest Import);
}

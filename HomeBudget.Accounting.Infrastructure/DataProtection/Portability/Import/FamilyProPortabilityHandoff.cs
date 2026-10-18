using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed record FamilyProPortabilityHandoff(
        PortabilityImportExecutionRequest Execution,
        Guid PriorOperationId,
        string PriorExternalJournalId,
        string ExternalJournalId,
        string SourceBackupReference,
        string ApprovedManifestReference,
        IReadOnlyList<FamilyProLineageIdentity> Lineage,
        IReadOnlyList<FamilyProLegacyReference> LegacyReferences);
}

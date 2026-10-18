using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.SqlServer
{
    public interface ISqlServerProtectionProvider
    {
        Task<SqlServerProtectionProbe> ProbeAsync(CancellationToken cancellationToken);

        Task<SqlServerCapturePlan> PlanCaptureAsync(
            SqlServerBackupKind kind,
            Guid planId,
            CancellationToken cancellationToken);

        Task<SqlServerCaptureResult> ExecuteCaptureAsync(
            SqlServerCapturePlan plan,
            CancellationToken cancellationToken);

        void ValidateChain(IReadOnlyCollection<SqlServerBackupMetadata> backups);

        SqlServerPointInTimeRecoveryPlan PlanPointInTimeRecovery(
            DateTimeOffset requestedCutoffUtc,
            IReadOnlyCollection<SqlServerBackupMetadata> backups);
    }
}

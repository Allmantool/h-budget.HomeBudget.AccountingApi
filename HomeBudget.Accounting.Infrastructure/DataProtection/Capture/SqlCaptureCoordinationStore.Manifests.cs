using System;
using System.Threading;
using System.Threading.Tasks;

using Dapper;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    internal sealed partial class SqlCaptureCoordinationStore
    {
        public async Task StageManifestAsync(
            RecoveryManifestDraft draft,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                INSERT INTO dbo.RecoveryManifests
                    (RecoverySetId, OperationId, EnvironmentId, LedgerScope, SourceGenerationId,
                     SchemaVersion, CaptureStartedUtc, CommonCutoffUtc, KeyReference,
                     ManifestChecksum, ManifestJson, VerificationStatus)
                SELECT @RecoverySetId, @OperationId, @EnvironmentId, @LedgerScope, @SourceGenerationId,
                       N'home-ledger-recovery-set/v1', @CaptureStartedUtc, @CommonCutoffUtc, @KeyReference,
                       @ManifestChecksum, @ManifestJson, N'pending'
                  FROM dbo.MaintenanceEpochs
                 WHERE OperationId = @OperationId
                   AND State = N'closed';
                IF @@ROWCOUNT <> 1
                    THROW 51025, 'Maintenance epoch is no longer active.', 1;";
            await using var connection = CreateConnection();
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                ManifestParameters(draft),
                cancellationToken: cancellationToken));
        }

        public async Task UpdateStagedManifestAsync(
            RecoveryManifestDraft draft,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                UPDATE dbo.RecoveryManifests
                   SET ManifestChecksum = @ManifestChecksum, ManifestJson = @ManifestJson
                 WHERE RecoverySetId = @RecoverySetId
                   AND OperationId = @OperationId
                   AND VerificationStatus = N'pending'
                   AND PublishedUtc IS NULL;
                SELECT @@ROWCOUNT;";
            await using var connection = CreateConnection();
            var changed = await connection.QuerySingleAsync<int>(new CommandDefinition(
                sql,
                ManifestParameters(draft),
                cancellationToken: cancellationToken));
            EnsureSingleManifestChange(changed);
        }

        public async Task MarkManifestFailedAsync(
            Guid recoverySetId,
            string failureClassification,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                UPDATE dbo.RecoveryManifests
                   SET VerificationStatus = N'failed', FailureClassification = @FailureClassification
                 WHERE RecoverySetId = @RecoverySetId
                   AND VerificationStatus = N'pending'
                   AND PublishedUtc IS NULL;
                SELECT @@ROWCOUNT;";
            await using var connection = CreateConnection();
            var changed = await connection.QuerySingleAsync<int>(new CommandDefinition(
                sql,
                new { RecoverySetId = recoverySetId, FailureClassification = failureClassification },
                cancellationToken: cancellationToken));
            EnsureSingleManifestChange(changed);
        }

        public async Task PublishManifestAsync(
            Guid recoverySetId,
            IndependentCopyEvidence copy,
            IntegrityVerificationEvidence verification,
            CancellationToken cancellationToken)
        {
            if (!copy.IsIndependent || !copy.IsImmutable || !verification.Succeeded)
            {
                throw new InvalidOperationException(
                    "Manifest publication requires independent immutable copy and integrity verification.");
            }

            const string sql = @"
                UPDATE dbo.RecoveryManifests
                   SET VerificationStatus = N'verified',
                       ProtectedUtc = @VerifiedUtc,
                       PublishedUtc = @VerifiedUtc,
                       FailureClassification = NULL
                 WHERE RecoverySetId = @RecoverySetId
                   AND VerificationStatus = N'pending'
                   AND PublishedUtc IS NULL;
                SELECT @@ROWCOUNT;";
            await using var connection = CreateConnection();
            var changed = await connection.QuerySingleAsync<int>(new CommandDefinition(
                sql,
                new
                {
                    RecoverySetId = recoverySetId,
                    VerifiedUtc = verification.VerifiedUtc.UtcDateTime
                },
                cancellationToken: cancellationToken));
            EnsureSingleManifestChange(changed);
        }

        private static object ManifestParameters(RecoveryManifestDraft draft) => new
        {
            draft.RecoverySetId,
            draft.OperationId,
            draft.EnvironmentId,
            draft.LedgerScope,
            draft.SourceGenerationId,
            CaptureStartedUtc = draft.CaptureStartedUtc.UtcDateTime,
            CommonCutoffUtc = draft.CommonCutoffUtc.UtcDateTime,
            draft.KeyReference,
            draft.ManifestChecksum,
            draft.ManifestJson
        };

        private static void EnsureSingleManifestChange(int changed)
        {
            if (changed != 1)
            {
                throw new InvalidOperationException("Recovery manifest is not in the expected unpublished state.");
            }
        }
    }
}

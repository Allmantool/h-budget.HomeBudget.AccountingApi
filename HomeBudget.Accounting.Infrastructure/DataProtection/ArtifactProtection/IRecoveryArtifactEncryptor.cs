using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Artifacts
{
    public interface IRecoveryArtifactEncryptor
    {
        Task<ArtifactKeyCapability> ProbeAsync(string keyReference, CancellationToken cancellationToken);

        Task<ArtifactEncryptionEvidence> EncryptAsync(
            string keyReference,
            Stream plaintext,
            Stream protectedOutput,
            ReadOnlyMemory<byte> associatedData,
            CancellationToken cancellationToken);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HomeBudget.Accounting.Infrastructure.DataProtection.Artifacts;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.Artifacts
{
    internal sealed class TestRecoveryArtifactSource : IRecoveryArtifactCaptureSource
    {
        private readonly byte[] _content;
        private readonly bool _failAfterWrite;

        public TestRecoveryArtifactSource(byte[] content, bool failAfterWrite = false)
        {
            _content = content;
            _failAfterWrite = failAfterWrite;
        }

        public string LastPlaintextPath { get; private set; }

        public async Task<IReadOnlyCollection<PlaintextRecoveryArtifact>> CaptureAsync(
            RecoveryArtifactCaptureContext context,
            CancellationToken cancellationToken)
        {
            LastPlaintextPath = Path.Combine(context.PlaintextRoot, "accounting", "baseline.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(LastPlaintextPath)!);
            await File.WriteAllBytesAsync(LastPlaintextPath, _content, cancellationToken);
            if (_failAfterWrite)
            {
                throw new IOException("capture canary secret");
            }

            return new[]
            {
                new PlaintextRecoveryArtifact("accounting/baseline.bin", LastPlaintextPath, Array.Empty<string>())
            };
        }
    }
}

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HomeBudget.Accounting.Infrastructure.DataProtection.Artifacts;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.Artifacts
{
    internal sealed class TestStreamingArtifactEncryptor : IRecoveryArtifactEncryptor, IDisposable
    {
        private const int ChunkSize = 64 * 1024;
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

        public bool IsSeparatelyRecoverable { get; set; } = true;

        public int LargestRead { get; private set; }

        public Task<ArtifactKeyCapability> ProbeAsync(string keyReference, CancellationToken cancellationToken) =>
            Task.FromResult(new ArtifactKeyCapability(
                "disposable-key-provider",
                "1",
                keyReference,
                true,
                IsSeparatelyRecoverable,
                false));

        public async Task<ArtifactEncryptionEvidence> EncryptAsync(
            string keyReference,
            Stream plaintext,
            Stream protectedOutput,
            ReadOnlyMemory<byte> associatedData,
            CancellationToken cancellationToken)
        {
            var noncePrefix = RandomNumberGenerator.GetBytes(8);
            await protectedOutput.WriteAsync(noncePrefix, cancellationToken);
            var buffer = new byte[ChunkSize];
            var chunkIndex = 0;
            using var aes = new AesGcm(_key, 16);
            while (true)
            {
                var read = await plaintext.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                LargestRead = Math.Max(LargestRead, read);
                var ciphertext = new byte[read];
                var tag = new byte[16];
                var nonce = BuildNonce(noncePrefix, chunkIndex);
                var aad = BuildAssociatedData(associatedData.Span, chunkIndex, read);
                aes.Encrypt(nonce, buffer.AsSpan(0, read), ciphertext, tag, aad);
                var length = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(length, read);
                await protectedOutput.WriteAsync(length, cancellationToken);
                await protectedOutput.WriteAsync(ciphertext, cancellationToken);
                await protectedOutput.WriteAsync(tag, cancellationToken);
                chunkIndex++;
            }

            return new ArtifactEncryptionEvidence(
                "disposable-key-provider",
                "1",
                keyReference,
                "AES-256-GCM-chunked",
                "test-envelope/v1",
                true,
                true);
        }

        public void Dispose() => CryptographicOperations.ZeroMemory(_key);

        private static byte[] BuildNonce(byte[] prefix, int chunkIndex)
        {
            var nonce = new byte[12];
            prefix.CopyTo(nonce, 0);
            BinaryPrimitives.WriteInt32BigEndian(nonce.AsSpan(8), chunkIndex);
            return nonce;
        }

        private static byte[] BuildAssociatedData(ReadOnlySpan<byte> baseData, int chunkIndex, int length)
        {
            var result = new byte[baseData.Length + 8];
            baseData.CopyTo(result);
            BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(baseData.Length), chunkIndex);
            BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(baseData.Length + 4), length);
            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace HomeBudget.Accounting.Api.Tests.Portability
{
    internal sealed class PortabilityArchiveFactory
    {
        private static readonly DateTimeOffset EntryTimestamp =
            new(2025, 2, 1, 0, 0, 0, TimeSpan.Zero);

        private readonly PortabilityContractFixture _contract;

        public PortabilityArchiveFactory(PortabilityContractFixture contract)
        {
            _contract = contract;
        }

        public MemoryStream CreateSample(
            IReadOnlyDictionary<string, byte[]> replacements = null,
            Action<ZipArchive> addEntries = null)
        {
            var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                foreach (var path in _contract.SampleEntries())
                {
                    var bytes = replacements != null && replacements.TryGetValue(path, out var replacement)
                        ? replacement
                        : File.ReadAllBytes(_contract.SamplePath(path));
                    AddBytes(archive, path, bytes);
                }

                addEntries?.Invoke(archive);
            }

            stream.Position = 0;
            return stream;
        }

        public static ZipArchiveEntry AddText(
            ZipArchive archive,
            string path,
            string value,
            CompressionLevel compressionLevel = CompressionLevel.Optimal) =>
            AddBytes(archive, path, System.Text.Encoding.UTF8.GetBytes(value), compressionLevel);

        public static ZipArchiveEntry AddBytes(
            ZipArchive archive,
            string path,
            byte[] value,
            CompressionLevel compressionLevel = CompressionLevel.Optimal)
        {
            var entry = archive.CreateEntry(path, compressionLevel);
            entry.LastWriteTime = EntryTimestamp;
            using var output = entry.Open();
            output.Write(value);
            return entry;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb
{
    public sealed partial class EventStoreDbProtectionProvider
    {
        private static readonly string[] RequiredDatabaseCheckpoints =
        {
            "chaser.chk",
            "epoch.chk",
            "proposal.chk",
            "truncate.chk",
            "writer.chk"
        };

        private async Task<IReadOnlyCollection<EventStoreDbBackupFile>> BuildSourceInventoryAsync(
            EventStoreDbSourceProbe probe,
            CancellationToken cancellationToken)
        {
            EnsureSafeDirectory(_policy.Source.DatabasePath);
            EnsureSafeDirectory(_policy.Source.IndexPath);
            var databaseFiles = EnumerateDatabaseFiles();
            var indexFiles = EnumerateIndexFiles(probe.ExpectedIndexFiles);
            var files = databaseFiles.Concat(indexFiles)
                .OrderBy(file => GetCopyOrder(file.Role))
                .ThenBy(file => file.RelativePath, StringComparer.Ordinal)
                .ToArray();

            var inventory = new List<EventStoreDbBackupFile>(files.Length);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureSafeFile(file.FullPath);
                var information = new FileInfo(file.FullPath);
                var checksum = await ComputeSha256Async(file.FullPath, cancellationToken).ConfigureAwait(false);
                inventory.Add(new EventStoreDbBackupFile(
                    probe.Identity.NodeId,
                    file.RelativePath,
                    file.Role,
                    information.Length,
                    checksum,
                    GetDependencies(file.Role)));
            }

            return inventory;
        }

        private InventoryCandidate[] EnumerateDatabaseFiles()
        {
            var files = Directory.EnumerateFiles(_policy.Source.DatabasePath, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, EventStoreDbProtectionPolicy.PathComparer)
                .ToArray();
            var names = files.Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
            if (RequiredDatabaseCheckpoints.Any(checkpoint => !names.Contains(checkpoint)) ||
                !names.Any(IsChunkFileName))
            {
                throw Failure("database-inventory-incomplete", "The EventStoreDB database inventory is incomplete.");
            }

            if (names.Any(name => !RequiredDatabaseCheckpoints.Contains(name, StringComparer.Ordinal) &&
                !IsChunkFileName(name)))
            {
                throw Failure("database-file-unsupported", "The EventStoreDB database contains an unsupported file.");
            }

            return files.Select(path => new InventoryCandidate(
                    path,
                    "db/" + Path.GetFileName(path),
                    IsChunkFileName(Path.GetFileName(path))
                        ? EventStoreDbFileRole.Chunk
                        : EventStoreDbFileRole.DatabaseCheckpoint))
                .ToArray();
        }

        private InventoryCandidate[] EnumerateIndexFiles(
            IReadOnlyCollection<string> expectedIndexFiles)
        {
            if (expectedIndexFiles is null)
            {
                throw Failure("index-inventory-incomplete", "The EventStoreDB index inventory is incomplete.");
            }

            var actual = Directory.EnumerateFiles(_policy.Source.IndexPath, "*", SearchOption.AllDirectories)
                .Select(path => new
                {
                    FullPath = path,
                    RelativePath = NormalizeRelativePath(Path.GetRelativePath(_policy.Source.IndexPath, path))
                })
                .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                .ToArray();
            var expected = expectedIndexFiles.Select(NormalizeRelativePath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            if (!actual.Select(file => file.RelativePath).SequenceEqual(expected, StringComparer.Ordinal) ||
                !expected.Contains("indexmap", StringComparer.Ordinal))
            {
                throw Failure("index-inventory-incomplete", "The EventStoreDB index inventory is incomplete.");
            }

            var candidates = actual.Select(file => new InventoryCandidate(
                    file.FullPath,
                    "index/" + file.RelativePath,
                    ClassifyIndexFile(file.RelativePath)))
                .ToArray();
            if (!candidates.Any(candidate => candidate.Role == EventStoreDbFileRole.IndexTable))
            {
                throw Failure("index-inventory-incomplete", "The EventStoreDB index inventory is incomplete.");
            }

            var existenceCheckpoint = expected.Contains(
                "stream-existence/streamExistenceFilter.chk",
                StringComparer.Ordinal);
            var existenceData = expected.Contains(
                "stream-existence/streamExistenceFilter.dat",
                StringComparer.Ordinal);
            if (existenceCheckpoint != existenceData)
            {
                throw Failure("index-inventory-incomplete", "The EventStoreDB stream-existence inventory is incomplete.");
            }

            return candidates;
        }

        private static EventStoreDbFileRole ClassifyIndexFile(string relativePath)
        {
            var fileName = Path.GetFileName(relativePath);
            if (fileName.EndsWith(".chk", StringComparison.Ordinal))
            {
                return EventStoreDbFileRole.IndexCheckpoint;
            }

            if (relativePath == "indexmap")
            {
                return EventStoreDbFileRole.IndexMap;
            }

            if (Guid.TryParseExact(fileName, "D", out _))
            {
                return EventStoreDbFileRole.IndexTable;
            }

            var tableName = Path.GetFileNameWithoutExtension(fileName);
            if ((fileName.EndsWith(".bloomfilter", StringComparison.Ordinal) &&
                    Guid.TryParseExact(tableName, "D", out _)) ||
                relativePath == "stream-existence/streamExistenceFilter.dat" ||
                relativePath == "scavenging/scavenging.db")
            {
                return EventStoreDbFileRole.IndexAuxiliary;
            }

            throw Failure("index-file-unsupported", "The EventStoreDB index contains an unsupported file.");
        }

        private static bool IsChunkFileName(string fileName)
        {
            if (!fileName.StartsWith("chunk-", StringComparison.Ordinal))
            {
                return false;
            }

            var parts = fileName[6..].Split('.', StringSplitOptions.None);
            return parts.Length == 2 &&
                parts.All(part => part.Length == 6 &&
                    int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _));
        }

        private static string NormalizeRelativePath(string path)
        {
            var normalized = path.Replace('\\', '/');
            if (Path.IsPathRooted(path) || normalized.Split('/').Any(segment => segment is "" or "." or ".."))
            {
                throw Failure("inventory-path-invalid", "The EventStoreDB inventory contains an invalid path.");
            }

            return normalized;
        }

        private static string[] GetDependencies(EventStoreDbFileRole role) => role switch
        {
            EventStoreDbFileRole.IndexTable or EventStoreDbFileRole.IndexAuxiliary => new[] { "index/indexmap" },
            EventStoreDbFileRole.Chunk => new[] { "db/chaser.chk", "db/writer.chk" },
            _ => Array.Empty<string>()
        };

        private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash);
        }

        private static void EnsureSafeDirectory(string path)
        {
            if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw Failure("source-path-unsafe", "An EventStoreDB source directory is missing or unsafe.");
            }

            var pending = new Stack<string>();
            pending.Push(path);
            while (pending.Count > 0)
            {
                foreach (var directory in Directory.EnumerateDirectories(
                    pending.Pop(),
                    "*",
                    SearchOption.TopDirectoryOnly))
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw Failure("source-path-unsafe", "An EventStoreDB source directory is missing or unsafe.");
                    }

                    pending.Push(directory);
                }
            }
        }

        private static void EnsureSafeFile(string path)
        {
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw Failure("source-file-unsafe", "An EventStoreDB source file is missing or unsafe.");
            }
        }

        private sealed record InventoryCandidate(
            string FullPath,
            string RelativePath,
            EventStoreDbFileRole Role);
    }
}

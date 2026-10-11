using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace HomeBudget.Accounting.Api.Tests.Portability.Import
{
    internal sealed class DisposablePortabilityImportPackage : IDisposable
    {
        public DisposablePortabilityImportPackage()
        {
            RootPath = Directory.CreateTempSubdirectory("home-ledger-import-preflight-").FullName;
            var source = new PortabilityContractFixture().SampleRoot;
            Copy(source, RootPath);
        }

        public string RootPath { get; }

        public string PackageSha256 => ComputePackageSha256(RootPath);

        public void MutateManifest(Action<JsonObject> mutation)
        {
            var path = Path.Combine(RootPath, "manifest.json");
            var manifest = JsonNode.Parse(File.ReadAllText(path)).AsObject();
            mutation(manifest);
            File.WriteAllText(path, manifest.ToJsonString(), new UTF8Encoding(false));
        }

        public void MutateJsonLine(string path, int index, Action<JsonObject> mutation, bool updateInventory = true)
        {
            var fullPath = Path.Combine(RootPath, path);
            var lines = File.ReadAllLines(fullPath).ToList();
            var value = JsonNode.Parse(lines[index]).AsObject();
            mutation(value);
            lines[index] = value.ToJsonString();
            File.WriteAllText(fullPath, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
            if (updateInventory)
            {
                UpdateInventory(path, lines.Count);
            }
        }

        public void AppendJsonLine(string path, string line)
        {
            var fullPath = Path.Combine(RootPath, path);
            File.AppendAllText(fullPath, line + "\n", new UTF8Encoding(false));
            UpdateInventory(path, File.ReadLines(fullPath).Count(value => !string.IsNullOrWhiteSpace(value)));
        }

        public void Dispose()
        {
            var root = Path.GetFullPath(RootPath);
            var expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (Directory.GetParent(root)?.FullName.TrimEnd(Path.DirectorySeparatorChar) != expectedParent
                || !Path.GetFileName(root).StartsWith("home-ledger-import-preflight-", StringComparison.Ordinal)
                || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Refusing to remove an unverified import test package.");
            }

            Directory.Delete(root, true);
        }

        private void UpdateInventory(string path, int recordCount)
        {
            var manifestPath = Path.Combine(RootPath, "manifest.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath)).AsObject();
            var entry = manifest["files"].AsArray()
                .Select(value => value.AsObject())
                .Single(value => value["path"].GetValue<string>() == path);
            var bytes = File.ReadAllBytes(Path.Combine(RootPath, path));
            entry["byteSize"] = bytes.LongLength;
            entry["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
            entry["recordCount"] = recordCount;
            File.WriteAllText(manifestPath, manifest.ToJsonString(), new UTF8Encoding(false));
        }

        private static string ComputePackageSha256(string root)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .OrderBy(value => Path.GetRelativePath(root, value), StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                hash.AppendData(Encoding.UTF8.GetBytes(relative));
                hash.AppendData([0]);
                hash.AppendData(File.ReadAllBytes(path));
                hash.AppendData([0]);
            }

            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        private static void Copy(string source, string destination)
        {
            foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
            }

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
            }
        }
    }
}

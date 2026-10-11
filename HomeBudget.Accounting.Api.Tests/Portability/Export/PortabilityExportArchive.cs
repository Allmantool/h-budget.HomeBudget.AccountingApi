using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace HomeBudget.Accounting.Api.Tests.Portability.Export
{
    internal sealed class PortabilityExportArchive : IDisposable
    {
        private readonly FileStream _stream;
        private readonly ZipArchive _archive;

        public PortabilityExportArchive(string path)
        {
            _stream = File.OpenRead(path);
            _archive = new ZipArchive(_stream, ZipArchiveMode.Read, false);
        }

        public IReadOnlyList<string> Entries => _archive.Entries
            .Select(entry => entry.FullName)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        public JsonElement ReadJson(string path)
        {
            using var document = JsonDocument.Parse(ReadText(path));
            return document.RootElement.Clone();
        }

        public IReadOnlyList<JsonElement> ReadJsonLines(string path) => ReadText(path)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                return document.RootElement.Clone();
            })
            .ToArray();

        public string ReadAllText() => string.Join(
            "\n",
            _archive.Entries.Select(entry => ReadText(entry.FullName)));

        public string ReadText(string path)
        {
            var entry = _archive.GetEntry(path)
                ?? throw new InvalidDataException($"Expected export entry '{path}'.");
            using var input = entry.Open();
            using var reader = new StreamReader(input);
            return reader.ReadToEnd();
        }

        public void Dispose()
        {
            _archive.Dispose();
            _stream.Dispose();
        }
    }
}

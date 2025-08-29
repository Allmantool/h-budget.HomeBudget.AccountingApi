using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Microsoft.Extensions.Logging;

namespace HomeBudget.Test.Core.Models
{
    internal class ExistingTestContainer : IContainer
    {
        public string Id { get; }
        public string Name { get; }

        public ExistingTestContainer(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public DateTime CreatedTime => DateTime.MinValue;
        public DateTime StartedTime => DateTime.MinValue;
        public DateTime StoppedTime => DateTime.MinValue;
        public DateTime PausedTime => DateTime.MinValue;
        public DateTime UnpausedTime => DateTime.MinValue;

        public ILogger Logger => null!;
        public string IpAddress => "127.0.0.1";
        public string MacAddress => "";
        public string Hostname => "localhost";
        public IImage Image => null!;
        public TestcontainersStates State => TestcontainersStates.Running;
        public TestcontainersHealthStatus Health => TestcontainersHealthStatus.Healthy;
        public long HealthCheckFailingStreak => 0;

        public event EventHandler Creating;
        public event EventHandler Starting;
        public event EventHandler Stopping;
        public event EventHandler Pausing;
        public event EventHandler Unpausing;
        public event EventHandler Created;
        public event EventHandler Started;
        public event EventHandler Stopped;
        public event EventHandler Paused;
        public event EventHandler Unpaused;

        public ushort GetMappedPublicPort(int containerPort) => (ushort)containerPort;
        public ushort GetMappedPublicPort(string containerPort) => ushort.Parse(containerPort);

        // Fixed: Return 0 (invalid port) since no default port mapping exists
        public ushort GetMappedPublicPort() => 0;

        // Fixed: Return empty dictionary since no port mappings are tracked
        public IReadOnlyDictionary<ushort, ushort> GetMappedPublicPorts() =>
            new Dictionary<ushort, ushort>();

        public Task<long> GetExitCodeAsync(CancellationToken ct = default) => Task.FromResult(0L);

        public Task<(string Stdout, string Stderr)> GetLogsAsync(
            DateTime since = default,
            DateTime until = default,
            bool timestampsEnabled = true,
            CancellationToken ct = default) => Task.FromResult(("", ""));

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task UnpauseAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task CopyAsync(byte[] fileContent, string filePath, UnixFileModes fileMode = default, CancellationToken ct = default) => Task.CompletedTask;
        public Task CopyAsync(string source, string target, UnixFileModes fileMode = default, CancellationToken ct = default) => Task.CompletedTask;
        public Task CopyAsync(DirectoryInfo source, string target, UnixFileModes fileMode = default, CancellationToken ct = default) => Task.CompletedTask;
        public Task CopyAsync(FileInfo source, string target, UnixFileModes fileMode = default, CancellationToken ct = default) => Task.CompletedTask;

        public Task<byte[]> ReadFileAsync(string filePath, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<byte>());

        public Task<ExecResult> ExecAsync(IList<string> command, CancellationToken ct = default) =>
            Task.FromResult(default(ExecResult));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
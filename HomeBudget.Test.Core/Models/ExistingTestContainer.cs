using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeBudget.Test.Core.Models
{
    /// <summary>
    /// Represents an already existing and externally managed container.
    /// Provides a no-op implementation of IContainer to integrate with
    /// DotNet.Testcontainers-based test frameworks without creating or managing real containers.
    /// </summary>
    internal sealed class ExistingTestContainer : IContainer
    {
        private readonly DateTime _created = DateTime.UtcNow;
        private readonly Dictionary<ushort, ushort> _portMappings;
        private readonly ILogger _logger;
        private bool _disposed;

        public string Id { get; }
        public string Name { get; }

        public ExistingTestContainer(
            string id,
            string name,
            IDictionary<ushort, ushort> portMappings = null,
            ILogger logger = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _portMappings = new Dictionary<ushort, ushort>(portMappings ?? new Dictionary<ushort, ushort>());
            _logger = logger ?? NullLogger.Instance;

            _logger.LogDebug("Created ExistingTestContainer wrapper for container {ContainerName} (ID: {ContainerId})", name, id);
        }

        public DateTime CreatedTime => _created;
        public DateTime StartedTime => _created;
        public DateTime StoppedTime => DateTime.MinValue;
        public DateTime PausedTime => DateTime.MinValue;
        public DateTime UnpausedTime => DateTime.MinValue;

        public ILogger Logger => _logger;
        public string IpAddress => "127.0.0.1";
        public string MacAddress => string.Empty;
        public string Hostname => "localhost";
        public IImage Image { get; } = new DummyImage();

        public TestcontainersStates State => TestcontainersStates.Running;
        public TestcontainersHealthStatus Health => TestcontainersHealthStatus.Healthy;
        public long HealthCheckFailingStreak => 0;

        public event EventHandler? Creating;
        public event EventHandler? Starting;
        public event EventHandler? Stopping;
        public event EventHandler? Pausing;
        public event EventHandler? Unpausing;
        public event EventHandler? Created;
        public event EventHandler? Started;
        public event EventHandler? Stopped;
        public event EventHandler? Paused;
        public event EventHandler? Unpaused;

        private void OnEvent(EventHandler? handler, string eventName)
        {
            if (_disposed)
            {
                _logger.LogWarning("Attempted to raise event {EventName} on disposed container", eventName);
                return;
            }

            _logger.LogDebug("Raising event {EventName} for container {ContainerName}", eventName, Name);
            handler?.Invoke(this, EventArgs.Empty);
        }

        public ushort GetMappedPublicPort(int containerPort)
        {
            return containerPort < 0 || containerPort > ushort.MaxValue
                ? throw new ArgumentOutOfRangeException(nameof(containerPort), $"Port must be between 0 and {ushort.MaxValue}")
                : _portMappings.TryGetValue((ushort)containerPort, out var publicPort)
                ? publicPort
                : (ushort)containerPort;
        }

        public ushort GetMappedPublicPort(string containerPort)
        {
            if (string.IsNullOrWhiteSpace(containerPort))
            {
                throw new ArgumentException("Port cannot be null or empty", nameof(containerPort));
            }

            if (ushort.TryParse(containerPort, out var port))
            {
                return GetMappedPublicPort(port);
            }

            throw new ArgumentException($"Invalid port format: {containerPort}", nameof(containerPort));
        }

        public ushort GetMappedPublicPort() => 0;

        public IReadOnlyDictionary<ushort, ushort> GetMappedPublicPorts() =>
            new Dictionary<ushort, ushort>(_portMappings);

        public Task StartAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            OnEvent(Starting, nameof(Starting));
            OnEvent(Started, nameof(Started));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            OnEvent(Stopping, nameof(Stopping));
            OnEvent(Stopped, nameof(Stopped));
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            OnEvent(Pausing, nameof(Pausing));
            OnEvent(Paused, nameof(Paused));
            return Task.CompletedTask;
        }

        public Task UnpauseAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            OnEvent(Unpausing, nameof(Unpausing));
            OnEvent(Unpaused, nameof(Unpaused));
            return Task.CompletedTask;
        }

        // Copy operations (no-op)
        public Task CopyAsync(byte[] fileContent, string filePath, UnixFileModes fileMode = default, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogDebug("No-op copy operation for file {FilePath}", filePath);
            return Task.CompletedTask;
        }

        public Task CopyAsync(string source, string target, UnixFileModes fileMode = default, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogDebug("No-op copy operation from {Source} to {Target}", source, target);
            return Task.CompletedTask;
        }

        public Task CopyAsync(DirectoryInfo source, string target, UnixFileModes fileMode = default, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogDebug("No-op copy operation from directory {Source} to {Target}", source.FullName, target);
            return Task.CompletedTask;
        }

        public Task CopyAsync(FileInfo source, string target, UnixFileModes fileMode = default, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogDebug("No-op copy operation from file {Source} to {Target}", source.FullName, target);
            return Task.CompletedTask;
        }

        // UID/GID overloads (no-op)
        public Task CopyAsync(
            byte[] fileContent,
            string filePath,
            uint uid = 0,
            uint gid = 0,
            UnixFileModes fileMode = UnixFileModes.OtherRead | UnixFileModes.GroupRead | UnixFileModes.UserWrite | UnixFileModes.UserRead,
            CancellationToken ct = default) => CopyAsync(fileContent, filePath, fileMode, ct);

        public Task CopyAsync(
            string source,
            string target,
            uint uid = 0,
            uint gid = 0,
            UnixFileModes fileMode = UnixFileModes.OtherRead | UnixFileModes.GroupRead | UnixFileModes.UserWrite | UnixFileModes.UserRead,
            CancellationToken ct = default) => CopyAsync(source, target, fileMode, ct);

        public Task CopyAsync(
            DirectoryInfo source,
            string target,
            uint uid = 0,
            uint gid = 0,
            UnixFileModes fileMode = UnixFileModes.OtherRead | UnixFileModes.GroupRead | UnixFileModes.UserWrite | UnixFileModes.UserRead,
            CancellationToken ct = default) => CopyAsync(source, target, fileMode, ct);

        public Task CopyAsync(
            FileInfo source,
            string target,
            uint uid = 0,
            uint gid = 0,
            UnixFileModes fileMode = UnixFileModes.OtherRead | UnixFileModes.GroupRead | UnixFileModes.UserWrite | UnixFileModes.UserRead,
            CancellationToken ct = default) => CopyAsync(source, target, fileMode, ct);

        public Task<byte[]> ReadFileAsync(string filePath, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogDebug("No-op read file operation for {FilePath}", filePath);
            return Task.FromResult(Array.Empty<byte>());
        }

        public Task<ExecResult> ExecAsync(IList<string> command, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogDebug("No-op exec operation for command: {Command}", string.Join(" ", command));

            var result = new ExecResult(
                string.Empty,
                string.Empty,
                0);

            return Task.FromResult(result);
        }

        public Task<long> GetExitCodeAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(0L);
        }

        public Task<(string Stdout, string Stderr)> GetLogsAsync(
            DateTime since = default,
            DateTime until = default,
            bool timestampsEnabled = true,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogDebug("No-op get logs operation");
            return Task.FromResult(("", ""));
        }

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                OnEvent(Stopped, nameof(Stopped));
                _disposed = true;
                _logger.LogDebug("Disposed ExistingTestContainer wrapper for container {ContainerName}", Name);
            }

            return ValueTask.CompletedTask;
        }
    }
}
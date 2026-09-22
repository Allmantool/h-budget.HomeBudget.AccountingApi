using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Api.IntegrationTests.Migration
{
    internal sealed class CapturedTestProcess : IAsyncDisposable
    {
        private const int DefaultDiagnosticTailByteLimit = 64 * 1024;
        private static readonly TimeSpan DefaultCleanupGrace = TimeSpan.FromSeconds(30);

        private readonly SemaphoreSlim _lifecycle = new(1, 1);
        private readonly Process _process;
        private readonly CapturedStream _standardOutput;
        private readonly CapturedStream _standardError;
        private readonly Task _outputCompletion;
        private bool _isDisposed;

        private CapturedTestProcess(
            Process process,
            CapturedStream standardOutput,
            CapturedStream standardError,
            Task outputCompletion)
        {
            _process = process;
            _standardOutput = standardOutput;
            _standardError = standardError;
            _outputCompletion = outputCompletion;
        }

        public bool HasExited => _process.HasExited;

        public int ExitCode => _process.ExitCode;

        public bool WasForceTerminated { get; private set; }

        public bool OutputCompleted => _outputCompletion.IsCompletedSuccessfully;

        public string StandardOutputTail => _standardOutput.Tail;

        public string StandardErrorTail => _standardError.Tail;

        public static CapturedTestProcess Start(
            ProcessStartInfo start,
            string standardOutputPath,
            string standardErrorPath,
            int diagnosticTailByteLimit = DefaultDiagnosticTailByteLimit)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(diagnosticTailByteLimit);
            EnsureRedirected(start);

            var standardOutput = new CapturedStream(standardOutputPath, diagnosticTailByteLimit);
            var standardError = new CapturedStream(standardErrorPath, diagnosticTailByteLimit);
            var process = new Process { StartInfo = start };
            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException($"Could not start process '{start.FileName}'.");
                }

                var outputPump = standardOutput.PumpAsync(process.StandardOutput.BaseStream);
                var errorPump = standardError.PumpAsync(process.StandardError.BaseStream);
                return new CapturedTestProcess(
                    process,
                    standardOutput,
                    standardError,
                    Task.WhenAll(outputPump, errorPump));
            }
            catch
            {
                process.Dispose();
                standardOutput.Dispose();
                standardError.Dispose();
                throw;
            }
        }

        public async Task WaitForExitAsync(CancellationToken token)
        {
            try
            {
                await WaitForExitAndOutputAsync(token);
            }
            catch (OperationCanceledException)
            {
                await StopAsync();
                throw;
            }
        }

        public Task WaitForExitAsync(TimeSpan deadline, CancellationToken token)
        {
            if (deadline <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(deadline), deadline, "The process deadline must be positive.");
            }

            return WaitForExitWithDeadlineAsync(deadline, token);
        }

        private async Task WaitForExitWithDeadlineAsync(TimeSpan deadline, CancellationToken token)
        {
            using var deadlineCancellation = new CancellationTokenSource(deadline);
            using var combinedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                token,
                deadlineCancellation.Token);
            try
            {
                await WaitForExitAndOutputAsync(combinedCancellation.Token);
            }
            catch (OperationCanceledException) when (deadlineCancellation.IsCancellationRequested && !token.IsCancellationRequested)
            {
                await StopAsync();
                throw new TimeoutException($"Process '{_process.StartInfo.FileName}' exceeded its {deadline} deadline.");
            }
            catch (OperationCanceledException)
            {
                await StopAsync();
                throw;
            }
        }

        public Task StopAsync() => StopAsync(DefaultCleanupGrace);

        internal Task StopAsync(TimeSpan cleanupGrace)
        {
            if (cleanupGrace <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cleanupGrace),
                    cleanupGrace,
                    "The process cleanup grace period must be positive.");
            }

            return StopCoreAsync(cleanupGrace);
        }

        private async Task StopCoreAsync(TimeSpan cleanupGrace)
        {
            using var cleanupCancellation = new CancellationTokenSource(cleanupGrace);
            var lifecycleAcquired = false;
            try
            {
                await _lifecycle.WaitAsync(cleanupCancellation.Token);
                lifecycleAcquired = true;
                if (!_process.HasExited)
                {
                    try
                    {
                        _process.Kill(entireProcessTree: true);
                        WasForceTerminated = true;
                    }
                    catch (InvalidOperationException) when (_process.HasExited)
                    {
                    }
                }

                await _process.WaitForExitAsync(cleanupCancellation.Token);
                await _outputCompletion.WaitAsync(cleanupCancellation.Token);
            }
            catch (OperationCanceledException) when (cleanupCancellation.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Process '{_process.StartInfo.FileName}' did not terminate and drain output within the {cleanupGrace} cleanup grace period.");
            }
            finally
            {
                if (lifecycleAcquired)
                {
                    _lifecycle.Release();
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_isDisposed)
            {
                return;
            }

            await StopAsync();
            _process.Dispose();
            _standardOutput.Dispose();
            _standardError.Dispose();
            _lifecycle.Dispose();
            _isDisposed = true;
        }

        private static void EnsureRedirected(ProcessStartInfo start)
        {
            if (!start.RedirectStandardOutput || !start.RedirectStandardError || start.UseShellExecute)
            {
                throw new ArgumentException(
                    "Captured processes require redirected stdout/stderr and UseShellExecute=false.",
                    nameof(start));
            }
        }

        private async Task WaitForExitAndOutputAsync(CancellationToken token)
        {
            await _process.WaitForExitAsync(token);
            await _outputCompletion;
        }

        private sealed class CapturedStream : IDisposable
        {
            private const int BufferSize = 16 * 1024;

            private readonly FileStream _file;
            private readonly ByteTail _tail;
            private bool _isDisposed;

            public CapturedStream(string path, int tailByteLimit)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)
                    ?? throw new InvalidOperationException($"Capture path has no parent directory: {path}"));
                _file = new FileStream(
                    path,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.ReadWrite,
                    BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                _tail = new ByteTail(tailByteLimit);
            }

            public string Tail => _tail.Text;

            public async Task PumpAsync(Stream source)
            {
                try
                {
                    var buffer = new byte[BufferSize];
                    int bytesRead;
                    while ((bytesRead = await source.ReadAsync(buffer.AsMemory())) > 0)
                    {
                        await _file.WriteAsync(buffer.AsMemory(0, bytesRead));
                        await _file.FlushAsync();
                        _tail.Append(buffer.AsSpan(0, bytesRead));
                    }
                }
                finally
                {
                    Dispose();
                }
            }

            public void Dispose()
            {
                if (_isDisposed)
                {
                    return;
                }

                _file.Dispose();
                _isDisposed = true;
            }
        }

        private sealed class ByteTail
        {
            private readonly object _sync = new();
            private readonly byte[] _buffer;
            private int _count;
            private int _start;

            public ByteTail(int capacity)
            {
                _buffer = new byte[capacity];
            }

            public string Text
            {
                get
                {
                    lock (_sync)
                    {
                        var value = new byte[_count];
                        var first = Math.Min(_count, _buffer.Length - _start);
                        _buffer.AsSpan(_start, first).CopyTo(value);
                        _buffer.AsSpan(0, _count - first).CopyTo(value.AsSpan(first));
                        return Encoding.UTF8.GetString(value);
                    }
                }
            }

            public void Append(ReadOnlySpan<byte> value)
            {
                lock (_sync)
                {
                    if (value.Length >= _buffer.Length)
                    {
                        value[^_buffer.Length..].CopyTo(_buffer);
                        _start = 0;
                        _count = _buffer.Length;
                        return;
                    }

                    var overflow = Math.Max(0, _count + value.Length - _buffer.Length);
                    _start = (_start + overflow) % _buffer.Length;
                    _count -= overflow;
                    var end = (_start + _count) % _buffer.Length;
                    var first = Math.Min(value.Length, _buffer.Length - end);
                    value[..first].CopyTo(_buffer.AsSpan(end));
                    value[first..].CopyTo(_buffer);
                    _count += value.Length;
                }
            }
        }
    }
}

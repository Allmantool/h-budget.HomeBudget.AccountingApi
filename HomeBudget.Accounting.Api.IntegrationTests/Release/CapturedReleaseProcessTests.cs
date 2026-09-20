using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace HomeBudget.Accounting.Api.IntegrationTests.Release
{
    [TestFixture]
    [NonParallelizable]
    internal sealed class CapturedReleaseProcessTests
    {
        private const int TailByteLimit = 32 * 1024;

        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "process-capture", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [Test]
        public async Task WaitForExitAsync_DrainsLargeConcurrentStreamsWithBoundedTails()
        {
            const int streamBytes = 8 * 1024 * 1024;
            await using var process = Start("dual-large", streamBytes.ToString(CultureInfo.InvariantCulture));

            await process.WaitForExitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(process.ExitCode, Is.Zero);
                Assert.That(process.OutputCompleted, Is.True);
                Assert.That(new FileInfo(OutputPath).Length, Is.EqualTo(streamBytes));
                Assert.That(new FileInfo(ErrorPath).Length, Is.EqualTo(streamBytes));
                Assert.That(process.StandardOutputTail.Length, Is.LessThanOrEqualTo(TailByteLimit));
                Assert.That(process.StandardErrorTail.Length, Is.LessThanOrEqualTo(TailByteLimit));
            });
        }

        [Test]
        public async Task WaitForExitAsync_PreservesFinalFragmentsWithoutNewlines()
        {
            await using var process = Start("final-fragment");

            await process.WaitForExitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(OutputPath), Is.EqualTo("stdout-final"));
                Assert.That(File.ReadAllText(ErrorPath), Is.EqualTo("stderr-final"));
                Assert.That(process.StandardOutputTail, Is.EqualTo("stdout-final"));
                Assert.That(process.StandardErrorTail, Is.EqualTo("stderr-final"));
            });
        }

        [Test]
        public async Task WaitForExitAsync_ReturnsNonzeroExitAfterSilentChildCompletes()
        {
            await using var process = Start("silent", "100", "23");

            await process.WaitForExitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(process.ExitCode, Is.EqualTo(23));
                Assert.That(process.OutputCompleted, Is.True);
                Assert.That(new FileInfo(OutputPath).Length, Is.Zero);
                Assert.That(new FileInfo(ErrorPath).Length, Is.Zero);
            });
        }

        [Test]
        public void WaitForExitAsync_DeadlineTerminatesProcessTreeAndDrainsShutdownOutput()
        {
            var process = Start("shutdown-output");

            Assert.That(
                async () => await process.WaitForExitAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None),
                Throws.TypeOf<TimeoutException>());

            Assert.Multiple(() =>
            {
                Assert.That(process.HasExited, Is.True);
                Assert.That(process.OutputCompleted, Is.True);
                Assert.That(File.ReadAllText(OutputPath), Does.Contain("started"));
                Assert.That(File.ReadAllText(ErrorPath), Does.Contain("shutdown-"));
            });
            Assert.That(async () => await process.DisposeAsync(), Throws.Nothing);
        }

        [Test]
        public async Task WaitForExitAsync_CancellationTerminatesDescendant()
        {
            await using var process = Start("parent-with-child");
            var descendantId = await ReadDescendantIdAsync();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

            await Assert.ThatAsync(
                () => process.WaitForExitAsync(TimeSpan.FromSeconds(30), cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());

            Assert.Multiple(() =>
            {
                Assert.That(process.HasExited, Is.True);
                Assert.That(process.OutputCompleted, Is.True);
                Assert.That(IsProcessRunning(descendantId), Is.False);
            });
        }

        [Test]
        public async Task ConcurrentCapture_CompletesPayloadThatDeadlocksLegacySequentialRead()
        {
            const int streamBytes = 4 * 1024 * 1024;
            using var legacy = Process.Start(CreateStartInfo("legacy-deadlock", streamBytes.ToString(CultureInfo.InvariantCulture)))
                ?? throw new InvalidOperationException("Could not start legacy capture probe.");
            var legacyRead = Task.Run(() =>
            {
                _ = legacy.StandardOutput.ReadToEnd();
                _ = legacy.StandardError.ReadToEnd();
            });

            var legacyCompleted = await Task.WhenAny(legacyRead, Task.Delay(500)) == legacyRead;
            if (!legacy.HasExited)
            {
                legacy.Kill(entireProcessTree: true);
                await legacy.WaitForExitAsync();
            }

            await using var process = Start("legacy-deadlock", streamBytes.ToString(CultureInfo.InvariantCulture));
            await process.WaitForExitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(legacyCompleted, Is.False, "The regression fixture no longer reproduces the sequential pipe deadlock.");
                Assert.That(process.ExitCode, Is.Zero);
                Assert.That(new FileInfo(ErrorPath).Length, Is.EqualTo(streamBytes));
                Assert.That(File.ReadAllText(OutputPath), Is.EqualTo("stdout-after-stderr"));
            });
        }

        [Test]
        public async Task ProgressMonitor_AllowsSlowProcessWhileMarkerAdvances()
        {
            await using var process = Start("silent", "350", "0");
            long marker = 0;

            await ReleaseProcessMonitor.WaitForExitAsync(
                process,
                () => Interlocked.Increment(ref marker),
                TimeSpan.FromMilliseconds(150),
                TimeSpan.FromMilliseconds(25),
                CancellationToken.None);

            Assert.That(process.ExitCode, Is.Zero);
        }

        [Test]
        public void ProgressMonitor_StopsProcessAfterMeaningfulProgressStalls()
        {
            var process = Start("shutdown-output");

            Assert.That(
                async () => await ReleaseProcessMonitor.WaitForExitAsync(
                    process,
                    () => 7,
                    TimeSpan.FromMilliseconds(150),
                    TimeSpan.FromMilliseconds(25),
                    CancellationToken.None),
                Throws.TypeOf<TimeoutException>().With.Message.Contains("no durable progress"));

            Assert.Multiple(() =>
            {
                Assert.That(process.HasExited, Is.True);
                Assert.That(process.OutputCompleted, Is.True);
            });
            Assert.That(async () => await process.DisposeAsync(), Throws.Nothing);
        }

        [Test]
        public async Task StopAsync_RejectsAnUnboundedCleanupGrace()
        {
            await using var process = Start("silent", "25", "0");

            await Assert.ThatAsync(
                () => process.StopAsync(TimeSpan.Zero),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        private CapturedReleaseProcess Start(params string[] arguments)
        {
            return CapturedReleaseProcess.Start(CreateStartInfo(arguments), OutputPath, ErrorPath, TailByteLimit);
        }

        private static ProcessStartInfo CreateStartInfo(params string[] arguments)
        {
            var executable = Path.Combine(AppContext.BaseDirectory, ExecutableName());
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            return start;
        }

        private async Task<int> ReadDescendantIdAsync()
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(OutputPath))
                {
                    var line = ReadSharedLines(OutputPath)
                        .FirstOrDefault(value => value.StartsWith("DESCENDANT:", StringComparison.Ordinal));
                    if (line is not null)
                    {
                        return int.Parse(line.AsSpan("DESCENDANT:".Length), CultureInfo.InvariantCulture);
                    }
                }

                await Task.Delay(25);
            }

            throw new TimeoutException("The process probe did not report its descendant process ID.");
        }

        private static bool IsProcessRunning(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static string[] ReadSharedLines(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string ExecutableName() => OperatingSystem.IsWindows()
            ? "HomeBudget.Release.ProcessProbe.exe"
            : "HomeBudget.Release.ProcessProbe";

        private string OutputPath => Path.Combine(_directory, "stdout.log");

        private string ErrorPath => Path.Combine(_directory, "stderr.log");
    }
}

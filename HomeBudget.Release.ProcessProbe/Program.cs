using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Release.ProcessProbe
{
    internal static class Program
    {
        private const int ExitCodeArgumentIndex = 2;
        private const int ShutdownOutputDelayMilliseconds = 10;

        private static async Task<int> Main(string[] args)
        {
            return args[0] switch
            {
                "dual-large" => await WriteDualLargeAsync(ParseInt(args[1])),
                "final-fragment" => WriteFinalFragments(),
                "silent" => await WaitAsync(ParseInt(args[1]), ParseInt(args[ExitCodeArgumentIndex])),
                "exit" => ParseInt(args[1]),
                "legacy-deadlock" => WriteLegacyDeadlockPayload(ParseInt(args[1])),
                "shutdown-output" => await WriteThenWaitAsync(),
                "parent-with-child" => await RunParentWithChildAsync(),
                "wait-child" => await WaitForeverAsync(),
                _ => throw new ArgumentOutOfRangeException(nameof(args), args[0], "Unknown probe mode.")
            };
        }

        private static async Task<int> WriteDualLargeAsync(int bytes)
        {
            var standardOutput = WriteStandardOutputBytesAsync(bytes);
            var standardError = WriteStandardErrorBytesAsync(bytes);
            await Task.WhenAll(standardOutput, standardError);
            return 0;
        }

        private static async Task WriteStandardOutputBytesAsync(int bytes)
        {
            using var stream = Console.OpenStandardOutput();
            await WriteBytesAsync(stream, (byte)'O', bytes);
        }

        private static async Task WriteStandardErrorBytesAsync(int bytes)
        {
            using var stream = Console.OpenStandardError();
            await WriteBytesAsync(stream, (byte)'E', bytes);
        }

        private static int WriteFinalFragments()
        {
            Console.Out.Write("stdout-final");
            Console.Error.Write("stderr-final");
            return 0;
        }

        private static async Task<int> WaitAsync(int milliseconds, int exitCode)
        {
            await Task.Delay(milliseconds);
            return exitCode;
        }

        private static int WriteLegacyDeadlockPayload(int bytes)
        {
            using var errorStream = Console.OpenStandardError();
            WriteBytes(errorStream, (byte)'E', bytes);
            Console.Out.Write("stdout-after-stderr");
            return 0;
        }

        private static async Task<int> WriteThenWaitAsync()
        {
            await Console.Out.WriteLineAsync("started");
            await Console.Out.FlushAsync();
            for (var index = 0; ; index++)
            {
                await Console.Error.WriteLineAsync($"shutdown-{index.ToString(CultureInfo.InvariantCulture)}");
                await Console.Error.FlushAsync();
                await Task.Delay(ShutdownOutputDelayMilliseconds);
            }
        }

        private static async Task<int> RunParentWithChildAsync()
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("The probe executable path is unavailable.");
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false
            };
            start.ArgumentList.Add("wait-child");
            using var child = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start the descendant probe.");
            await Console.Out.WriteLineAsync($"DESCENDANT:{child.Id.ToString(CultureInfo.InvariantCulture)}");
            await Console.Out.FlushAsync();
            await WaitForeverAsync();
            return 0;
        }

        private static async Task<int> WaitForeverAsync()
        {
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return 0;
        }

        private static async Task WriteBytesAsync(Stream stream, byte value, int bytes)
        {
            var buffer = CreateBuffer(value);
            var remaining = bytes;
            while (remaining > 0)
            {
                var count = Math.Min(buffer.Length, remaining);
                await stream.WriteAsync(buffer.AsMemory(0, count));
                remaining -= count;
            }

            await stream.FlushAsync();
        }

        private static void WriteBytes(Stream stream, byte value, int bytes)
        {
            var buffer = CreateBuffer(value);
            var remaining = bytes;
            while (remaining > 0)
            {
                var count = Math.Min(buffer.Length, remaining);
                stream.Write(buffer, 0, count);
                stream.Flush();
                remaining -= count;
            }
        }

        private static byte[] CreateBuffer(byte value)
        {
            var buffer = new byte[16 * 1024];
            Array.Fill(buffer, value);
            return buffer;
        }

        private static int ParseInt(string value) => int.Parse(value, CultureInfo.InvariantCulture);
    }
}

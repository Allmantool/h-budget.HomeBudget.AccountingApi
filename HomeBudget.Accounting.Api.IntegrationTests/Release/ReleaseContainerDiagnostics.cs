using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using DotNet.Testcontainers.Containers;

namespace HomeBudget.Accounting.Api.IntegrationTests.Release
{
    internal static class ReleaseContainerDiagnostics
    {
        private static readonly TimeSpan CommandDeadline = TimeSpan.FromSeconds(30);
        private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

        public static async Task CaptureAsync(
            TestContainersService containers,
            string sessionDirectory,
            CancellationToken token)
        {
            if (containers is null)
            {
                return;
            }

            var diagnosticsDirectory = Path.Combine(sessionDirectory, "container-diagnostics");
            Directory.CreateDirectory(diagnosticsDirectory);
            var summaries = new List<ContainerSummary>();
            foreach (var item in Containers(containers))
            {
                if (item.Container is null)
                {
                    continue;
                }

                try
                {
                    var containerId = item.Container.Id;
                    summaries.Add(Summary(item.Service, item.Container));
                    await RunDockerAsync(
                        ["inspect", "--format", "{{json .State}}", containerId],
                        Path.Combine(diagnosticsDirectory, $"{item.Service}.inspect-state.json"),
                        Path.Combine(diagnosticsDirectory, $"{item.Service}.inspect-state.stderr.log"),
                        token);
                    await RunDockerAsync(
                        ["inspect", "--format", "{{.RestartCount}}", containerId],
                        Path.Combine(diagnosticsDirectory, $"{item.Service}.restart-count.txt"),
                        Path.Combine(diagnosticsDirectory, $"{item.Service}.restart-count.stderr.log"),
                        token);
                    await RunDockerAsync(
                        ["logs", "--tail", "500", "--timestamps", containerId],
                        Path.Combine(diagnosticsDirectory, $"{item.Service}.logs.stdout.log"),
                        Path.Combine(diagnosticsDirectory, $"{item.Service}.logs.stderr.log"),
                        token);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    await File.AppendAllTextAsync(
                        Path.Combine(diagnosticsDirectory, "capture-errors.log"),
                        $"{item.Service}: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}",
                        token);
                }
            }

            await File.WriteAllTextAsync(
                Path.Combine(diagnosticsDirectory, "containers.json"),
                JsonSerializer.Serialize(summaries, IndentedJson),
                token);
        }

        private static IEnumerable<(string Service, IContainer Container)> Containers(TestContainersService containers)
        {
            yield return ("mongo", containers.MongoDbContainer);
            yield return ("sql", containers.MsSqlDbContainer);
            yield return ("eventstore", containers.EventSourceDbContainer);
            yield return ("kafka", containers.KafkaContainer);
            yield return ("zookeeper", containers.ZkContainer);
            yield return ("kafka-ui", containers.KafkaUIContainer);
        }

        private static ContainerSummary Summary(string service, IContainer container) => new(
            service,
            container.Id,
            container.Name,
            container.State.ToString(),
            container.Health.ToString(),
            container.CreatedTime,
            container.StartedTime,
            container.StoppedTime);

        private static async Task RunDockerAsync(
            IReadOnlyCollection<string> arguments,
            string outputPath,
            string errorPath,
            CancellationToken token)
        {
            var start = new ProcessStartInfo("docker")
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

            try
            {
                await using var process = CapturedReleaseProcess.Start(start, outputPath, errorPath);
                await process.WaitForExitAsync(CommandDeadline, token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await File.AppendAllTextAsync(errorPath, $"{Environment.NewLine}{exception.GetType().Name}: {exception.Message}", token);
            }
        }

        private sealed record ContainerSummary(
            string Service,
            string Id,
            string Name,
            string State,
            string Health,
            DateTime CreatedTime,
            DateTime StartedTime,
            DateTime StoppedTime);
    }
}

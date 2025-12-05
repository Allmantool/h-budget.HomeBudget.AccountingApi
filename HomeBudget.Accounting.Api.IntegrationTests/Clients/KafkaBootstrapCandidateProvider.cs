using System;
using System.Collections.Generic;
using System.Linq;

using Testcontainers.Kafka;

namespace HomeBudget.Accounting.Api.IntegrationTests.Clients
{
    internal sealed class BootstrapCandidateProvider
    {
        private readonly KafkaContainer _container;

        private static readonly string[] StaticCandidates =
        {
            "test-kafka:9092", "kafka:9092",
            "localhost:39092", "127.0.0.1:39092",
            "localhost:9092",  "127.0.0.1:9092",
            "localhost:9093",  "127.0.0.1:9093",
            "localhost:9094",  "127.0.0.1:9094",
            "localhost:29092", "127.0.0.1:29092",
            "localhost:29093", "127.0.0.1:29093",
        };

        private static readonly int[] PortsToMap = { 9092, 29092, 29093 };

        public BootstrapCandidateProvider(KafkaContainer container)
        {
            _container = container;
        }

        public IEnumerable<string> BuildCandidates()
        {
            return Enumerable.Empty<string>()
                .Concat(BuildDynamicCandidates())
                .Concat(BuildMappedStaticCandidates())
                .Distinct();
        }

        private IEnumerable<string> BuildDynamicCandidates()
        {
            var list = new List<string>();

            // Add testcontainers' own bootstrap address if present
            var bootstrap = _container.GetBootstrapAddress();
            if (!string.IsNullOrWhiteSpace(bootstrap))
            {
                list.Add(bootstrap);
                list.Add(bootstrap.Replace("plaintext://", "", StringComparison.OrdinalIgnoreCase));
            }

            var mappedPorts = ResolveMappedPorts(PortsToMap);

            foreach (var kv in mappedPorts)
            {
                var mappedPort = kv.Value;
                list.Add($"{_container.IpAddress}:{mappedPort}");
                list.Add($"{_container.Hostname}:{mappedPort}");
            }

            return list;
        }

        private IEnumerable<string> BuildMappedStaticCandidates()
        {
            var mappedPorts = ResolveMappedPorts(PortsToMap);

            foreach (var candidate in StaticCandidates)
            {
                var (host, port) = SplitHostPort(candidate);

                if (port != -1 && mappedPorts.TryGetValue(port, out var mapped))
                {
                    yield return $"{host}:{mapped}";
                }
                else
                {
                    yield return candidate;
                }
            }
        }

        private IReadOnlyDictionary<int, int> ResolveMappedPorts(IEnumerable<int> ports)
        {
            var result = new Dictionary<int, int>();

            foreach (var port in ports)
            {
                try
                {
                    var mapped = _container.GetMappedPublicPort(port);
                    result[port] = mapped;
                }
                catch
                {
                }
            }

            return result;
        }

        private static (string host, int port) SplitHostPort(string candidate)
        {
            var parts = candidate.Split(':');

            if (parts.Length == 2 && int.TryParse(parts[1], out var port))
            {
                return (parts[0], port);
            }

            return (candidate, -1);
        }
    }
}

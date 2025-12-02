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

            var bootstrap = _container.GetBootstrapAddress();
            if (!string.IsNullOrWhiteSpace(bootstrap))
            {
                list.Add(bootstrap);
                list.Add(bootstrap.Replace("plaintext://", "", StringComparison.OrdinalIgnoreCase));
            }

            foreach (var port in PortsToMap)
            {
                var mapped = _container.GetMappedPublicPort(port);
                list.Add($"{_container.IpAddress}:{mapped}");
                list.Add($"{_container.Hostname}:{mapped}");
            }

            return list;
        }

        private IEnumerable<string> BuildMappedStaticCandidates()
        {
            var map = PortsToMap.ToDictionary(
                port => port,
                _container.GetMappedPublicPort
            );

            return StaticCandidates.Select(c =>
            {
                var (host, port) = SplitHostPort(c);
                return map.TryGetValue(port, out var mappedPort)
                    ? $"{host}:{mappedPort}"
                    : c;
            });
        }

        private static (string host, int port) SplitHostPort(string candidate)
        {
            var parts = candidate.Split(':');
            return parts.Length == 2 && int.TryParse(parts[1], out var port)
                ? (parts[0], port)
                : (candidate, -1);
        }
    }
}

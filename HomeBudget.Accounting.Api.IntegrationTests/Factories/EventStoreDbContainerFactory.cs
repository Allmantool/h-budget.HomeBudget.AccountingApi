using System;

using DotNet.Testcontainers.Builders;
using Testcontainers.EventStoreDb;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;

namespace HomeBudget.Accounting.Api.IntegrationTests.Factories
{
    internal static class EventStoreDbContainerFactory
    {
        public static EventStoreDbContainer Build()
        {
            return new EventStoreDbBuilder()
                    .WithImage("eventstore/eventstore:24.10.4-jammy")
                    .WithName($"{nameof(TestContainersService)}-event-store-db-container-{Guid.NewGuid()}")
                    .WithHostname("test-eventsource-db-host")
                    .WithPortBinding(2113, 2113)
                    .WithPortBinding(2117, 2117)
                    .WithEnvironment("EVENTSTORE_CLUSTER_SIZE", "1")
                    .WithEnvironment("EVENTSTORE_INSECURE", "true")
                    .WithEnvironment("EVENTSTORE_RUN_PROJECTIONS", "System")
                    .WithEnvironment("EVENTSTORE_START_STANDARD_PROJECTIONS", "true")
                    .WithEnvironment("EVENTSTORE_ENABLE_ATOM_PUB_OVER_HTTP", "true")
                    .WithEnvironment("EVENTSTORE_COMMIT_TIMEOUT_MS", "60000")
                    .WithEnvironment("EVENTSTORE_WRITE_TIMEOUT_MS", "60000")
                    .WithEnvironment("EVENTSTORE_DISABLE_HTTP_CACHING", "true")
                    .WithEnvironment("EVENTSTORE_MAX_MEM_TABLE_SIZE", "100000")
                    .WithEnvironment("EVENTSTORE_CACHED_CHUNKS", "512")
                    .WithEnvironment("EVENTSTORE_MIN_FLUSH_DELAY_MS", "2000")
                    .WithEnvironment("EVENTSTORE_STATS_PERIOD_SEC", "60")
                    .WithEnvironment("EVENTSTORE_SKIP_DB_VERIFY", "true")
                    .WithEnvironment("EVENTSTORE_HTTP_PORT", "2113")
                    .WithEnvironment("EVENTSTORE_EXT_IP", "0.0.0.0")
                    .WithEnvironment("EVENTSTORE_INT_IP", "0.0.0.0")
                    .WithAutoRemove(true)
                    .WithCleanUp(true)
                    .WithWaitStrategy(
                        Wait.ForUnixContainer()
                            .AddCustomWaitStrategy(
                                new CustomWaitStrategy(TimeSpan.FromMinutes(BaseTestContainerOptions.StopTimeoutInMinutes))
                            ))
                    .WithCreateParameterModifier(config =>
                    {
                        config.HostConfig.Memory = BaseTestContainerOptions.Memory;
                        config.StopTimeout = TimeSpan.FromMinutes(BaseTestContainerOptions.StopTimeoutInMinutes);
                    })
                    .Build();
        }
    }
}

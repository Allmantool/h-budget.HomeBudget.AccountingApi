using System;

using DotNet.Testcontainers.Builders;
using Testcontainers.MongoDb;

namespace HomeBudget.Accounting.Api.IntegrationTests.Factories
{
    internal static class MongoDbContainerFactory
    {
        public static MongoDbContainer Build(long containerMaxMemoryAllocation)
        {
            return new MongoDbBuilder()
                    .WithImage("mongo:7.0.5-rc0-jammy")
                    .WithName($"{nameof(TestContainersService)}-mongo-db-container-{Guid.NewGuid()}")
                    .WithHostname("test-mongo-db-host")
                    .WithPortBinding(55821, 28117)
                    .WithEnvironment("MONGO_INITDB_ROOT_USERNAME", "mongo")
                    .WithEnvironment("MONGO_INITDB_ROOT_PASSWORD", "mongo")
                    .WithAutoRemove(true)
                    .WithCleanUp(true)
                    .WithWaitStrategy(
                        Wait.ForUnixContainer()
                            .AddCustomWaitStrategy(
                                new CustomWaitStrategy(TimeSpan.FromMinutes(5))
                            ))
                    .WithCreateParameterModifier(config =>
                    {
                        config.HostConfig.Memory = containerMaxMemoryAllocation;
                        config.StopTimeout = TimeSpan.FromMinutes(5);
                    })
                    .Build();
        }
    }
}

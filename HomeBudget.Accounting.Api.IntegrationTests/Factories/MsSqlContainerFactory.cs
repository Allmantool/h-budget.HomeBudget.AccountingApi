using System;

using DotNet.Testcontainers.Builders;
using Testcontainers.MsSql;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;

namespace HomeBudget.Tests.Infrastructure.Containers
{
    internal static class MsSqlContainerFactory
    {
        public static MsSqlContainer Build()
        {
            return new MsSqlBuilder()
                  .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                  .WithName("integration-sql-server")
                  .WithPassword("Passw0rd!")
                  .WithEnvironment("ACCEPT_EULA", "Y")
                  .WithEnvironment("SA_PASSWORD", "Passw0rd!")
                  .WithPortBinding(1433, true)
                  .WithWaitStrategy(
                      Wait.ForUnixContainer()
                          .AddCustomWaitStrategy(
                              new CustomWaitStrategy(TimeSpan.FromMinutes(BaseTestContainerOptions.StopTimeoutInMinutes))
                      )
                 )
                .WithCreateParameterModifier(config =>
                {
                    config.HostConfig.Memory = BaseTestContainerOptions.Memory1Gb;
                    config.StopTimeout = TimeSpan.FromMinutes(BaseTestContainerOptions.StopTimeoutInMinutes);
                })
                .Build();
        }
    }
}
using System;
using System.Diagnostics;
using System.Threading.Tasks;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Components.Operations.Tests.Constants;

namespace HomeBudget.Accounting.Api.IntegrationTests
{
    public abstract class BaseIntegrationTests
    {
        private static bool _initialized;
        internal TestContainersService TestContainers { get; private set; }

        [OneTimeSetUp]
        public virtual async Task SetupAsync()
        {
            if (_initialized)
            {
                return;
            }

            BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
            BsonSerializer.TryRegisterSerializer(new DateOnlySerializer());

            var maxWait = TimeSpan.FromMinutes(BaseTestContainerOptions.StopTimeoutInMinutes);
            var sw = Stopwatch.StartNew();

            TestContainers = await TestContainersService.InitAsync();

            while (!TestContainers.IsReadyForUse)
            {
                if (sw.Elapsed > maxWait)
                {
                    Assert.Fail(
                        $"TestContainersService did not start within the allowed timeout of {maxWait.TotalSeconds} seconds."
                    );
                }

                await Task.Delay(TimeSpan.FromSeconds(ComponentTestOptions.TestContainersWaitingInSeconds));
            }

            sw.Stop();

            _initialized = true;
        }
    }
}

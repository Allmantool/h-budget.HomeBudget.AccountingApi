using AutoMapper;

using Microsoft.Extensions.Logging.Abstractions;

using HomeBudget.Accounting.Api.MapperProfileConfigurations;

namespace HomeBudget.Accounting.Api.Tests.Mappers
{
    [TestFixture]
    public class CrossAccountsTransferRequestMappingProfilerTests
    {
        private MapperConfiguration _configuration;

        [SetUp]
        public void Setup()
        {
            var configurationExpression = new MapperConfigurationExpression();
            configurationExpression.AddProfile<CrossAccountsTransferRequestMappingProfiler>();

            _configuration = new MapperConfiguration(configurationExpression, NullLoggerFactory.Instance);
        }

        [Test]
        public void Verify_That_Mapping_configuration_is_valid()
        {
            _configuration.AssertConfigurationIsValid();
        }
    }
}
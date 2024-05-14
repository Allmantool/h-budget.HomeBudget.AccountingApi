﻿using AutoMapper;

using HomeBudget.Accounting.Api.MapperProfileConfigurations;

namespace HomeBudget.Accounting.Api.Tests.Mappers
{
    [TestFixture]
    internal class PaymentAccountMappingProfilerTests
    {
        private MapperConfiguration _configuration;

        [SetUp]
        public void Setup()
        {
            _configuration = new MapperConfiguration(pr => pr.AddProfile<PaymentAccountMappingProfiler>());
        }

        [Test]
        public void Verify_That_Mapping_configuration_is_valid()
        {
            _configuration.AssertConfigurationIsValid();
        }
    }
}

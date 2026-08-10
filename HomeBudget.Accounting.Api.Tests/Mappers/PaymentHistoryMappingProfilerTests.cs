using AutoMapper;

using Microsoft.Extensions.Logging.Abstractions;

using HomeBudget.Accounting.Api.MapperProfileConfigurations;
using HomeBudget.Accounting.Api.Models.History;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Api.Tests.Mappers
{
    [TestFixture]
    public class PaymentHistoryMappingProfilerTests
    {
        private MapperConfiguration _configuration;

        [SetUp]
        public void Setup()
        {
            var configurationExpression = new MapperConfigurationExpression();
            configurationExpression.AddProfile<PaymentHistoryMappingProfiler>();

            _configuration = new MapperConfiguration(configurationExpression, NullLoggerFactory.Instance);
        }

        [Test]
        public void Verify_That_Mapping_configuration_is_valid()
        {
            _configuration.AssertConfigurationIsValid();
        }

        [Test]
        public void Map_Transfer_ThenIncludesRelatedPaymentAccountId()
        {
            var relatedPaymentAccountId = System.Guid.NewGuid();
            var source = new FinancialTransaction
            {
                TransactionType = TransactionTypes.Transfer,
                ContractorId = relatedPaymentAccountId
            };

            var result = _configuration.CreateMapper().Map<HistoryOperationRecordResponse>(source);

            Assert.That(result.RelatedPaymentAccountId, Is.EqualTo(relatedPaymentAccountId));
        }

        [Test]
        public void Map_Payment_ThenDoesNotIncludeRelatedPaymentAccountId()
        {
            var source = new FinancialTransaction
            {
                TransactionType = TransactionTypes.Payment,
                ContractorId = System.Guid.NewGuid()
            };

            var result = _configuration.CreateMapper().Map<HistoryOperationRecordResponse>(source);

            Assert.That(result.RelatedPaymentAccountId, Is.Null);
        }
    }
}

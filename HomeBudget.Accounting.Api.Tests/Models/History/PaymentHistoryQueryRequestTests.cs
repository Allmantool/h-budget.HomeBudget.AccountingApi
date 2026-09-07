using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

using FluentAssertions;

using HomeBudget.Accounting.Api.Models.History;

namespace HomeBudget.Accounting.Api.Tests.Models.History
{
    [TestFixture]
    public class PaymentHistoryQueryRequestTests
    {
        [Test]
        public void Validate_WhenDefaultRequest_ThenIsValid()
        {
            var results = Validate(new PaymentHistoryQueryRequest());

            results.Should().BeEmpty();
        }

        [TestCase(0)]
        [TestCase(10001)]
        public void Validate_WhenPageOutsideBounds_ThenReturnsValidationError(int page)
        {
            var results = Validate(new PaymentHistoryQueryRequest { Page = page });

            results.Should().Contain(result => result.MemberNames.Contains(nameof(PaymentHistoryQueryRequest.Page)));
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(101)]
        public void Validate_WhenPageSizeIsNotSupported_ThenReturnsValidationError(int pageSize)
        {
            var results = Validate(new PaymentHistoryQueryRequest { PageSize = pageSize });

            results.Should().Contain(result => result.MemberNames.Contains(nameof(PaymentHistoryQueryRequest.PageSize)));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(5)]
        [TestCase(10)]
        [TestCase(25)]
        [TestCase(50)]
        [TestCase(100)]
        public void Validate_WhenPageSizeIsWithinBoundedRange_ThenIsValid(int pageSize)
        {
            var results = Validate(new PaymentHistoryQueryRequest { PageSize = pageSize });

            results.Should().BeEmpty();
        }

        [Test]
        public void Validate_WhenSortAndRangesAreInvalid_ThenReturnsValidationErrors()
        {
            var results = Validate(new PaymentHistoryQueryRequest
            {
                SortBy = "Payload.Record.Key",
                SortDirection = "sideways",
                DateFrom = new System.DateOnly(2026, 9, 2),
                DateTo = new System.DateOnly(2026, 9, 1),
                AmountMin = 9m,
                AmountMax = 1m,
                CategoryId = "not-a-guid"
            });

            results.Should().HaveCount(5);
        }

        private static IReadOnlyCollection<ValidationResult> Validate(PaymentHistoryQueryRequest request)
        {
            return request.Validate(new ValidationContext(request)).ToArray();
        }
    }
}

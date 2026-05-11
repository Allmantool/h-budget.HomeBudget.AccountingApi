using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

using FluentAssertions;

using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Api.Models.Contractor;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.PaymentAccount;

namespace HomeBudget.Accounting.Api.Tests
{
    [TestFixture]
    public class RequestValidationTests
    {
        [Test]
        public void CreateOperationRequest_WhenAmountAndDateInvalid_ShouldReturnValidationErrors()
        {
            var request = new CreateOperationRequest
            {
                Amount = 0m,
                OperationDate = default
            };

            var errors = Validate(request);

            errors.Should().Contain(error => error.MemberNames.Contains(nameof(CreateOperationRequest.Amount)));
            errors.Should().Contain(error => error.MemberNames.Contains(nameof(CreateOperationRequest.OperationDate)));
        }

        [Test]
        public void CreateOperationRequest_WhenReferenceIdsInvalid_ShouldReturnValidationErrors()
        {
            var request = new CreateOperationRequest
            {
                Amount = 10m,
                OperationDate = new(2026, 5, 11),
                CategoryId = "not-a-guid",
                ContractorId = "also-not-a-guid"
            };

            var errors = Validate(request);

            errors.Should().Contain(error => error.MemberNames.Contains(nameof(CreateOperationRequest.CategoryId)));
            errors.Should().Contain(error => error.MemberNames.Contains(nameof(CreateOperationRequest.ContractorId)));
        }

        [Test]
        public void CreatePaymentAccountRequest_WhenBodyFieldsInvalid_ShouldReturnValidationErrors()
        {
            var request = new CreatePaymentAccountRequest
            {
                AccountType = 999
            };

            var errors = Validate(request);

            errors.Should().Contain(error => error.MemberNames.Contains(nameof(CreatePaymentAccountRequest.Agent)));
            errors.Should().Contain(error => error.MemberNames.Contains(nameof(CreatePaymentAccountRequest.Currency)));
            errors.Should().Contain(error => error.MemberNames.Contains(nameof(CreatePaymentAccountRequest.AccountType)));
        }

        [Test]
        public void CreateCategoryRequest_WhenHandbookBodyInvalid_ShouldReturnValidationErrors()
        {
            var request = new CreateCategoryRequest
            {
                CategoryType = 999,
                NameNodes = [" "]
            };

            var errors = Validate(request);

            errors.Should().Contain(error => error.MemberNames.Contains(nameof(CreateCategoryRequest.NameNodes)));
            errors.Should().Contain(error => error.MemberNames.Contains(nameof(CreateCategoryRequest.CategoryType)));
        }

        [Test]
        public void CreateContractorRequest_WhenNameNodesInvalid_ShouldReturnValidationError()
        {
            var request = new CreateContractorRequest
            {
                NameNodes = []
            };

            var errors = Validate(request);

            errors.Should().Contain(error => error.MemberNames.Contains(nameof(CreateContractorRequest.NameNodes)));
        }

        private static IReadOnlyCollection<ValidationResult> Validate(object request)
        {
            var results = new List<ValidationResult>();

            Validator.TryValidateObject(
                request,
                new ValidationContext(request),
                results,
                true);

            return results;
        }
    }
}

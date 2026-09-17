using FluentAssertions;

using HomeBudget.Accounting.Api.Idempotency;
using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Api.Models.Contractor;
using HomeBudget.Accounting.Api.Models.PaymentAccount;

namespace HomeBudget.Accounting.Api.Tests
{
    [TestFixture]
    public sealed class ReferenceCreateFingerprintTests
    {
        [Test]
        public void AccountFingerprint_WhenBusinessRequestIsRepeated_IsStable()
        {
            var request = new CreatePaymentAccountRequest
            {
                AccountType = 2,
                Currency = "BYN",
                InitialBalance = 0m,
                Agent = "Family Pro",
                Description = "Cashless",
                SourceReference = "FamilyPro12:hash:ACCOUNT:326"
            };
            var changed = new CreatePaymentAccountRequest
            {
                AccountType = request.AccountType,
                Currency = request.Currency,
                InitialBalance = request.InitialBalance,
                Agent = request.Agent,
                Description = "Changed",
                SourceReference = request.SourceReference
            };

            ReferenceCreateFingerprint.Account(request).Should().Be(ReferenceCreateFingerprint.Account(request));
            ReferenceCreateFingerprint.Account(changed).Should().NotBe(ReferenceCreateFingerprint.Account(request));
        }

        [Test]
        public void CategoryFingerprint_UsesTargetIdentityAndSourceReference()
        {
            var first = new CreateCategoryRequest
            {
                CategoryType = 1,
                NameNodes = ["Household", "Food"],
                SourceReference = "FamilyPro12:hash:TARGET_CATEGORY:shared-3-5-6"
            };
            var repeated = first with { };
            var changedSource = first with { SourceReference = "FamilyPro12:hash:CATEGORY:7" };

            ReferenceCreateFingerprint.Category(first).Should().Be(ReferenceCreateFingerprint.Category(repeated));
            ReferenceCreateFingerprint.Category(first).Should().NotBe(ReferenceCreateFingerprint.Category(changedSource));
        }

        [Test]
        public void ContractorFingerprint_DoesNotUseDisplayNameAsTheOnlyIdentity()
        {
            var first = new CreateContractorRequest
            {
                NameNodes = ["Historical payee"],
                SourceReference = "FamilyPro12:hash:PAYEE:1"
            };
            var second = first with { SourceReference = "FamilyPro12:hash:PAYEE:2" };

            ReferenceCreateFingerprint.Contractor(first).Should().NotBe(ReferenceCreateFingerprint.Contractor(second));
        }
    }
}

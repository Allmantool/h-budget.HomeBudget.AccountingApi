using System.Text.Json;

using FluentAssertions;

using HomeBudget.Accounting.Api.Models.PaymentAccount;
using HomeBudget.Accounting.Domain.Enumerations;

namespace HomeBudget.Accounting.Api.Tests
{
    [TestFixture]
    public class CreatePaymentAccountRequestTests
    {
        [Test]
        public void AccountTypesJsonConverter_ShouldConvert_WithExpectedResults()
        {
            var rowPayload = "{\"accountType\":1,\"currency\":\"BYN\",\"balance\":\"150\",\"agent\":\"Priorbank\",\"description\":\"Card for Salary\"}";

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };

            var deserializeResult = JsonSerializer.Deserialize<CreatePaymentAccountRequest>(
                rowPayload,
                options);

            var serializeResult = JsonSerializer.Serialize(deserializeResult, options);

            Assert.Multiple(() =>
            {
                BaseEnumeration.FromValue<AccountTypes>(deserializeResult.AccountType).Should().Be(AccountTypes.Virtual);
                serializeResult.Should().Contain("\"AccountType\":1");
            });
        }
    }
}

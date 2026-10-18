using System;
using System.Linq;
using System.Text.Json;

using FluentAssertions;

using HomeBudget.Accounting.Api.Models.Operations.Responses;

namespace HomeBudget.Accounting.Api.Tests
{
    [TestFixture]
    internal sealed class LedgerGenerationApiContractTests
    {
        [TestCase(typeof(CreateOperationResponse))]
        [TestCase(typeof(UpdateOperationResponse))]
        [TestCase(typeof(RemoveOperationResponse))]
        [TestCase(typeof(CrossAccountsTransferResponse))]
        public void MutationResponse_ExposesTheSpaGenerationContract(Type responseType)
        {
            var propertyNames = responseType
                .GetProperties()
                .Select(property => property.Name)
                .ToArray();

            propertyNames.Should().Contain([
                "LedgerId",
                "LedgerGenerationId",
                "GenerationToken"
            ]);
        }

        [Test]
        public void ActiveGenerationResponse_SerializesTheSpaGenerationContract()
        {
            var responseType = typeof(PaymentCommandStatusResponse).Assembly
                .GetType("HomeBudget.Accounting.Api.Models.Operations.Responses.LedgerGenerationResponse");

            responseType.Should().NotBeNull();
            var instance = Activator.CreateInstance(responseType!);
            var json = JsonSerializer.Serialize(instance, responseType!, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            json.Should().Contain("\"ledgerId\"");
            json.Should().Contain("\"ledgerGenerationId\"");
            json.Should().Contain("\"generationToken\"");
        }
    }
}

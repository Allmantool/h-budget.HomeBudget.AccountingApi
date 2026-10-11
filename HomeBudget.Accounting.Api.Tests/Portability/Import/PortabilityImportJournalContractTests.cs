using System.Reflection;

using FluentAssertions;

using HomeBudget.Accounting.Infrastructure.Configuration;

namespace HomeBudget.Accounting.Api.Tests.Portability.Import
{
    [TestFixture]
    internal sealed class PortabilityImportJournalContractTests
    {
        [Test]
        public void Infrastructure_WhenImportJournalIsAvailable_ExposesDurableCreateAndReadContract()
        {
            var assembly = typeof(DependencyRegistrations).Assembly;
            const string prefix = "HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import.";
            var store = assembly.GetType(prefix + "IPortabilityImportJournalStore");
            var sqlStore = assembly.GetType(prefix + "SqlPortabilityImportJournalStore");

            store.Should().NotBeNull("task 4.2 requires a durable import journal abstraction");
            sqlStore.Should().NotBeNull("task 4.2 requires restart-readable SQL persistence");
            store.GetMethod("CreateOrReadAsync").Should().NotBeNull();
            store.GetMethod("GetAsync").Should().NotBeNull();
        }
    }
}

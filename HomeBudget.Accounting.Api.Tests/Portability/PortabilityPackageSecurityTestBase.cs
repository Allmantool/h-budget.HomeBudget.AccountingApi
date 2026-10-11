using System;
using System.IO;
using System.Threading.Tasks;

using FluentAssertions;

namespace HomeBudget.Accounting.Api.Tests.Portability
{
    internal abstract class PortabilityPackageSecurityTestBase
    {
        protected DisposablePortabilityTestRoot TestRoot { get; private set; }

        protected PortabilityArchiveFactory Archives { get; private set; }

        protected PortabilityInspectorDriver Inspector { get; private set; }

        protected PortabilityContractFixture Contract { get; private set; }

        [SetUp]
        public void SetUp()
        {
            TestRoot = new DisposablePortabilityTestRoot();
            Contract = new PortabilityContractFixture();
            Archives = new PortabilityArchiveFactory(Contract);
            Inspector = new PortabilityInspectorDriver();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                TestRoot.AssertNoArchiveOutputRemains();
            }
            finally
            {
                TestRoot.Dispose();
            }
        }

        protected async Task AssertRejectedAsync(
            MemoryStream archive,
            string expectedCode)
        {
            var code = await Inspector
                .InspectRejectedAsync(archive, TestRoot.ExtractionPath)
                .ConfigureAwait(false);

            code.Should().Be(expectedCode);
        }
    }
}

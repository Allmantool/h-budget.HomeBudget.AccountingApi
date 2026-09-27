using System;
using System.Linq;
using System.Reflection;

using FluentAssertions;

namespace HomeBudget.Accounting.Api.Tests
{
    [TestFixture]
    public sealed class HealthChecksUiRuntimeDependencyTests
    {
        [Test]
        public void IdentityModelReference_ShouldBeResolvableAtRuntime()
        {
            var healthChecksUiAssembly = Assembly.Load("HealthChecks.UI");
            var identityModelReference = healthChecksUiAssembly
                .GetReferencedAssemblies()
                .Single(reference => reference.Name == "IdentityModel");

            var loadDependency = () => Assembly.Load(identityModelReference);

            loadDependency.Should().NotThrow(
                "the Accounting API publish must include every dependency used by the HealthChecks UI collector");
        }
    }
}

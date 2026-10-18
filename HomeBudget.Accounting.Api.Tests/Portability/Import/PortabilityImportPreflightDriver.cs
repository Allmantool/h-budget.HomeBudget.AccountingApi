using System;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Infrastructure.Configuration;

namespace HomeBudget.Accounting.Api.Tests.Portability.Import
{
    internal sealed class PortabilityImportPreflightDriver
    {
        private const string Namespace = "HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import";
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public async Task ValidateAsync(PortabilityImportScenario scenario)
        {
            var assembly = typeof(DependencyRegistrations).Assembly;
            var serviceType = Require(assembly, "PortabilityImportPreflightService");
            var probeType = Require(assembly, "IPortabilityImportTargetProbe");
            var optionsType = Require(assembly, "PortabilityImportPreflightOptions");
            var requestType = Require(assembly, "PortabilityImportRequest");
            var proxy = DispatchProxy.Create(probeType, typeof(PortabilityImportTargetProbeProxy));
            ((PortabilityImportTargetProbeProxy)proxy).Scenario = scenario;
            var options = Activator.CreateInstance(optionsType);
            var request = JsonSerializer.Deserialize(scenario.RequestJson, requestType, JsonOptions);
            var service = Activator.CreateInstance(serviceType, proxy, options);
            var method = serviceType.GetMethod("ValidateAsync", [requestType, typeof(string), typeof(CancellationToken)]);
            if (method == null)
            {
                throw new AssertionException("Expected preflight ValidateAsync contract is missing.");
            }

            try
            {
                await ((Task)method.Invoke(service, [request, scenario.Package.RootPath, CancellationToken.None]))
                    .ConfigureAwait(false);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw exception.InnerException;
            }
        }

        public async Task<PortabilityImportError> RejectAsync(PortabilityImportScenario scenario)
        {
            try
            {
                await ValidateAsync(scenario).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                return new PortabilityImportError(
                    Read(exception, "Code"),
                    Read(exception, "EntryPath"),
                    Read(exception, "EntityId"),
                    Read(exception, "FieldPath"));
            }

            throw new AssertionException("Expected import preflight rejection.");
        }

        private static Type Require(Assembly assembly, string name) => assembly.GetType($"{Namespace}.{name}")
            ?? throw new AssertionException($"Expected production import contract '{Namespace}.{name}' is missing.");

        private static string Read(Exception exception, string property) =>
            exception.GetType().GetProperty(property)?.GetValue(exception) as string;
    }

    internal sealed record PortabilityImportError(string Code, string EntryPath, string EntityId, string FieldPath);
}

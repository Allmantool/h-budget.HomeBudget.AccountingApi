using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Infrastructure.Configuration;

using NUnit.Framework;

namespace HomeBudget.Accounting.Api.Tests.Portability.Export
{
    internal sealed class PortabilityExportContractDriver
    {
        private const string ContractNamespace =
            "HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Export";
        private static readonly string[] RequiredReaderMethods =
        [
            "AcquireCutoffAsync",
            "WaitForProjectionAsync",
            "ReadAccountsAsync",
            "ReadCurrenciesAsync",
            "ReadCategoriesAsync",
            "ReadContractorsAsync",
            "ReadSettingsAsync",
            "ReadRatesAsync",
            "ReadOperationsAsync",
            "ReadLineageAsync",
            "ReadReconciliationAsync"
        ];
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
        private readonly Assembly _infrastructureAssembly = typeof(DependencyRegistrations).Assembly;

        public async Task ExportAsync(
            PortabilityExportScenario scenario,
            CancellationToken cancellationToken = default)
        {
            var contract = ResolveContract();
            var reader = DispatchProxy.Create(contract.ReaderType, typeof(PortabilityExportReaderProxy));
            ((PortabilityExportReaderProxy)reader).Scenario = scenario;

            var options = Activator.CreateInstance(contract.OptionsType);
            SetRequiredProperty(options, "MaxBufferedRecords", 64);
            SetRequiredProperty(options, "ProjectionCatchupTimeout", TimeSpan.FromSeconds(5));
            SetRequiredProperty(options, "MaxExportRecords", 100_000L);

            var request = JsonSerializer.Deserialize(
                scenario.RequestJson,
                contract.RequestType,
                SerializerOptions);
            if (request == null)
            {
                Assert.Fail("The production portability export request could not be constructed.");
            }

            var exporter = contract.Constructor.Invoke([reader, options]);
            object invocation;
            try
            {
                invocation = contract.ExportMethod.Invoke(exporter, [request, cancellationToken]);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw exception.InnerException;
            }

            if (invocation is not Task task)
            {
                Assert.Fail("The production portability export operation must return Task or Task<T>.");
                return;
            }

            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not AssertionException)
            {
                throw Unwrap(exception);
            }
        }

        public async Task<string> ExportRejectedAsync(PortabilityExportScenario scenario)
        {
            try
            {
                await ExportAsync(scenario).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                return ReadCode(exception);
            }
            catch (System.IO.IOException exception)
            {
                return ReadCode(exception);
            }

            Assert.Fail("Expected the portability export to fail.");
            return null;
        }

        private static string ReadCode(Exception exception) =>
            exception.GetType().GetProperty("Code")?.GetValue(exception) as string
            ?? exception.GetType().Name;

        private static Exception Unwrap(Exception exception)
        {
            var current = exception;
            while (current is TargetInvocationException or AggregateException)
            {
                current = current switch
                {
                    TargetInvocationException { InnerException: not null } target => target.InnerException,
                    AggregateException aggregate when aggregate.InnerExceptions.Count == 1 =>
                        aggregate.InnerExceptions[0],
                    _ => current
                };
            }

            return current;
        }

        private ExportContract ResolveContract()
        {
            var exporterType = ResolveRequiredType("PortabilityExportService");
            var readerType = ResolveRequiredType("IPortabilityExportReader");
            var optionsType = ResolveRequiredType("PortabilityExportOptions");
            var requestType = ResolveRequiredType("PortabilityExportRequest");

            if (!readerType.IsInterface)
            {
                Assert.Fail($"Expected production contract '{readerType.FullName}' to be an interface.");
            }

            ValidateReaderContract(readerType);
            var constructor = exporterType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                [readerType, optionsType],
                modifiers: null);
            if (constructor == null)
            {
                Assert.Fail(
                    $"Expected production exporter '{exporterType.FullName}' constructor "
                    + $"({readerType.Name}, {optionsType.Name}).");
            }

            var exportMethod = exporterType.GetMethod(
                "ExportAsync",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                [requestType, typeof(CancellationToken)],
                modifiers: null);
            if (exportMethod == null || !typeof(Task).IsAssignableFrom(exportMethod.ReturnType))
            {
                Assert.Fail(
                    $"Expected production exporter '{exporterType.FullName}.ExportAsync"
                    + $"({requestType.Name}, CancellationToken)' returning Task or Task<T>.");
            }

            return new ExportContract(
                readerType,
                optionsType,
                requestType,
                constructor,
                exportMethod);
        }

        private static void ValidateReaderContract(Type readerType)
        {
            var methods = readerType.GetMethods().ToDictionary(method => method.Name, StringComparer.Ordinal);
            foreach (var methodName in RequiredReaderMethods)
            {
                if (!methods.ContainsKey(methodName))
                {
                    Assert.Fail(
                        $"Expected authoritative export reader method '{readerType.FullName}.{methodName}'.");
                }
            }

            foreach (var methodName in RequiredReaderMethods.Where(name => name.StartsWith("Read", StringComparison.Ordinal)))
            {
                var returnType = methods[methodName].ReturnType;
                if (!returnType.IsGenericType
                    || returnType.GetGenericTypeDefinition() != typeof(IAsyncEnumerable<>)
                    || returnType.GetGenericArguments()[0] != typeof(JsonElement))
                {
                    Assert.Fail(
                        $"Authoritative export reader '{methodName}' must stream IAsyncEnumerable<JsonElement>.");
                }
            }
        }

        private Type ResolveRequiredType(string name)
        {
            var fullName = $"{ContractNamespace}.{name}";
            return _infrastructureAssembly.GetType(fullName, false)
                ?? throw new AssertionException(
                    $"Expected production portability export contract '{fullName}' is missing.");
        }

        private static void SetRequiredProperty(object instance, string propertyName, object value)
        {
            var property = instance.GetType().GetProperty(propertyName);
            if (property?.CanWrite != true)
            {
                Assert.Fail(
                    $"Expected production export option '{instance.GetType().FullName}.{propertyName}'.");
            }

            property.SetValue(instance, value);
        }

        private sealed record ExportContract(
            Type ReaderType,
            Type OptionsType,
            Type RequestType,
            ConstructorInfo Constructor,
            MethodInfo ExportMethod);
    }
}

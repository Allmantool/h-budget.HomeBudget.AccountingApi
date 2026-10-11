using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Infrastructure.Configuration;

namespace HomeBudget.Accounting.Api.Tests.Portability
{
    internal sealed class PortabilityInspectorDriver
    {
        private const string InspectorTypeName =
            "HomeBudget.Accounting.Infrastructure.DataProtection.Portability.PortabilityPackageInspector";
        private const string OptionsTypeName =
            "HomeBudget.Accounting.Infrastructure.DataProtection.Portability.PortabilityPackageInspectionOptions";

        public async Task<string> InspectRejectedAsync(
            Stream archive,
            string extractionRoot,
            IReadOnlyDictionary<string, object> optionOverrides = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await InspectAsync(archive, extractionRoot, optionOverrides, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (exception is InvalidOperationException
                    && exception.Message.StartsWith("Expected production", StringComparison.Ordinal))
                {
                    throw;
                }

                var code = exception.GetType()
                    .GetProperty("Code", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(exception) as string;
                return !string.IsNullOrWhiteSpace(code)
                    ? code
                    : throw new InvalidOperationException(
                        $"Package rejection '{exception.GetType().FullName}' did not expose a stable Code.",
                        exception);
            }

            throw new InvalidOperationException("Expected production inspector to reject the package.");
        }

        public async Task InspectAsync(
            Stream archive,
            string extractionRoot,
            IReadOnlyDictionary<string, object> optionOverrides = null,
            CancellationToken cancellationToken = default)
        {
            var assembly = typeof(DependencyRegistrations).Assembly;
            var inspectorType = assembly.GetType(InspectorTypeName)
                ?? throw MissingContract(InspectorTypeName);
            var optionsType = assembly.GetType(OptionsTypeName)
                ?? throw MissingContract(OptionsTypeName);
            var options = Activator.CreateInstance(optionsType, true)
                ?? throw MissingContract($"constructible {OptionsTypeName}");

            foreach (var option in optionOverrides ?? new Dictionary<string, object>())
            {
                SetOption(optionsType, options, option.Key, option.Value);
            }

            var constructor = inspectorType
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SingleOrDefault(candidate =>
                    candidate.GetParameters() is [ { ParameterType: var parameterType }]
                    && parameterType == optionsType)
                ?? throw MissingContract($"{InspectorTypeName}({OptionsTypeName})");
            var inspector = constructor.Invoke([options]);
            var method = inspectorType.GetMethod(
                "InspectAndExtractAsync",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                [typeof(Stream), typeof(string), typeof(CancellationToken)],
                null)
                ?? throw MissingContract(
                    $"{InspectorTypeName}.InspectAndExtractAsync(Stream, string, CancellationToken)");

            archive.Position = 0;
            object invocation;
            try
            {
                invocation = method.Invoke(inspector, [archive, extractionRoot, cancellationToken]);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }

            if (invocation is not Task task)
            {
                throw MissingContract($"Task return from {InspectorTypeName}.InspectAndExtractAsync");
            }

            await task.ConfigureAwait(false);
        }

        private static void SetOption(Type optionsType, object options, string name, object value)
        {
            var property = optionsType.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw MissingContract($"{OptionsTypeName}.{name}");
            var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            property.SetValue(options, Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture));
        }

        private static InvalidOperationException MissingContract(string member) =>
            new($"Expected production portability inspection contract '{member}' is missing.");
    }
}

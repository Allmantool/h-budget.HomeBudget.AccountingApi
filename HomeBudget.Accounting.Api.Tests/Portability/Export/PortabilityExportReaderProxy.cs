using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Api.Tests.Portability.Export
{
    internal class PortabilityExportReaderProxy : DispatchProxy
    {
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        public PortabilityExportScenario Scenario { get; set; }

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            Scenario.RecordCall(targetMethod.Name, args ?? []);
            return targetMethod.Name switch
            {
                "AcquireCutoffAsync" => CreateSerializedResult(
                    targetMethod.ReturnType,
                    Scenario.CutoffJson),
                "WaitForProjectionAsync" => CreateBooleanResult(
                    targetMethod.ReturnType,
                    Scenario.ProjectionCanCatchUp),
                _ when targetMethod.Name.StartsWith("Read", StringComparison.Ordinal) =>
                    CreateRecordStream(targetMethod, args),
                _ => throw new InvalidOperationException(
                    $"Unexpected production export reader member '{targetMethod.Name}'.")
            };
        }

        private object CreateRecordStream(MethodInfo method, object[] args)
        {
            if (!method.ReturnType.IsGenericType
                || method.ReturnType.GetGenericTypeDefinition() != typeof(IAsyncEnumerable<>)
                || method.ReturnType.GetGenericArguments()[0] != typeof(JsonElement))
            {
                throw new InvalidOperationException(
                    $"Export reader '{method.Name}' must return IAsyncEnumerable<JsonElement>.");
            }

            var cancellationToken = args?.OfType<CancellationToken>().LastOrDefault() ?? default;
            return Scenario.ReadAsync(method.Name, cancellationToken);
        }

        private static object CreateSerializedResult(Type returnType, string json)
        {
            var resultType = GetAsyncResultType(returnType);
            var value = JsonSerializer.Deserialize(json, resultType, SerializerOptions);
            return CreateAsyncResult(returnType, resultType, value);
        }

        private static object CreateBooleanResult(Type returnType, bool value)
        {
            var resultType = GetAsyncResultType(returnType);
            if (resultType != typeof(bool))
            {
                throw new InvalidOperationException("WaitForProjectionAsync must return Task<bool> or ValueTask<bool>.");
            }

            return CreateAsyncResult(returnType, resultType, value);
        }

        private static Type GetAsyncResultType(Type returnType)
        {
            if (!returnType.IsGenericType
                || returnType.GetGenericArguments().Length != 1)
            {
                throw new InvalidOperationException("Export reader coordination methods must return Task<T> or ValueTask<T>.");
            }

            var definition = returnType.GetGenericTypeDefinition();
            if (definition != typeof(Task<>) && definition != typeof(ValueTask<>))
            {
                throw new InvalidOperationException("Export reader coordination methods must return Task<T> or ValueTask<T>.");
            }

            return returnType.GetGenericArguments()[0];
        }

        private static object CreateAsyncResult(Type returnType, Type resultType, object value)
        {
            if (returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                return Activator.CreateInstance(returnType, value);
            }

            return typeof(Task)
                .GetMethod(nameof(Task.FromResult))
                .MakeGenericMethod(resultType)
                .Invoke(null, [value]);
        }
    }
}

using System;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Api.Tests.Portability.Import
{
    internal class PortabilityImportTargetProbeProxy : DispatchProxy
    {
        public PortabilityImportScenario Scenario { get; set; }

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (targetMethod.Name != "ProbeAsync")
            {
                Scenario.TargetMutationAttempts++;
                throw new InvalidOperationException("Preflight attempted a target mutation.");
            }

            Scenario.ProbeCalls++;
            var resultType = targetMethod.ReturnType.GetGenericArguments()[0];
            var value = JsonSerializer.Deserialize(
                Scenario.TargetJson,
                resultType,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return typeof(Task).GetMethod(nameof(Task.FromResult))
                .MakeGenericMethod(resultType)
                .Invoke(null, [value]);
        }
    }
}

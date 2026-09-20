using System;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.ReleaseVerification
{
    internal static class FamilyProFullRunExecutionGate
    {
        public static Task ExecuteAsync<TPlan>(Func<TPlan> loadPlan, Func<TPlan, Task> provisionAndExecute)
        {
            ArgumentNullException.ThrowIfNull(loadPlan);
            ArgumentNullException.ThrowIfNull(provisionAndExecute);
            var validatedPlan = loadPlan();
            return provisionAndExecute(validatedPlan);
        }

        public static async Task ExecuteSecondRunAsync(
            Func<Task> executeFirstRun,
            Action validateTerminalFirstRun,
            Func<Task> executeSecondRun)
        {
            ArgumentNullException.ThrowIfNull(executeFirstRun);
            ArgumentNullException.ThrowIfNull(validateTerminalFirstRun);
            ArgumentNullException.ThrowIfNull(executeSecondRun);
            await executeFirstRun();
            validateTerminalFirstRun();
            await executeSecondRun();
        }
    }
}

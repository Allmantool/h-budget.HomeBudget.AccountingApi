using HomeBudget.Accounting.Infrastructure.DataProtection;

namespace HomeBudget.Components.Operations.Commands.Models
{
    internal interface ILedgerGenerationBoundCommand
    {
        LedgerGenerationScope ExpectedGeneration { get; }
    }
}

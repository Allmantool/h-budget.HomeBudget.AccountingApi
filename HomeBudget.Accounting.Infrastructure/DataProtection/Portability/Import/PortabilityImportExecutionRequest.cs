using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed class PortabilityImportExecutionRequest
    {
        public Guid OperationId { get; init; }

        public string ActorId { get; init; }

        public PortabilityImportRequest Import { get; init; }
    }
}

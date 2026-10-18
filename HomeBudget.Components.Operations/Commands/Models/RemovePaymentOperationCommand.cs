using System;

using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Core.Commands;
using HomeBudget.Core.Models;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Commands.Models
{
    internal sealed class RemovePaymentOperationCommand(FinancialTransaction operationForDelete)
        : IRequest<Result<Guid>>, ICorrelatedCommand, IIdempotentPaymentCommand, ILedgerGenerationBoundCommand
    {
        public string CorrelationId { get; set; }

        public FinancialTransaction OperationForDelete { get; } = operationForDelete;

        public PaymentCommandContext CommandContext { get; init; }

        public LedgerGenerationScope ExpectedGeneration { get; init; }
    }
}

using System;

using MediatR;

using HomeBudget.Core.Commands;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Accounts.Commands.Models
{
    public sealed class UpdatePaymentAccountBalanceCommand(
        Guid paymentAccountId,
        decimal balance,
        long projectionFence = 0)
        : IRequest<Result<Guid>>, ICorrelatedCommand
    {
        public string CorrelationId { get; set; }
        public Guid PaymentAccountId { get; } = paymentAccountId;
        public decimal Balance { get; } = balance;
        public long ProjectionFence { get; } = projectionFence;
    }
}

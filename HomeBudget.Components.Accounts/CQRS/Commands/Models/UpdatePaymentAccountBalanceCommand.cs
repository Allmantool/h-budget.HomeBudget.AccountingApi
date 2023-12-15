using System;

using MediatR;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Accounts.CQRS.Commands.Models
{
    public class UpdatePaymentAccountBalanceCommand(Guid paymentAccountId, decimal balance)
        : IRequest<Result<Guid>>
    {
        public Guid PaymentAccountId { get; } = paymentAccountId;

        public decimal Balance { get; } = balance;
    }
}

using System;

using MediatR;

using HomeBudget.Core.Models;

namespace HomeBudget.Components.Accounts.Commands.Models
{
    public class UpdatePaymentAccountBalanceCommand(Guid paymentAccountId, decimal balance)
        : IRequest<Result<Guid>>
    {
        public Guid PaymentAccountId { get; } = paymentAccountId;

        public decimal Balance { get; } = balance;
    }
}

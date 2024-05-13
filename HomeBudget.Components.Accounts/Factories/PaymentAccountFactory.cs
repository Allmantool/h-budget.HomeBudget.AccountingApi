using System;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Accounts.Factories
{
    internal class PaymentAccountFactory : IPaymentAccountFactory
    {
        public PaymentAccount Create(
            string agent,
            decimal initialBalance,
            string currency,
            string description,
            AccountTypes accountType)
        {
            return new PaymentAccount
            {
                Key = Guid.NewGuid(),
                Agent = agent,
                InitialBalance = initialBalance,
                Currency = currency,
                Description = description,
                Type = accountType
            };
        }
    }
}

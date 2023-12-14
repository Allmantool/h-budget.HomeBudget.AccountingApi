using System;
using System.Collections.Generic;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Accounts
{
    public static class MockAccountsStore
    {
        public static readonly List<PaymentAccount> Records = new()
        {
            new()
            {
                Key = Guid.Parse("92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84"),
                Agent = "Priorbank",
                Type = AccountTypes.Bank,
                Balance = 0m,
                Currency = CurrencyTypes.BYN.ToString(),
                Description = "Prior description"
            },
            new()
            {
                Key = Guid.Parse("35a40606-3782-4f53-8f64-49649b71ab6f"),
                Agent = "Priorbank - update test",
                Type = AccountTypes.Bank,
                Balance = 12.0m,
                Currency = CurrencyTypes.BYN.ToString(),
                Description = "Prior description - test update"
            },
            new()
            {
                Key = Guid.Parse("47d84ccf-7f79-4b6b-a691-3c2b313b0905"),
                Agent = "Priorbank",
                Type = AccountTypes.Bank,
                Balance = 20.24m,
                Currency = CurrencyTypes.USD.ToString(),
                Description = "Prior description"
            },
            new()
            {
                Key = Guid.Parse("257f78da-1e0f-4ce7-9c50-b494804a6830"),
                Agent = "Priorbank",
                Type = AccountTypes.Cash,
                Balance = 20.24m,
                Currency = CurrencyTypes.USD.ToString(),
                Description = "Prior description"
            },
            new()
            {
                Key = Guid.Parse("c9b33506-9a98-4f76-ad8e-17c96858305b"),
                Agent = "Priorbank",
                Type = AccountTypes.Loan,
                Balance = 20.24m,
                Currency = CurrencyTypes.USD.ToString(),
                Description = "Prior description"
            },
            new()
            {
                Key = Guid.Parse("852530a6-70b0-4040-8912-8558d59d977a"),
                Agent = "Techobank",
                Type = AccountTypes.Bank,
                Balance = 1320.24m,
                Currency = CurrencyTypes.BYN.ToString(),
                Description = "Tech description"
            },
            new()
            {
                Key = Guid.Parse("aed5a7ff-cd0f-4c65-b5ab-a3d7b8f9ac07"),
                Agent = "Techobank - For delete tests",
                Type = AccountTypes.Bank,
                Balance = 25.24m,
                Currency = CurrencyTypes.BYN.ToString(),
                Description = "Tech description"
            }
        };
    }
}

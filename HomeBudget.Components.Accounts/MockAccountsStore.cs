using System;
using System.Collections.Generic;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Accounts
{
    public static class MockAccountsStore
    {
        public static ICollection<PaymentAccount> Records { get; } =
        [
            new PaymentAccount
            {
                Key = Guid.Parse("92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84"),
                Agent = "Priorbank",
                Type = AccountTypes.Bank,
                Balance = 0m,
                Currency = CurrencyTypes.BYN.ToString(),
                Description = "Prior description"
            },
            new PaymentAccount
            {
                Key = Guid.Parse("35a40606-3782-4f53-8f64-49649b71ab6f"),
                Agent = "Priorbank - update test",
                Type = AccountTypes.Bank,
                Balance = 12.0m,
                Currency = CurrencyTypes.BYN.ToString(),
                Description = "Prior description - test update"
            },
            new PaymentAccount
            {
                Key = Guid.Parse("47d84ccf-7f79-4b6b-a691-3c2b313b0905"),
                Agent = "Priorbank",
                Type = AccountTypes.Bank,
                Balance = 20.24m,
                Currency = CurrencyTypes.USD.ToString(),
                Description = "Prior description"
            },
            new PaymentAccount
            {
                Key = Guid.Parse("257f78da-1e0f-4ce7-9c50-b494804a6830"),
                Agent = "Priorbank",
                Type = AccountTypes.Cash,
                Balance = 20.24m,
                Currency = CurrencyTypes.USD.ToString(),
                Description = "Prior description"
            },
            new PaymentAccount
            {
                Key = Guid.Parse("e6739854-7191-4e0a-a655-7d067aecc220"),
                Agent = "Bank - Negative balance",
                Type = AccountTypes.Cash,
                Balance = 0,
                Currency = CurrencyTypes.USD.ToString(),
                Description = "Negative balance"
            },
            new PaymentAccount
            {
                Key = Guid.Parse("4daf3bef-5ffc-4a24-a032-eb97e8593a24"),
                Agent = "Test update after added new one record",
                Type = AccountTypes.Cash,
                Balance = 0,
                Currency = CurrencyTypes.USD.ToString(),
                Description = "Test update after added new one record"
            },
            new PaymentAccount
            {
                Key = Guid.Parse("c9b33506-9a98-4f76-ad8e-17c96858305b"),
                Agent = "Priorbank",
                Type = AccountTypes.Loan,
                Balance = 20.24m,
                Currency = CurrencyTypes.USD.ToString(),
                Description = "Prior description"
            },
            new PaymentAccount
            {
                Key = Guid.Parse("852530a6-70b0-4040-8912-8558d59d977a"),
                Agent = "Techobank",
                Type = AccountTypes.Bank,
                Balance = 1320.24m,
                Currency = CurrencyTypes.BYN.ToString(),
                Description = "Tech description"
            },
            new PaymentAccount
            {
                Key = Guid.Parse("f38f6c9d-3f1c-4e50-84f9-47d9b5e6a47d"),
                Agent = "Ordering check",
                Type = AccountTypes.Bank,
                Balance = 0,
                Currency = CurrencyTypes.BYN.ToString(),
                Description = "Tech description"
            },
            new PaymentAccount
            {
                Key = Guid.Parse("421f203b-fc78-4c7c-93c8-5d56e9aefc30"),
                Agent = "Ordering check",
                Type = AccountTypes.Bank,
                Balance = 0,
                Currency = CurrencyTypes.BYN.ToString(),
                Description = "Tech description"
            },
            new PaymentAccount
            {
                Key = Guid.Parse("5f5af6ad-8a4f-47b9-ab90-2d884edc1aa4"),
                Agent = "Techobank Updaet with no existed",
                Type = AccountTypes.Bank,
                Balance = 1320.24m,
                Currency = CurrencyTypes.BYN.ToString(),
                Description = "Techobank Update with no existed"
            },
            new PaymentAccount
            {
                Key = Guid.Parse("1035d1c0-ab8e-4438-973a-5a3da3f22a1e"),
                Agent = "Techobank - Remove tests",
                Type = AccountTypes.Bank,
                Balance = 1320.24m,
                Currency = CurrencyTypes.BYN.ToString(),
                Description = "Remove tests"
            },
            new PaymentAccount
            {
                Key = Guid.Parse("0dbfb498-83e1-4e02-a2c1-c0761eab8529"),
                Agent = "Techobank - Remove tests by Ref",
                Type = AccountTypes.Bank,
                Balance = 1320.24m,
                Currency = CurrencyTypes.BYN.ToString(),
                Description = "Remove tests"
            },
            new PaymentAccount
            {
                Key = Guid.Parse("aed5a7ff-cd0f-4c61-b5ab-a3d7b8f9ac64"),
                Agent = "Techobank - Balance summary calculation",
                Type = AccountTypes.Bank,
                Balance = 25.24m,
                Currency = CurrencyTypes.BYN.ToString(),
                Description = "Balance calculation test"
            },
            new PaymentAccount
            {
                Key = Guid.Parse("aed5a7ff-cd0f-4c65-b5ab-a3d7b8f9ac07"),
                Agent = "Techobank - For delete tests",
                Type = AccountTypes.Bank,
                Balance = 25.24m,
                Currency = CurrencyTypes.BYN.ToString(),
                Description = "Tech description"
            },
        ];
    }
}

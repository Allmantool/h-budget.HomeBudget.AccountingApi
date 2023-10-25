﻿using System;
using System.Collections.Generic;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Api
{
    internal static class MockStore
    {
        public static readonly List<PaymentAccount> PaymentAccounts = new()
        {
            new()
            {
                Id = Guid.Parse("92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84"),
                Agent = "Priorbank",
                Type = AccountTypes.Bank,
                Balance = 320.24m,
                Currency = CurrencyTypes.BYN.ToString(),
                Description = "Prior description"
            },
            new()
            {
                Id = Guid.Parse("47d84ccf-7f79-4b6b-a691-3c2b313b0905"),
                Agent = "Priorbank",
                Type = AccountTypes.Bank,
                Balance = 20.24m,
                Currency = CurrencyTypes.USD.ToString(),
                Description = "Prior description"
            },
            new()
            {
                Id = Guid.Parse("257f78da-1e0f-4ce7-9c50-b494804a6830"),
                Agent = "Priorbank",
                Type = AccountTypes.Cash,
                Balance = 20.24m,
                Currency = CurrencyTypes.USD.ToString(),
                Description = "Prior description"
            },
            new()
            {
                Id = Guid.Parse("c9b33506-9a98-4f76-ad8e-17c96858305b"),
                Agent = "Priorbank",
                Type = AccountTypes.Loan,
                Balance = 20.24m,
                Currency = CurrencyTypes.USD.ToString(),
                Description = "Prior description"
            },
            new()
            {
                Id = Guid.Parse("852530a6-70b0-4040-8912-8558d59d977a"),
                Agent = "Techobank",
                Type = AccountTypes.Bank,
                Balance = 1320.24m,
                Currency = CurrencyTypes.BYN.ToString(),
                Description = "Tech description"
            }
        };

        public static readonly Dictionary<int, Contractor> Contractors = new()
        {
            {
               new Contractor(new[] { "Work", "GodelTech" }).GetHashCode(),
               new Contractor(new[] { "Work", "GodelTech" })
               {
                   Id = Guid.Parse("728c684e-cc1f-422d-b4e7-eb7e466e5e78")
               }
            },
            {
                new Contractor(new[] { "Mobile Operators", "A1", "+375 29 687 42 43" }).GetHashCode(),
                new Contractor(new[] { "Mobile Operators", "A1", "+375 29 687 42 43" })
                {
                    Id = Guid.Parse("1c0112d1-3310-46d7-b8c3-b248002b9a8c")
                }
            },
            {
                new Contractor(new[] { "Transport", "Taxi" }).GetHashCode(),
                new Contractor(new[] { "Transport", "Taxi" })
                {
                    Id = Guid.Parse("66e81106-9214-41a4-8297-82d6761f1d40")
                }
            },
            {
                new Contractor(new[] { "Glossary", "Dionis" }).GetHashCode(),
                new Contractor(new[] { "Glossary", "Dionis" })
                {
                    Id = Guid.Parse("20275637-3f7b-444e-b08b-0ca612afcd61")
                }
            },
        };
    }
}

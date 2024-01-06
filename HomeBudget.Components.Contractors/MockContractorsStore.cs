using System;
using System.Collections.Generic;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Contractors
{
    public static class MockContractorsStore
    {
        public static readonly List<Contractor> Contractors = new()
        {
            new Contractor(new[] { "Work", "GodelTech" })
            {
                Key = Guid.Parse("728c684e-cc1f-422d-b4e7-eb7e466e5e78")
            },
            new Contractor(new[] { "Mobile Operators", "A1", "+375 29 687 42 43" })
            {
                Key = Guid.Parse("1c0112d1-3310-46d7-b8c3-b248002b9a8c")
            },
            new Contractor(new[] { "Transport", "Taxi" })
            {
                Key = Guid.Parse("66e81106-9214-41a4-8297-82d6761f1d40")
            },
            new Contractor(new[] { "Glossary", "Dionis" })
            {
                Key = Guid.Parse("20275637-3f7b-444e-b08b-0ca612afcd61")
            },
        };
    }
}

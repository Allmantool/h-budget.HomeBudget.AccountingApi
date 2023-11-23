using System.Collections.Generic;
using System;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations
{
    public static class MockOperationsStore
    {
        public static readonly List<PaymentOperation> PaymentOperations = new()
        {
            new PaymentOperation
            {
                Key = Guid.Parse("2adb60a8-6367-4b8b-afa0-4ff7f7b1c92c"),
                Amount = 35.64m,
                PaymentAccountId = Guid.Parse("92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84"),
                CategoryId = Guid.Parse("e9b040ef-6450-46ab-8416-2b146d4da0f0"),
                ContractorId = Guid.Parse("66e81106-9214-41a4-8297-82d6761f1d40"),
                Comment = "Comment Expense",
                OperationDay = new DateOnly(2023, 01, 15)
            },
            new PaymentOperation
            {
                Key = Guid.Parse("20a8ca8e-0127-462c-b854-b2868490f3ec"),
                Amount = 85.64m,
                PaymentAccountId = Guid.Parse("852530a6-70b0-4040-8912-8558d59d977a"),
                CategoryId = Guid.Parse("0eb283e2-fa49-403d-b7ea-8d6326b3b742"),
                ContractorId = Guid.Parse("728c684e-cc1f-422d-b4e7-eb7e466e5e78"),
                Comment = "Comment Income",
                OperationDay = new DateOnly(2023, 01, 16)
            },
            new PaymentOperation
            {
                Key = Guid.Parse("5a53e3d3-0596-4ade-8aff-f3b3b956d0bd"),
                Amount = 115.64m,
                PaymentAccountId = Guid.Parse("c9b33506-9a98-4f76-ad8e-17c96858305b"),
                CategoryId = Guid.Parse("0eb283e2-fa49-403d-b7ea-8d6326b3b742"),
                ContractorId = Guid.Parse("728c684e-cc1f-422d-b4e7-eb7e466e5e78"),
                Comment = "Comment Income",
                OperationDay = new DateOnly(2023, 01, 16)
            }
        };
    }
}

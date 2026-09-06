using System;
using System.Collections.Generic;

using FluentAssertions;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Extensions;

namespace HomeBudget.Components.Operations.Tests.Extensions
{
    [TestFixture]
    public class FinancialTransactionIncrementTests
    {
        [TestCase(1, 23, -23)]
        [TestCase(0, 23, 23)]
        [TestCase(1, -23, -23)]
        [TestCase(0, -23, 23)]
        public void CalculateIncrement_WhenCategorizedPaymentIsProjected_ThenCategoryDeterminesDirection(
            int categoryType,
            decimal amount,
            decimal expectedIncrement)
        {
            var categoryId = Guid.NewGuid();
            var operation = new FinancialTransaction
            {
                Amount = amount,
                CategoryId = categoryId,
                TransactionType = TransactionTypes.Payment
            };
            var categories = new Dictionary<Guid, Category>
            {
                [categoryId] = new Category((CategoryTypes)categoryType, ["test"])
            };

            var increment = operation.CalculateIncrement(categories);

            increment.Should().Be(expectedIncrement);
        }

        [TestCase(-23)]
        [TestCase(23)]
        public void CalculateIncrement_WhenTransferIsProjected_ThenSignedAmountIsRetained(decimal amount)
        {
            var operation = new FinancialTransaction
            {
                Amount = amount,
                TransactionType = TransactionTypes.Transfer
            };

            var increment = operation.CalculateIncrement(new Dictionary<Guid, Category>());

            increment.Should().Be(amount);
        }
    }
}

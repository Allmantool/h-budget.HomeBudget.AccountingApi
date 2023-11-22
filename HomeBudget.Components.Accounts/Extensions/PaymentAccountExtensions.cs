using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Categories;

namespace HomeBudget.Components.Accounts.Extensions
{
    public static class PaymentAccountExtensions
    {
        public static bool SyncBalanceOnCreate(
            this PaymentAccount paymentAccount,
            PaymentOperation operationToAdd)
        {
            var category = MockCategoriesStore.Categories.Find(pa => pa.Key.Equals(operationToAdd.CategoryId));

            if (category == null)
            {
                return false;
            }

            var isIncomeOperation = category.CategoryType == CategoryTypes.Income;

            paymentAccount.Increment(operationToAdd.Amount, isIncomeOperation);

            return true;
        }

        public static bool SyncBalanceOnDelete(
            this PaymentAccount paymentAccount,
            PaymentOperation operationForDelete)
        {
            var category = MockCategoriesStore.Categories.Find(pa => pa.Key.Equals(operationForDelete.CategoryId));

            if (category == null)
            {
                return false;
            }

            var isIncomeOperation = category.CategoryType == CategoryTypes.Income;

            paymentAccount.Decrement(operationForDelete.Amount, isIncomeOperation);

            return true;
        }

        public static bool SyncBalanceOnUpdate(
            this PaymentAccount paymentAccount,
            PaymentOperation originOperation,
            PaymentOperation operationForDelete)
        {
            var category = MockCategoriesStore.Categories.Find(pa => pa.Key.Equals(operationForDelete.CategoryId));

            if (category == null)
            {
                return false;
            }

            var isIncomeOperation = category.CategoryType == CategoryTypes.Income;

            paymentAccount.Update(originOperation.Amount, operationForDelete.Amount, isIncomeOperation);

            return true;
        }

        private static void Increment(this PaymentAccount paymentAccount, decimal value, bool isIncomeOperation)
        {
            paymentAccount.Balance = isIncomeOperation
                ? paymentAccount.Balance + value
                : paymentAccount.Balance - value;
        }

        private static void Decrement(this PaymentAccount paymentAccount, decimal value, bool isIncomeOperation)
        {
            paymentAccount.Balance = isIncomeOperation
                ? paymentAccount.Balance - value
                : paymentAccount.Balance + value;
        }

        private static void Update(
            this PaymentAccount paymentAccount,
            decimal originValue,
            decimal newValue,
            bool isIncomeOperation)
        {
            paymentAccount.Decrement(originValue, isIncomeOperation);
            paymentAccount.Increment(newValue, isIncomeOperation);
        }
    }
}
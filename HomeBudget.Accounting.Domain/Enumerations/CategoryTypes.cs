namespace HomeBudget.Accounting.Domain.Enumerations
{
    public class CategoryTypes(int key, string name)
        : BaseEnumeration<CategoryTypes, int>(key, name)
    {
        public static readonly CategoryTypes Income = new(0, nameof(Income));
        public static readonly CategoryTypes Expense = new(1, nameof(Expense));

        public static implicit operator CategoryTypes(int categoryId) => FromValue(categoryId);
    }
}

namespace HomeBudget.Accounting.Domain.Enumerations
{
    public class CategoryTypes(int id, string name)
        : BaseEnumeration(id, name)
    {
        public static readonly CategoryTypes Income = new(0, nameof(Income));
        public static readonly CategoryTypes Expense = new(1, nameof(Expense));
    }
}

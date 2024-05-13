namespace HomeBudget.Accounting.Domain.Enumerations
{
    public class TransactionTypes(int id, string name)
        : BaseEnumeration(id, name)
    {
        public static readonly TransactionTypes Payment = new(1, nameof(Payment));
        public static readonly TransactionTypes Transfer = new(2, nameof(Transfer));
    }
}

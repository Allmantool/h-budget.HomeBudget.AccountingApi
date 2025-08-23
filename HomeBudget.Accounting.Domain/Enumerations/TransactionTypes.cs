namespace HomeBudget.Accounting.Domain.Enumerations
{
    public class TransactionTypes(int key, string name)
        : BaseEnumeration<TransactionTypes, int>(key, name)
    {
        public static readonly TransactionTypes None = new(0, nameof(None));
        public static readonly TransactionTypes Payment = new(1, nameof(Payment));
        public static readonly TransactionTypes Transfer = new(2, nameof(Transfer));

        public static implicit operator TransactionTypes(int transactionId) => FromValue(transactionId);
    }
}

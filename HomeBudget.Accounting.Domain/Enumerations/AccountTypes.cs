namespace HomeBudget.Accounting.Domain.Enumerations
{
    public class AccountTypes(int key, string name)
        : BaseEnumeration<AccountTypes, int>(key, name)
    {
        public static readonly AccountTypes Cash = new(0, nameof(Cash));
        public static readonly AccountTypes Virtual = new(1, nameof(Virtual));
        public static readonly AccountTypes Loan = new(2, nameof(Loan));
        public static readonly AccountTypes Deposit = new(3, nameof(Deposit));

        public static implicit operator AccountTypes(int accountId) => FromValue(accountId);
    }
}

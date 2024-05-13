namespace HomeBudget.Accounting.Domain.Enumerations
{
    public class CurrencyTypes(int id, string name)
        : BaseEnumeration(id, name)
    {
        public static readonly CurrencyTypes Byn = new(70, nameof(Byn));
        public static readonly CurrencyTypes Usd = new(10, nameof(Usd));
        public static readonly CurrencyTypes Rub = new(20, nameof(Rub));
        public static readonly CurrencyTypes Pln = new(30, nameof(Pln));
        public static readonly CurrencyTypes Eur = new(40, nameof(Eur));
        public static readonly CurrencyTypes Uan = new(50, nameof(Uan));
        public static readonly CurrencyTypes Try = new(60, nameof(Try));
    }
}

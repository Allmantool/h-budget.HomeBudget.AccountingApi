namespace HomeBudget.Accounting.Infrastructure.Factories
{
    public static class HeaderFactory
    {
        public static HeaderBuilder Create() => new HeaderBuilder();

        public static HeaderBuilder With(string key, string value) =>
            Create().With(key, value);

        public static HeaderBuilder With(string key, byte[] value) =>
            Create().With(key, value);
    }
}

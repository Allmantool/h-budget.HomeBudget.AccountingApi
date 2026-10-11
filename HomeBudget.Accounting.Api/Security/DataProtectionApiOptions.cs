namespace HomeBudget.Accounting.Api.Security
{
    public sealed class DataProtectionApiOptions
    {
        public const string SectionName = "DataProtectionApi";

        public bool Enabled { get; set; }
        public string Authority { get; set; }
        public string Audience { get; set; }
        public string AdministratorClaimType { get; set; }
        public string AdministratorClaimValue { get; set; }
        public string RequiredScope { get; set; }
        public int RecentAuthenticationMinutes { get; set; } = 15;
        public int ConfirmationLifetimeMinutes { get; set; } = 5;
        public int ClockSkewSeconds { get; set; } = 60;
        public bool RequireHttpsMetadata { get; set; } = true;
    }
}

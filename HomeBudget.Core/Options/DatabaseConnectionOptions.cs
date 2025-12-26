namespace HomeBudget.Core.Options
{
    public record DatabaseConnectionOptions
    {
        public string ConnectionString { get; set; }
        public int SqlWriteCommandTimeoutSeconds { get; set; } = 1800;
        public int SqlReadCommandTimeoutSeconds { get; set; } = 1800;

        public string RedisConnectionString { get; set; }
    }
}

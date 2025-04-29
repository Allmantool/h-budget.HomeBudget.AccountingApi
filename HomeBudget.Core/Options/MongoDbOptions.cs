namespace HomeBudget.Core.Options
{
    public record MongoDbOptions
    {
        public string LedgerDatabase { get; set; }
        public string ConnectionString { get; set; }
        public string PaymentsHistory { get; set; }
        public string HandBooks { get; set; }
        public string PaymentAccounts { get; set; }
        public int MaxConnectionPoolSize { get; set; } = 300;
        public int MinConnectionPoolSize { get; set; } = 50;
        public long ConnectTimeoutInMinutes { get; set; } = 5;
        public long HeartbeatTimeoutInSeconds { get; set; } = 15;
        public long MaxConnectionIdleTimeInMinutes { get; set; } = 10;
        public long MaxConnectionLifeTimeInMinutes { get; set; } = 45;
        public long ServerSelectionTimeoutInSeconds { get; set; } = 45;
        public long SocketTimeoutInSeconds { get; set; } = 90;
        public long WaitQueueTimeoutInSeconds { get; set; } = 15;
    }
}

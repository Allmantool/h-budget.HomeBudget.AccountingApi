﻿namespace HomeBudget.Core.Options
{
    public record MongoDbOptions
    {
        public string ConnectionString { get; set; }
        public string PaymentsHistoryDatabaseName { get; set; }
        public string HandBooksDatabaseName { get; set; }
        public string PaymentAccountsDatabaseName { get; set; }
    }
}

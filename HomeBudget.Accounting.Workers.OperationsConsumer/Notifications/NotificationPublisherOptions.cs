namespace HomeBudget.Accounting.Workers.OperationsConsumer.Notifications
{
    internal sealed class NotificationPublisherOptions
    {
        public const string SectionName = "NotificationPublisherOptions";

        public string AccountingApiBaseUrl { get; set; } = "http://homebudget-accounting-api";
    }
}

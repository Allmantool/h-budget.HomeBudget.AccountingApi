namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public sealed class EventStoreSubscriptionContext
    {
        private int _settled;

        public string StreamId { get; init; }
        public string Revision { get; init; }
        public string Position { get; init; }

        internal System.Func<System.Threading.Tasks.Task> Acknowledge { get; init; }
        internal System.Func<string, System.Threading.Tasks.Task> Retry { get; init; }

        public System.Threading.Tasks.Task AcknowledgeAsync() => SettleAsync(Acknowledge);

        public System.Threading.Tasks.Task RetryAsync(string reason) =>
            Retry is null ? System.Threading.Tasks.Task.CompletedTask : SettleAsync(() => Retry(reason));

        private async System.Threading.Tasks.Task SettleAsync(System.Func<System.Threading.Tasks.Task> settle)
        {
            if (settle is null || System.Threading.Interlocked.Exchange(ref _settled, 1) != 0)
            {
                return;
            }

            try
            {
                await settle();
            }
            catch
            {
                System.Threading.Volatile.Write(ref _settled, 0);
                throw;
            }
        }
    }
}


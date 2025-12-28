namespace HomeBudget.Accounting.Domain.Enumerations
{
    public class OutboxStatus(byte key, string name)
        : BaseEnumeration<OutboxStatus, byte>(key, name)
    {
        public static readonly OutboxStatus Pending = new(0, nameof(Pending));
        public static readonly OutboxStatus Published = new(1, nameof(Published));
        public static readonly OutboxStatus Acknowledged = new(2, nameof(Acknowledged));
        public static readonly OutboxStatus Retrying = new(3, nameof(Retrying));
        public static readonly OutboxStatus Failed = new(4, nameof(Failed));
        public static readonly OutboxStatus DeadLettered = new(5, nameof(DeadLettered));

        public static implicit operator OutboxStatus(byte statusId) => FromValue(statusId);

        public static OutboxStatus ToOutboxStatus()
        {
            throw new System.NotImplementedException();
        }

        public bool IsFinal => this == Failed || this == DeadLettered;

        public bool CanRetry => this == Pending || this == Retrying || this == Failed;
    }
}

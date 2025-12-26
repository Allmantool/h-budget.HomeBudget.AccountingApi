namespace HomeBudget.Accounting.Domain.Enumerations
{
    public class OutboxStatuses(byte key, string name)
        : BaseEnumeration<OutboxStatuses, byte>(key, name)
    {
        public static readonly OutboxStatuses Pending = new(0, nameof(Pending));
        public static readonly OutboxStatuses Published = new(1, nameof(Published));
        public static readonly OutboxStatuses Processed = new(2, nameof(Processed));
        public static readonly OutboxStatuses Failed = new(3, nameof(Failed));

        public static implicit operator OutboxStatuses(byte statusId) => FromValue(statusId);

        public OutboxStatuses ToOutboxStatuses()
        {
            throw new System.NotImplementedException();
        }
    }
}

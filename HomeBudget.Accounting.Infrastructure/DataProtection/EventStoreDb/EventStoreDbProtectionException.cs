using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb
{
    public sealed class EventStoreDbProtectionException : Exception
    {
        public EventStoreDbProtectionException()
            : this("eventstoredb-protection-error", "EventStoreDB protection failed.")
        {
        }

        public EventStoreDbProtectionException(string message)
            : this("eventstoredb-protection-error", message)
        {
        }

        public EventStoreDbProtectionException(string message, Exception innerException)
            : base(message, innerException)
        {
            Code = "eventstoredb-protection-error";
        }

        public EventStoreDbProtectionException(string code, string safeMessage)
            : base(safeMessage)
        {
            Code = code;
        }

        public string Code { get; }
    }
}

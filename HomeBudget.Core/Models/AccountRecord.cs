using System;

namespace HomeBudget.Core.Models
{
    public record AccountRecord
    {
        private Guid accountId;

        public AccountRecord(Guid accountId)
        {
            Id = accountId;
        }

        public Guid Id
        {
            get => accountId;
            set => accountId = value;
        }
    }
}

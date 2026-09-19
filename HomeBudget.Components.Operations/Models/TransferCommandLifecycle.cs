using System;

namespace HomeBudget.Components.Operations.Models
{
    public static class TransferCommandLifecycle
    {
        private const byte Published = 1;
        private const byte Failed = 5;
        private const byte Persisted = 6;
        private const byte Projected = 7;

        public static PaymentCommandStatus Evaluate(TransferCommandRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (record.SenderStatus == Failed || record.RecipientStatus == Failed)
            {
                return PaymentCommandStatus.Failed;
            }

            if (record.SenderStatus >= Projected && record.RecipientStatus >= Projected)
            {
                return PaymentCommandStatus.Projected;
            }

            if (record.SenderStatus >= Persisted && record.RecipientStatus >= Persisted)
            {
                return PaymentCommandStatus.Persisted;
            }

            return record.SenderStatus >= Published && record.RecipientStatus >= Published
                ? PaymentCommandStatus.Published
                : PaymentCommandStatus.Accepted;
        }
    }
}

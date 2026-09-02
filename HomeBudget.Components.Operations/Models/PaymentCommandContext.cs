namespace HomeBudget.Components.Operations.Models
{
    public sealed class PaymentCommandContext
    {
        public string IdempotencyKeyHash { get; init; }

        public string RequestFingerprint { get; init; }

        public string CommandType { get; init; }

        public string CommandId { get; private set; }

        public bool WasAlreadyAccepted { get; private set; }

        internal void SetRegistration(string commandId, bool wasAlreadyAccepted)
        {
            CommandId = commandId;
            WasAlreadyAccepted = wasAlreadyAccepted;
        }
    }
}

namespace HomeBudget.Components.Operations.Models
{
    public sealed record IdempotencyPreflight
    {
        public PaymentCommandContext Context { get; init; }
        public PaymentCommandRecord ExistingCommand { get; init; }
        public bool IsConflict { get; init; }
    }
}

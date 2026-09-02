namespace HomeBudget.Components.Operations.Models
{
    internal interface IIdempotentPaymentCommand
    {
        PaymentCommandContext CommandContext { get; }
    }
}

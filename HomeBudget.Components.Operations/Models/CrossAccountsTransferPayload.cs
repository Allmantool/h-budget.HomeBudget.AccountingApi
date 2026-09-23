using System;

namespace HomeBudget.Components.Operations.Models
{
    public record CrossAccountsTransferPayload
    {
        public Guid Sender { get; set; }
        public Guid Recipient { get; set; }
        public decimal Amount { get; set; }
        public decimal Multiplier { get; set; }
        public decimal? CustomConversionMultiplier { get; set; }
        public decimal? SenderAmount { get; set; }
        public decimal? RecipientAmount { get; set; }
        public string SenderCurrency { get; set; }
        public string RecipientCurrency { get; set; }
        public string SourceReference { get; set; }
        public DateOnly OperationAt { get; set; }
    }
}

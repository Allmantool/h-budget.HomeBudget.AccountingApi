namespace HomeBudget.Components.Transfers.Models
{
    public class CrossAccountsTransferPayload
    {
        public Guid Sender { get; set; }
        public Guid Recipient { get; set; }
        public decimal Amount { get; set; }
        public DateTime OperationAt { get; set; }
    }
}

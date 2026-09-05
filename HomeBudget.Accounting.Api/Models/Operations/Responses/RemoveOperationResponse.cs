using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Accounting.Api.Models.Operations.Responses
{
    public sealed record RemoveOperationResponse
    {
        public string PaymentAccountId { get; set; }
        public string PaymentOperationId { get; set; }
        public string CommandId { get; set; }
        public string Status { get; set; }
        public bool IsDuplicate { get; set; }

        public static RemoveOperationResponse From(
            string paymentAccountId,
            PaymentCommandRecord command,
            bool isDuplicate)
        {
            return new RemoveOperationResponse
            {
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = command.PaymentOperationId.ToString(),
                CommandId = command.CommandId,
                Status = command.Status.ToString(),
                IsDuplicate = isDuplicate
            };
        }
    }
}

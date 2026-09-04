using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Accounting.Api.Models.Operations.Responses
{
    public sealed record CreateOperationResponse
    {
        public string PaymentAccountId { get; set; }
        public string PaymentOperationId { get; set; }
        public string CommandId { get; set; }
        public string Status { get; set; }
        public bool IsDuplicate { get; set; }

        public static CreateOperationResponse From(
            string paymentAccountId,
            PaymentCommandRecord command,
            bool isDuplicate)
        {
            return new CreateOperationResponse
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

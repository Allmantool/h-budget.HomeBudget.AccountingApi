using MongoDB.Bson;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations.Tests.Models
{
    internal class PaymentHistoryDocument : PaymentOperationHistoryRecord
    {
        public ObjectId Id { get; set; }
    }
}

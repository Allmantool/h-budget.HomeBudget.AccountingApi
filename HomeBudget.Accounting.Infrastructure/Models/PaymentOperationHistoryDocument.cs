using MongoDB.Bson;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Infrastructure.Models
{
    public class PaymentHistoryDocument : PaymentOperationHistoryRecord
    {
        public ObjectId Id { get; set; }
    }
}

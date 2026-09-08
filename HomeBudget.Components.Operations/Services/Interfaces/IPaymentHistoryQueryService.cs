using System.Collections.Generic;

using HomeBudget.Components.Categories.Models;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    public interface IPaymentHistoryQueryService
    {
        PaymentHistoryQuery CreateQuery(
            PaymentHistoryQueryPayload request,
            IReadOnlyCollection<CategoryDocument> categories);
    }
}

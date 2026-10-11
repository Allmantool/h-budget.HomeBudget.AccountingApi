using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure;

namespace HomeBudget.Components.Categories.Models
{
    public sealed class CategoryDocument : DocumentEntity<Category>
    {
        public string LedgerId { get; init; } = "default";
        public string LedgerGenerationId { get; init; } = "legacy";
    }
}

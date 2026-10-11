using System.Text.Json;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed record PortabilityImportRecord(
        string EntityType,
        string SourceEntityId,
        string Fingerprint,
        string IdempotencyKey,
        string EntryPath,
        JsonElement Payload);
}

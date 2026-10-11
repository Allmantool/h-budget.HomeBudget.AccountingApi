namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Export
{
    internal sealed record PortabilityExportArtifact(
        string Path,
        string MediaType,
        string Schema,
        long? RecordCount,
        long ByteSize,
        string Sha256);
}

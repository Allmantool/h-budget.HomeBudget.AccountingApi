using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed class PortabilityImportValidationException : InvalidOperationException
    {
        public PortabilityImportValidationException()
            : this("import.invalid")
        {
        }

        public PortabilityImportValidationException(string code)
            : this(code, null, null, null, null)
        {
        }

        public PortabilityImportValidationException(string code, Exception innerException)
            : this(code, null, null, null, innerException)
        {
        }

        public PortabilityImportValidationException(
            string code,
            string entryPath = null,
            string entityId = null,
            string fieldPath = null,
            Exception innerException = null)
            : base($"Portability import rejected ({code}).", innerException)
        {
            Code = code;
            EntryPath = entryPath;
            EntityId = entityId;
            FieldPath = fieldPath;
        }

        public string Code { get; }

        public string EntryPath { get; }

        public string EntityId { get; }

        public string FieldPath { get; }
    }
}

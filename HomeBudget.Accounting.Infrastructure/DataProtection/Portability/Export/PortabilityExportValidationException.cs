using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Export
{
    public sealed class PortabilityExportValidationException : InvalidOperationException
    {
        public PortabilityExportValidationException()
            : this("export.invalid")
        {
        }

        public PortabilityExportValidationException(string code)
            : this(code, null)
        {
        }

        public PortabilityExportValidationException(string code, Exception innerException = null)
            : base($"Portability export rejected ({code}).", innerException)
        {
            Code = code;
        }

        public string Code { get; }
    }
}

using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    internal sealed class PortabilityImportJournalConflictException : InvalidOperationException
    {
        public PortabilityImportJournalConflictException()
            : this("import.journal-conflict")
        {
        }

        public PortabilityImportJournalConflictException(string code)
            : base($"Portability import journal conflict ({code}).")
        {
            Code = code;
        }

        public PortabilityImportJournalConflictException(string code, Exception innerException)
            : base($"Portability import journal conflict ({code}).", innerException)
        {
            Code = code;
        }

        public string Code { get; }
    }
}

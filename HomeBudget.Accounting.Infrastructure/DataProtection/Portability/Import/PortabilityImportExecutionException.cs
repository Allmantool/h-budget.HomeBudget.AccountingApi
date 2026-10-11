using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed class PortabilityImportExecutionException : InvalidOperationException
    {
        public PortabilityImportExecutionException()
            : this("Portability import execution failed.")
        {
        }

        public PortabilityImportExecutionException(string message)
            : base(message)
        {
            Code = "import.execution-failed";
        }

        public PortabilityImportExecutionException(string message, Exception innerException)
            : base(message, innerException)
        {
            Code = "import.execution-failed";
        }

        private PortabilityImportExecutionException(
            string code,
            string entryPath,
            string entityId,
            Exception innerException)
            : base($"Portability import execution failed ({code}).", innerException)
        {
            Code = code;
            EntryPath = entryPath;
            EntityId = entityId;
        }

        public string Code { get; }

        public string EntryPath { get; }

        public string EntityId { get; }

        internal static PortabilityImportExecutionException Create(
            string code,
            string entryPath = null,
            string entityId = null,
            Exception innerException = null) => new(code, entryPath, entityId, innerException);
    }
}

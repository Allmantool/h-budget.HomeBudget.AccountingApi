using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability
{
    public sealed class PortabilityPackageValidationException : Exception
    {
        public PortabilityPackageValidationException()
            : base("Portability package validation failed (archive.invalid).")
        {
            Code = "archive.invalid";
        }

        public PortabilityPackageValidationException(string message)
            : base(message)
        {
            Code = "archive.invalid";
        }

        public PortabilityPackageValidationException(string message, Exception innerException)
            : base(message, innerException)
        {
            Code = "archive.invalid";
        }

        private PortabilityPackageValidationException(
            string code,
            string entryPath,
            long? recordNumber,
            string fieldPath,
            Exception innerException)
            : base(BuildMessage(code, entryPath, recordNumber, fieldPath), innerException)
        {
            Code = code;
            EntryPath = entryPath;
            RecordNumber = recordNumber;
            FieldPath = fieldPath;
        }

        public string Code { get; }

        public string EntryPath { get; }

        public long? RecordNumber { get; }

        public string FieldPath { get; }

        internal static PortabilityPackageValidationException Create(
            string code,
            string entryPath = null,
            long? recordNumber = null,
            string fieldPath = null,
            Exception innerException = null) =>
            new(code, entryPath, recordNumber, fieldPath, innerException);

        private static string BuildMessage(
            string code,
            string entryPath,
            long? recordNumber,
            string fieldPath)
        {
            var location = entryPath == null ? string.Empty : $" Entry '{entryPath}'.";
            var record = recordNumber == null ? string.Empty : $" Record {recordNumber.Value}.";
            var field = fieldPath == null ? string.Empty : $" Field '{fieldPath}'.";
            return $"Portability package validation failed ({code}).{location}{record}{field}";
        }
    }
}

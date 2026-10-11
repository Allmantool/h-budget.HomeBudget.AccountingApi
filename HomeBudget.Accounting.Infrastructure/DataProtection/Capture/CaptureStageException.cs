using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    public sealed class CaptureStageException : Exception
    {
        public CaptureStageException()
            : this("capture.unclassified")
        {
        }

        public CaptureStageException(string classification)
            : base(classification)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(classification);
            Classification = classification;
        }

        public CaptureStageException(string classification, Exception innerException)
            : base(classification, innerException)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(classification);
            Classification = classification;
        }

        public string Classification { get; }
    }
}

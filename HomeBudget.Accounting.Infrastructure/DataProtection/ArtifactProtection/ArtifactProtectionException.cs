using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Artifacts
{
    public sealed class ArtifactProtectionException : Exception
    {
        public ArtifactProtectionException()
        {
        }

        public ArtifactProtectionException(string message)
            : base(message)
        {
        }

        public ArtifactProtectionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public ArtifactProtectionException(string code, string safeMessage)
            : base(safeMessage)
        {
            Code = code;
        }

        public string Code { get; }
    }
}

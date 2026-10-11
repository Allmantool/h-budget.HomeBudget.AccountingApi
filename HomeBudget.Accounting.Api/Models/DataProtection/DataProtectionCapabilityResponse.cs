using System.Collections.Generic;

namespace HomeBudget.Accounting.Api.Models.DataProtection
{
    public sealed class DataProtectionCapabilityResponse
    {
        public bool Enabled { get; set; }
        public bool AuthorizationConfigured { get; set; }
        public string Status { get; set; }
        public IReadOnlyCollection<string> RecognizedOperationKinds { get; set; }
        public IReadOnlyCollection<string> ExecutableOperationKinds { get; set; }
        public IReadOnlyCollection<string> MissingPrerequisites { get; set; }
        public bool SupportsPolling { get; set; }
        public bool SupportsServerSentEvents { get; set; }
    }
}

using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Export
{
    public sealed class PortabilityExportRequest
    {
        public Guid ExportId { get; set; }

        public string EnvironmentId { get; set; }

        public string LedgerId { get; set; }

        public string LedgerGenerationId { get; set; }

        public long LedgerGenerationNumber { get; set; }

        public string Completeness { get; set; }

        public IReadOnlyList<string> SelectedOperationIds { get; set; } = [];

        public DateTimeOffset RequestedCutoffUtc { get; set; }

        public string DestinationPath { get; set; }
    }
}

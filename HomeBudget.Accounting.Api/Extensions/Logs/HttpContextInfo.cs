using Microsoft.Extensions.Primitives;

namespace HomeBudget.Accounting.Api.Extensions.Logs
{
    internal record HttpContextInfo
    {
        public string IpAddress { get; set; }
        public string Host { get; set; }
        public string Protocol { get; set; }
        public string Scheme { get; set; }
        public string User { get; set; }
        public StringValues CorrelationIdFromRequest { get; set; }
        public StringValues CorrelationIdFromResponse { get; set; }
    }
}

using System;
using Microsoft.Extensions.Logging;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Logs
{
    internal static partial class PaymentOperationsBatchProcessorBackgroundServiceLogs
    {
        [LoggerMessage(
            EventId = 3001,
            Level = LogLevel.Error,
            Message = "{Service} failed to process batch")]
        public static partial void OperationDeliveryError(this ILogger logger, string service, string messageError, Exception ex);
    }
}

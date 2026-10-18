using System;

namespace HomeBudget.Accounting.Api.Tests.Portability.Import
{
    internal sealed class SimulatedImportProcessCrashException : Exception
    {
        public SimulatedImportProcessCrashException()
        {
        }

        public SimulatedImportProcessCrashException(string message)
            : base(message)
        {
        }

        public SimulatedImportProcessCrashException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}

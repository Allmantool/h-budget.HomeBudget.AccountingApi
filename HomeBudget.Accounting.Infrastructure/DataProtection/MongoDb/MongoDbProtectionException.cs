using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb
{
    public sealed class MongoDbProtectionException : Exception
    {
        public MongoDbProtectionException()
            : this("mongodb-protection-error", "MongoDB protection failed.")
        {
        }

        public MongoDbProtectionException(string message)
            : this("mongodb-protection-error", message)
        {
        }

        public MongoDbProtectionException(string message, Exception innerException)
            : base(message, innerException)
        {
            Code = "mongodb-protection-error";
        }

        public MongoDbProtectionException(string code, string safeMessage)
            : base(safeMessage)
        {
            Code = code;
        }

        public string Code { get; }
    }
}

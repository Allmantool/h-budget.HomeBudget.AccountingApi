using System;
using System.Net;

namespace HomeBudget.Accounting.Api.Filters
{
    internal static class ApiErrorStatusCodeClassifier
    {
        public static int FromFailureMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return (int)HttpStatusCode.ServiceUnavailable;
            }

            if (Contains(message, "already exists")
                || Contains(message, "duplicate"))
            {
                return (int)HttpStatusCode.Conflict;
            }

            if (Contains(message, "hasn't been found")
                || Contains(message, "not found")
                || Contains(message, "doesn't exist")
                || Contains(message, " is null"))
            {
                return (int)HttpStatusCode.NotFound;
            }

            if (Contains(message, "validation failed")
                || Contains(message, "invalid")
                || Contains(message, "has been provided")
                || Contains(message, "must")
                || Contains(message, "required"))
            {
                return (int)HttpStatusCode.BadRequest;
            }

            return (int)HttpStatusCode.InternalServerError;
        }

        public static int FromException(Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                return 499;
            }

            return IsInfrastructureFailure(exception)
                ? (int)HttpStatusCode.ServiceUnavailable
                : (int)HttpStatusCode.InternalServerError;
        }

        public static string MessageForException(Exception exception)
        {
            return IsInfrastructureFailure(exception)
                ? "Infrastructure dependency is unavailable"
                : "An unexpected error occurred";
        }

        private static bool IsInfrastructureFailure(Exception exception)
        {
            if (exception is TimeoutException)
            {
                return true;
            }

            var typeName = exception.GetType().FullName ?? string.Empty;

            return typeName.StartsWith("Confluent.Kafka.", StringComparison.Ordinal)
                || typeName.StartsWith("EventStore.Client.", StringComparison.Ordinal)
                || typeName.StartsWith("Grpc.Core.", StringComparison.Ordinal)
                || typeName.StartsWith("Microsoft.Data.SqlClient.", StringComparison.Ordinal)
                || typeName.StartsWith("MongoDB.", StringComparison.Ordinal)
                || (exception.InnerException != null && IsInfrastructureFailure(exception.InnerException));
        }

        private static bool Contains(string source, string value)
        {
            return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}

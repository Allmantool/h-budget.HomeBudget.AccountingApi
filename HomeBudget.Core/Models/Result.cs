namespace HomeBudget.Core.Models
{
    public class Result<T>(
        T payload,
        string statusMessage,
        bool isSucceeded)
    {
        public T Payload { get; private set; } = payload;
        public bool IsSucceeded { get; private set; } = isSucceeded;
        public string StatusMessage { get; private set; } = statusMessage;

        public static Result<T> Succeeded(T payload) => new(payload, null, true);
        public static Result<T> Failure(string errorMessage = default) => new(default, errorMessage, false);
    }
}

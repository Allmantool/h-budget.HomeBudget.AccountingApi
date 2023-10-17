namespace HomeBudget.Accounting.Domain.Models
{
    public class Result<T>
    {
        public Result(T payload = default, string message = default, bool isSucceeded = default)
        {
            Payload = payload;
            IsSucceeded = isSucceeded;
            Message = message;
        }

        public T Payload { get; }
        public bool IsSucceeded { get; }
        public string Message { get; }
    }
}

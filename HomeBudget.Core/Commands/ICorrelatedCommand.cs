namespace HomeBudget.Core.Commands
{
    public interface ICorrelatedCommand
    {
        string CorrelationId { get; set; }
    }
}

using EventStore.Client;

namespace HomeBudget.Components.Operations.Models
{
    internal sealed class PaymentStreamState
    {
        public PaymentStreamState(
            StreamRevision? expectedRevision,
            IWriteResult duplicateResult)
        {
            ExpectedRevision = expectedRevision;
            DuplicateResult = duplicateResult;
        }

        public StreamRevision? ExpectedRevision { get; }

        public IWriteResult DuplicateResult { get; }

        public static PaymentStreamState Empty { get; } = new(null, null);

        public static PaymentStreamState Duplicate(IWriteResult result) => new(result.NextExpectedStreamRevision, result);
    }
}

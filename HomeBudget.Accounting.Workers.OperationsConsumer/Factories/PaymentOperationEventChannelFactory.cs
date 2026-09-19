using System.Threading.Channels;

using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Observability;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Factories
{
    internal static class PaymentOperationEventChannelFactory
    {
        public static Channel<ActivityEnvelope<PaymentOperationEvent>> CreateBufferChannel(EventStoreDbOptions opts)
        {
            return CreateBufferChannel<ActivityEnvelope<PaymentOperationEvent>>(opts);
        }

        public static Channel<HomeBudget.Accounting.Workers.OperationsConsumer.Clients.ProjectionBatchContext> CreateProjectionBufferChannel(EventStoreDbOptions opts)
        {
            return CreateBufferChannel<HomeBudget.Accounting.Workers.OperationsConsumer.Clients.ProjectionBatchContext>(opts);
        }

        private static Channel<T> CreateBufferChannel<T>(EventStoreDbOptions opts)
        {
            var capacity = opts.ChannelCapacity > 0 ? opts.ChannelCapacity : 10000;
            var boundedOptions = new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            };

            return Channel.CreateBounded<T>(boundedOptions);
        }
    }
}

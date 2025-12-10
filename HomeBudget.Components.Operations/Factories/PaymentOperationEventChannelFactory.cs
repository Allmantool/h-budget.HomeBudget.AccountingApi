using System.Threading.Channels;

using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Factories
{
    internal static class PaymentOperationEventChannelFactory
    {
        public static Channel<PaymentOperationEvent> CreateBufferChannel(EventStoreDbOptions opts)
        {
            var capacity = opts.ChannelCapacity > 0 ? opts.ChannelCapacity : 10000;
            var boundedOptions = new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            };

            return Channel.CreateBounded<PaymentOperationEvent>(boundedOptions);
        }
    }
}

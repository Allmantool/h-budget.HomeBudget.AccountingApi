using HomeBudget.Core.Models;

namespace HomeBudget.Core.Options
{
    public class KafkaOptions
    {
        public ProducerSettings ProducerSettings { get; set; }
        public Topics Topics { get; init; }
    }
}

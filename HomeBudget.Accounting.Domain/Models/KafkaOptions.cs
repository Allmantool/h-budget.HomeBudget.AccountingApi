namespace HomeBudget.Accounting.Domain.Models
{
    public class KafkaOptions
    {
        public ProducerSettings ProducerSettings { get; set; }
        public Topics Topics { get; set; }
    }
}

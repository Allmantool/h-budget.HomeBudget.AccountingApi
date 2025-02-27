﻿using HomeBudget.Core.Models;

namespace HomeBudget.Core.Options
{
    public record KafkaOptions
    {
        public AdminSettings AdminSettings { get; set; }
        public ProducerSettings ProducerSettings { get; set; }
        public ConsumerSettings ConsumerSettings { get; set; }
        public Topics Topics { get; set; }
    }
}

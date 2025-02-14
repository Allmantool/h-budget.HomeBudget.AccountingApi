﻿using System;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.Services
{
    public interface IAdminKafkaService : IDisposable
    {
        Task CreateTopicAsync(string topicName, CancellationToken stoppingToken);
    }
}

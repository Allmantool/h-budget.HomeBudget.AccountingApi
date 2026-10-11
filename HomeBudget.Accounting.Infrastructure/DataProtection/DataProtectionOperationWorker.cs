using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    internal sealed class DataProtectionOperationWorker : BackgroundService
    {
        private static readonly Action<ILogger, Exception> LogProcessingFailure =
            LoggerMessage.Define(
                LogLevel.Error,
                new EventId(2001, nameof(DataProtectionOperationWorker)),
                "Data-protection operation processing failed; the durable operation remains available for inspection.");

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DataProtectionWorkerOptions _options;
        private readonly ILogger<DataProtectionOperationWorker> _logger;
        private readonly string _ownerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

        public DataProtectionOperationWorker(
            IServiceScopeFactory scopeFactory,
            IOptions<DataProtectionWorkerOptions> options,
            ILogger<DataProtectionOperationWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.IsEnabled)
            {
                return;
            }

            var pollInterval = TimeSpan.FromMilliseconds(_options.PollIntervalMilliseconds);
            var leaseDuration = TimeSpan.FromSeconds(_options.LeaseDurationSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<DataProtectionOperationProcessor>();
                    var processed = await processor.ProcessNextAsync(_ownerId, leaseDuration, stoppingToken);
                    if (processed is null)
                    {
                        await Task.Delay(pollInterval, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    await DelayAfterFailureAsync(exception, pollInterval, stoppingToken);
                }
            }
        }

        private async Task DelayAfterFailureAsync(
            Exception exception,
            TimeSpan pollInterval,
            CancellationToken stoppingToken)
        {
            LogProcessingFailure(_logger, exception);
            await Task.Delay(pollInterval, stoppingToken);
        }
    }
}

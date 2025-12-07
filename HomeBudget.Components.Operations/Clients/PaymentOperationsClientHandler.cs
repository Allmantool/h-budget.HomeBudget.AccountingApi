using System;
using System.Globalization;
using Confluent.Kafka;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Operations.Logs;
using HomeBudget.Core.Options;

internal sealed class PaymentOperationsClientHandler : IKafkaClientHandler
{
    private readonly ILogger<PaymentOperationsClientHandler> _logger;
    private readonly IProducer<byte[], byte[]> _kafkaProducer;

    public PaymentOperationsClientHandler(
        IOptions<KafkaOptions> options,
        ILogger<PaymentOperationsClientHandler> logger)
    {
        _logger = logger;
        var kafkaOptions = options.Value;
        var producerSettings = kafkaOptions.ProducerSettings;

        var producerConfig = new ProducerConfig
        {
            ClientId = $"{Environment.MachineName}-PaymentOperations-{Guid.NewGuid():N}",
            BootstrapServers = producerSettings.BootstrapServers,

            MessageTimeoutMs = producerSettings.MessageTimeoutMs,
            RequestTimeoutMs = producerSettings.RequestTimeoutMs,
            SocketTimeoutMs = producerSettings.SocketTimeoutMs,

            QueueBufferingMaxKbytes = producerSettings.QueueBufferingMaxKbytes,
            QueueBufferingMaxMessages = producerSettings.QueueBufferingMaxMessages,
            BatchSize = producerSettings.BatchSize,
            LingerMs = producerSettings.LingerMs,
            CompressionType = GetCompressionType(producerSettings.CompressionType),

            RetryBackoffMs = producerSettings.RetryBackoffMs,

            Acks = Acks.Leader,
            EnableIdempotence = false,
            MaxInFlight = producerSettings.MaxInFlight,

            SocketSendBufferBytes = producerSettings.SocketSendBufferBytes,
            SocketReceiveBufferBytes = producerSettings.SocketReceiveBufferBytes,
            SocketKeepaliveEnable = true,

            StatisticsIntervalMs = producerSettings.StatisticsIntervalMs,
            EnableDeliveryReports = true,
            EnableBackgroundPoll = true,
        };

        var producerBuilder = new ProducerBuilder<byte[], byte[]>(producerConfig);

        producerBuilder.SetErrorHandler((_, error) =>
        {
            _logger.ProducerError(
                error.Code,
                error.Reason);
        });

        producerBuilder.SetStatisticsHandler((_, stats) =>
        {
            _logger.ProducerStats(stats);
        });

        _kafkaProducer = producerBuilder.Build();
    }

    private static CompressionType GetCompressionType(string compressionType)
    {
        return compressionType?.ToLower(CultureInfo.CurrentCulture) switch
        {
            "gzip" => CompressionType.Gzip,
            "snappy" => CompressionType.Snappy,
            "lz4" => CompressionType.Lz4,
            "zstd" => CompressionType.Zstd,
            _ => CompressionType.None
        };
    }

    public Handle Handle => _kafkaProducer.Handle;

    public IProducer<byte[], byte[]> GetProducer() => _kafkaProducer;

    public void Dispose()
    {
        try
        {
            _kafkaProducer?.Flush(TimeSpan.FromSeconds(30));
        }
        catch (Exception ex)
        {
            _logger.ProducerFlushWarning(ex);
        }
        finally
        {
            _kafkaProducer?.Dispose();
        }
    }
}
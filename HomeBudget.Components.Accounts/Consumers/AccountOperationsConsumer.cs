using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Accounting.Infrastructure.Consumers;
using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Accounting.Infrastructure.DataProtection.Capture;
using HomeBudget.Components.Accounts.Handlers;
using HomeBudget.Components.Accounts.Models;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Accounts.Consumers
{
    internal class AccountOperationsConsumer : BaseKafkaConsumer<string, string>
    {
        private readonly ILogger<AccountOperationsConsumer> _logger;
        private readonly IAccountOperationsHandler _accountOperationsHandler;
        private readonly IActiveLedgerGenerationResolver _activeGenerationResolver;
        private readonly IAccountDeliveryDispositionStore _dispositionStore;
        private readonly IMaintenanceAdmissionFence _admissionFence;

        public AccountOperationsConsumer(
            ILogger<AccountOperationsConsumer> logger,
            IAccountOperationsHandler accountOperationsHandler,
            IOptions<KafkaOptions> kafkaOptions,
            IActiveLedgerGenerationResolver activeGenerationResolver,
            IAccountDeliveryDispositionStore dispositionStore,
            IMaintenanceAdmissionFence admissionFence)
            : base(EnrichConsumerOptions(kafkaOptions.Value), logger)
        {
            _logger = logger;
            _accountOperationsHandler = accountOperationsHandler;
            _activeGenerationResolver = activeGenerationResolver;
            _dispositionStore = dispositionStore;
            _admissionFence = admissionFence;
        }

        internal AccountOperationsConsumer(
            ILogger<AccountOperationsConsumer> logger,
            IAccountOperationsHandler accountOperationsHandler,
            IOptions<KafkaOptions> kafkaOptions,
            IActiveLedgerGenerationResolver activeGenerationResolver,
            IAccountDeliveryDispositionStore dispositionStore,
            IMaintenanceAdmissionFence admissionFence,
            IConsumer<string, string> consumer)
            : base(EnrichConsumerOptions(kafkaOptions.Value), logger, consumer)
        {
            _logger = logger;
            _accountOperationsHandler = accountOperationsHandler;
            _activeGenerationResolver = activeGenerationResolver;
            _dispositionStore = dispositionStore;
            _admissionFence = admissionFence;
        }

        private static KafkaOptions EnrichConsumerOptions(KafkaOptions options)
        {
            var consumerSettings = options.ConsumerSettings;

            if (consumerSettings == null)
            {
                return options;
            }

            consumerSettings.GroupId = "accounting.accounts.group";
            consumerSettings.ClientId = $"{consumerSettings.GroupId}_{Guid.NewGuid()}";

            consumerSettings.EnableAutoCommit = false;

            return options;
        }

        public override Task ConsumeAsync(CancellationToken cancellationToken)
        {
            return ConsumeWithOutcomeAsync(
                payload => ProcessMessageAsync(payload, cancellationToken),
                null,
                cancellationToken);
        }

        internal async Task<KafkaMessageProcessingOutcome> ProcessMessageAsync(
            Confluent.Kafka.ConsumeResult<string, string> payload,
            CancellationToken cancellationToken)
        {
            var message = payload?.Message;
            if (message == null || string.IsNullOrWhiteSpace(message.Value))
            {
                return KafkaMessageProcessingOutcome.Ignored;
            }

            var active = _activeGenerationResolver is null
                ? LedgerGenerationScope.Legacy
                : await _activeGenerationResolver.GetActiveAsync(cancellationToken);
            if (!TryResolveScope(message.Headers, active, out var ledgerId, out var ledgerGenerationId))
            {
                await _dispositionStore.RecordAsync(
                    new AccountDeliveryDisposition
                    {
                        MessageId = GetMessageId(payload),
                        LedgerId = ledgerId,
                        LedgerGenerationId = ledgerGenerationId,
                        PayloadSha256 = Convert.ToHexString(
                            SHA256.HashData(Encoding.UTF8.GetBytes(message.Value))).ToLowerInvariant(),
                        Reason = "stale-generation",
                        Topic = payload.Topic ?? string.Empty,
                        Partition = payload.Partition.Value,
                        Offset = payload.Offset.Value,
                        DisposedUtc = DateTime.UtcNow
                    },
                    cancellationToken);
                _logger.LogWarning("Ignored account message {MessageId} for an inactive ledger generation.", message.Key);
                return KafkaMessageProcessingOutcome.Duplicate;
            }

            await using var admissionPermit = await _admissionFence.AcquireAdmissionPermitAsync(
                MaintenanceScopeKey.ForLedger(active),
                active.LedgerGenerationId,
                cancellationToken).ConfigureAwait(false);

            try
            {
                message.Headers.Add(KafkaMessageHeaders.ProcessedAt, Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O")));
                _logger.LogInformation("Account message has been consumed: {MessageId}", message.Key);

                var accountEvent = JsonSerializer.Deserialize<AccountOperationEvent>(message.Value);
                if (accountEvent is null)
                {
                    return KafkaMessageProcessingOutcome.Ignored;
                }

                accountEvent.Metadata[EventMetadataKeys.FromMessage] = message.Key;
                accountEvent.Metadata[EventMetadataKeys.LedgerId] = ledgerId;
                accountEvent.Metadata[EventMetadataKeys.LedgerGenerationId] = ledgerGenerationId;

                await _accountOperationsHandler.HandleAsync(accountEvent, cancellationToken);
                return KafkaMessageProcessingOutcome.Processed;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize account message {MessageId}. Error: {Error}", message.Key, ex.Message);
                return KafkaMessageProcessingOutcome.Ignored;
            }
        }

        private static bool TryResolveScope(
            Confluent.Kafka.Headers headers,
            LedgerGenerationScope active,
            out string ledgerId,
            out string ledgerGenerationId)
        {
            byte[] ledgerBytes = null;
            byte[] generationBytes = null;
            var hasLedger = headers is not null
                && headers.TryGetLastBytes(KafkaMessageHeaders.LedgerId, out ledgerBytes);
            var hasGeneration = headers is not null
                && headers.TryGetLastBytes(KafkaMessageHeaders.LedgerGenerationId, out generationBytes);
            if (!hasLedger && !hasGeneration && active == LedgerGenerationScope.Legacy)
            {
                ledgerId = active.LedgerId;
                ledgerGenerationId = active.LedgerGenerationId;
                return true;
            }

            ledgerId = hasLedger ? Encoding.UTF8.GetString(ledgerBytes) : string.Empty;
            ledgerGenerationId = hasGeneration ? Encoding.UTF8.GetString(generationBytes) : string.Empty;
            return string.Equals(ledgerId, active.LedgerId, StringComparison.Ordinal)
                && string.Equals(ledgerGenerationId, active.LedgerGenerationId, StringComparison.Ordinal);
        }

        private static string GetMessageId(ConsumeResult<string, string> payload)
        {
            var message = payload.Message;
            if (message.Headers is not null
                && message.Headers.TryGetLastBytes(KafkaMessageHeaders.MessageId, out var messageIdBytes))
            {
                var messageId = Encoding.UTF8.GetString(messageIdBytes);
                if (!string.IsNullOrWhiteSpace(messageId))
                {
                    return messageId;
                }
            }

            if (!string.IsNullOrWhiteSpace(message.Key))
            {
                return message.Key;
            }

            return $"{payload.Topic}:{payload.Partition.Value}:{payload.Offset.Value}";
        }
    }
}

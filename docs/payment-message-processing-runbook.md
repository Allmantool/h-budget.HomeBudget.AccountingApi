# Payment Message Processing Runbook

## Durable State

Kafka payment messages are tracked in `dbo.PaymentInboxMessages` before they are written to EventStoreDB.

Key columns:

- `MessageId`: idempotency key from the `message-id` Kafka header, falling back to key or topic/partition/offset.
- `Topic`, `Partition`, `Offset`: Kafka location for operational lookup.
- `Status`: `Processing`, `Processed`, `Failed`, `Poison`, or `ReplayRequested`.
- `RetryCount`, `LastError`: retry history and latest visible failure.
- `CreatedUtc`, `UpdatedUtc`, `ProcessedUtc`: lifecycle timestamps.

The consumer commits the Kafka offset only after EventStore append succeeds and the inbox row is marked `Processed`, or after a terminal poison message is written to the EventStore dead-letter stream.

## Retry And DLQ Policy

1. Duplicate messages whose `MessageId` is already `Processed` or `Poison` are skipped and committed.
2. EventStore append failures are retryable. The inbox row is marked `Failed`, `RetryCount` is incremented, and the Kafka offset is not committed.
3. After `PaymentInboxOptions.MaxRetryAttempts` failures, the row is marked `Poison`, the message is written to `dead-letter-stream`, and the offset is committed.
4. Invalid JSON or an empty payment payload is immediately marked `Poison`, written to `dead-letter-stream`, and committed.

Poison messages are intentionally visible in SQL and not hidden only in application logs.

## Manual Replay

Use this path after the bad dependency or bad payload has been corrected.

1. Inspect the poison row:

```sql
SELECT *
FROM dbo.PaymentInboxMessages
WHERE MessageId = '<message-id>';
```

2. Request replay:

```sql
UPDATE dbo.PaymentInboxMessages
   SET Status = 'ReplayRequested',
       RetryCount = 0,
       LastError = NULL,
       ProcessedUtc = NULL,
       UpdatedUtc = SYSUTCDATETIME()
 WHERE MessageId = '<message-id>'
   AND Status = 'Poison';
```

3. Re-publish the corrected payload to the original topic with the same `message-id` header.

4. Confirm recovery:

```sql
SELECT MessageId, Status, RetryCount, LastError, ProcessedUtc
FROM dbo.PaymentInboxMessages
WHERE MessageId = '<message-id>';
```

Expected result is `Status = 'Processed'` with `ProcessedUtc` populated.

## Signals

- Logs include duplicate skips, transient retryable failures, and terminal poison transitions with `MessageId`.
- Metrics include `homebudget.payment_inbox.status.transitions` tagged by status.
- EventStore DLQ entries are appended to `dead-letter-stream` with Kafka topic, partition, offset, raw message, and exception metadata.

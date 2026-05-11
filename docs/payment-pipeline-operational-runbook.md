# Payment Pipeline Operational Runbook

Use the **Home Ledger Payment Pipeline** Grafana dashboard first. Pivot from metrics to logs with `CorrelationId`, `TraceId`, `MessageId`, `CommandId`, `OperationId`, `PaymentAccountId`, `StreamId`, and `ImportBatchId` when present.

## Outbox Stuck

Signals:
- `HomeLedgerOutboxStuck`
- `homebudget_outbox_pending_count`
- `homebudget_outbox_oldest_pending_age`

Checks:
- Confirm `/health/ready` is healthy for SQL Server and Kafka on `homebudget-accounting-api`.
- Query `dbo.OutboxAccountPayments` ordered by `CreatedUtc` where `Status = 0`.
- Inspect `LastError`, `RetryCount`, `LockedBy`, and `LockedUntilUtc`.
- Search Seq/Loki by `MessageId` and `CorrelationId`.

Recovery:
- If Kafka is unavailable, restore Kafka first; the publisher will retry retryable rows.
- If rows are locked by a dead instance and `LockedUntilUtc` is in the past, wait for the next publisher poll.
- If rows are dead-lettered, inspect payload and `LastError`; replay only after fixing the root cause and resetting the row state intentionally.

## Kafka Lag

Signals:
- `HomeLedgerKafkaConsumerLagHigh`
- `homebudget_kafka_consumer_lag`
- `kafka_consumergroup_lag`

Checks:
- Confirm the payments consumer worker `/health/ready` is healthy for Kafka, EventStoreDB, SQL Server, and MongoDB.
- Check consumer group assignment in Kafka UI or `kafka-consumer-groups`.
- Inspect `homebudget_eventstore_append_failures_total` and EventStore write latency; slow EventStore writes usually create lag.
- Search worker logs by `MessageId` for repeated transient failures.

Recovery:
- Scale or restart the worker if it is not assigned partitions.
- Restore the failing downstream dependency before increasing consumer count.
- For poison messages, follow the EventStore DLQ section and replay only after payload or handler issues are fixed.

## EventStore DLQ

Signals:
- `HomeLedgerEventStoreDlqGrowing`
- `homebudget_eventstore_deadlettered_total`
- `homebudget_eventstore_dlq_count`

Checks:
- Inspect the `dead-letter-stream` in EventStoreDB.
- Use event metadata for `kafka-topic`, `kafka-partition`, `kafka-offset`, `raw-message`, `MessageId`, `CorrelationId`, and `TraceId`.
- Check related inbox row in `dbo.PaymentInboxMessages`.

Recovery:
- Fix the serialization, validation, or append problem first.
- If the inbox row is `Poison`, request replay through the supported replay path or update operational state according to the existing payment message processing runbook.
- Do not delete DLQ events; retain them as the audit trail.

## Projection Lag

Signals:
- `HomeLedgerProjectionLagHigh`
- `HomeLedgerProjectionFailures`
- `homebudget_projection_lag`
- `homebudget_projection_failures_total`

Checks:
- Confirm MongoDB and EventStoreDB readiness on the worker.
- Query MongoDB `_projection_audit` for `Status = "Failed"` and recent `StartedUtc`.
- Search worker logs by `StreamId`, `PaymentAccountId`, and `CorrelationId`.
- Inspect EventStore persistent subscription `ps-homeledger-mongo-projection-v1` for parked or retrying messages.

Recovery:
- Restore MongoDB/EventStoreDB before replaying.
- If a projection run failed mid-rewrite, rerun the projection for the affected stream/account after the root cause is fixed.
- Verify account balance by comparing payment history rows with the aggregate stream.

## Migration Reconciliation Failure

Signals:
- `HomeLedgerReconciliationFailures`
- `homebudget_reconciliation_failures_total`

Checks:
- Inspect the failed `_projection_audit` row and worker logs by `StreamId` and `PaymentAccountId`.
- Check category/account references required by the migrated payment events.
- Confirm the payment history collection has no stale rows for the failed `ProjectionRunId`.

Recovery:
- Fix missing reference data or malformed migrated events.
- Re-run the projection for the affected account/month stream.
- Confirm `homebudget_reconciliation_failures_total` stops increasing and the audit row ends in `Succeeded`.

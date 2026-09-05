# Payment command idempotency and status

## Contract

`POST`, `PATCH`, and `DELETE` under `payment-operations/{paymentAccountId}` accept an optional transition-period `Idempotency-Key` request header. Clients that need safe retries **must** send a stable opaque key for one logical command. The server never generates this key on a client's behalf.

The key is scoped to the payment account and stored only as a SHA-256 hash. The server also stores a SHA-256 fingerprint of a canonical command representation. A repeat with the same account, key, and fingerprint returns the original command and operation identities. A repeat with a different fingerprint receives `409 Conflict` and produces no new outbox record.

The header is optional only for backwards compatibility with existing clients. Requests without it retain legacy at-least-once API semantics and do not claim retry safety. The next client rollout should make it mandatory.

A repeat delete with the original key replays its original command. A delete for an already-projected operation with a **new** key remains a not-found domain result; it does not create a compensating event. This keeps a new command attempt distinguishable from a safe retry.

## Identity and lifecycle

The existing outbox `MessageId` is the public `commandId`; it is copied to event metadata as both `MessageId` and `CommandId`. Create operation identity is allocated before the durable insert, so a replay returns the stored operation identity.

The command lifecycle is derived from durable boundaries:

`Accepted` (outbox row committed) -> `Published` (Kafka acknowledgement) -> `Persisted` (EventStore append acknowledged) -> `Projected` (Mongo projection and account-balance update complete).

`Failed` means a command reached the durable dead-letter state. Transient outbox failures remain `Accepted` or `Published` as applicable; they are not reported as terminal failures.

A durable dead-letter may finalize only a pre-persistence state. A delayed failure report cannot replace a `Persisted` or `Projected` success state; `Projected` is the final successful lifecycle state.

`GET payment-operations/{paymentAccountId}/commands/{commandId}` returns only account-scoped business lifecycle data. It intentionally does not expose Kafka offsets, payloads, or exception text.

## Duplicate protection

* API: `UX_Outbox_Account_IdempotencyKeyHash` and key-range locking make the idempotency decision durable and concurrency-safe.
* Kafka: the existing `PaymentInboxMessages` table de-duplicates message delivery.
* EventStore: the existing deterministic event ID and duplicate detection prevent a redelivery from appending another logical event.
* Projection: history is rebuilt from the idempotent EventStore stream and writes are projection-run fenced; once the rebuild plus balance update succeeds, each command in that projection batch is marked `Projected`.

## Canonical fingerprint

The fingerprint is UTF-8 SHA-256 of an ordinal, pipe-delimited representation. It includes command kind, account, target operation when applicable, amount formatted with `G29` invariant culture, normalized GUID references, ISO date-only value, scope operation ID, and comment. It does not depend on JSON ordering or request serialization formatting.

## Failure semantics

If the outbox insert fails, the HTTP request fails and nothing is accepted. After acceptance, the existing outbox and inbox retry paths continue delivery. A timeout after acceptance may safely be retried only with the same key and payload; it returns the prior command identity and current status.

## Verification record (2026-09-04)

### Confirmed facts

* The SQL Server Testcontainers factory no longer uses the global `integration-sql-server` container name. Fixtures use their generated container reference and connection string.
* The idempotency acceptance check and insert now run in one SQL transaction with `XACT_ABORT`, `UPDLOCK`, and `HOLDLOCK`. This was necessary to make overlapping HTTP retries resolve to the stored command rather than a SQL infrastructure error.
* Explicit MVC error statuses are preserved by the result-status filter. Ordinary legacy `Result<T>` failures with implicit `200 OK` status still receive their classified HTTP status.

### Final verification matrix

| Requirement | Status | Evidence |
|---|---|---|
| Clean-database V7 migration and idempotency schema | PASS | Payment API Testcontainers suite starts a clean SQL database and exercises V7 columns/index through idempotent command writes. |
| Same-key create retry | PASS | `Create_WhenIdempotencyKeyIsRetried_ShouldReuseTheCommandAndProjectOneOperation`: same command/operation ID, one history record, `Projected` lifecycle. |
| Same-key update retry | PASS | `UpdateAndDelete_WhenIdempotencyKeysAreRetried_ShouldReuseTheirOriginalCommands`. |
| Same-key delete retry | PASS | `UpdateAndDelete_WhenIdempotencyKeysAreRetried_ShouldReuseTheirOriginalCommands`. |
| Different payload conflict | PASS | `Create_WhenIdempotencyKeyIsReusedForDifferentPayload_ShouldReturnConflictWithoutAnotherProjection`; HTTP 409 and one projected operation. |
| Concurrent identical create | PASS | `Create_WhenConcurrentRetriesUseOneIdempotencyKey_ShouldAcceptOneCommandWithoutServerErrors`: eight overlapping requests, one command, one operation, one projection, no 5xx. |
| Timeout after durable acceptance | PASS | `Create_WhenResponseIsLostAfterDurableAcceptance_ShouldRetryTheOriginalCommandAndProjectOnce` aborts the HTTP response only after the payment action completed durable acceptance, then proves the retry returns the stored identity and creates one projection/balance effect. |
| Kafka redelivery | PASS | `Create_WhenKafkaRedeliversTheSameDurableMessage_ShouldCommitDuplicateWithoutAnotherBusinessEffect` produces the persisted outbox payload with its original message ID to real Kafka and observes the production consumer group commit that duplicate offset without another business effect. |
| EventStore duplicate | PASS | `PaymentOperationsEventStoreIdempotencyTests` writes identical message/event identities twice to real EventStoreDB and asserts one stored event. |
| Projection duplicate | PASS | `ProjectionHandler_WhenAddedUpdatedAndRemovedEventsAreRedelivered_ShouldApplyEachLogicalStateOnce` sends each logical state twice through the production projection handler against MongoDB and verifies one record/value and one net balance effect. |
| Status endpoint | PASS | `CommandStatus_WhenCommandDoesNotExist_ShouldReturnNotFoundWithoutCommandMetadata` and `CommandStatus_WhenCommandBelongsToAnotherAccount_ShouldReturnNotFoundWithoutCommandMetadata` both return `404` without command metadata. |
| Status monotonicity | PASS | `OutboxLifecycleMonotonicityTests` proves delayed, repeated, concurrent, delayed retry-failure, and delayed-dead-letter transitions cannot regress `Projected`; the red tests drove guarded SQL transitions. |
| Worker restart/redelivery | PASS | `Create_WhenWorkerIsRestartedAfterCommandAcceptance_ShouldProjectOneOperationAndPreserveLifecycle` stops and recreates the actual worker host, then verifies one projection/balance effect and `Projected`. |
| Missing idempotency header compatibility | PARTIAL | Existing API creation tests continue to pass without the header; this is not yet an explicit regression assertion. |
| Integration harness repeatability | PASS | Payment API integration suite passed 19/19 after the release-gate tests were added; the focused lifecycle and direct projection suites also passed 3/3 and 1/1. |

### Distributed-boundary release matrix

| Scenario | Contract | Test | Infrastructure boundary | Evidence | Result |
| -------- | -------- | ---- | ----------------------- | -------- | ------ |
| Response loss after durable acceptance | The first caller receives no response after the action accepted the command; the same-key retry returns the original operation and projects it once. | `Create_WhenResponseIsLostAfterDurableAcceptance_ShouldRetryTheOriginalCommandAndProjectOnce` | MVC action completion, SQL outbox, Kafka, EventStoreDB, MongoDB | Passed in the 19/19 payment integration suite. The test-only MVC action filter aborts only after the payment action completes; retry asserts duplicate semantics, one read-model operation, one balance delta, and `Projected`. | PASS |
| Kafka redelivery | A byte-identical durable outbox message with the same message ID reaches the payment consumer and has no second business effect. | `Create_WhenKafkaRedeliversTheSameDurableMessage_ShouldCommitDuplicateWithoutAnotherBusinessEffect` | Kafka broker and real consumer group, SQL inbox/outbox, EventStoreDB, MongoDB | Passed in the 19/19 payment integration suite. It publishes the stored SQL outbox payload with the original `MessageId`, then observes `accounting.payments.group` commit that duplicate Kafka offset. | PASS |
| Duplicate EventStore append | Same event/message identity produces one EventStore business event. | `PaymentOperationsEventStoreIdempotencyTests` | EventStoreDB | Existing real EventStoreDB suite: 5/5. | PASS |
| Duplicate projection create/update/delete | Repeated logical projection inputs preserve one history record/value per active state and reverse the balance once on removal. | `ProjectionHandler_WhenAddedUpdatedAndRemovedEventsAreRedelivered_ShouldApplyEachLogicalStateOnce` | Direct projection handler, real MongoDB and account documents | Passed (1/1). Delivers `Added ×2`, `Updated ×2`, and `Removed ×2` through `SyncOperationsHistoryCommandHandler`; queries Mongo history and the persisted account balance after each state. | PASS |
| Lifecycle monotonicity | Older/repeated transitions and delayed retry/dead-letter failure cannot overwrite a later successful lifecycle state; races converge to the furthest state. | `OutboxLifecycleMonotonicityTests` | Production outbox-status service and SQL Server | Passed (3/3). RED runs showed both out-of-order/concurrent transitions and delayed retry/dead-letter failure could regress `Projected`; guarded atomic SQL transitions now retain the later durable state. | PASS |
| Status lookup isolation | Unknown command and a command under a different account are indistinguishable and reveal no payload. | `CommandStatus_WhenCommandDoesNotExist_ShouldReturnNotFoundWithoutCommandMetadata`; `CommandStatus_WhenCommandBelongsToAnotherAccount_ShouldReturnNotFoundWithoutCommandMetadata` | HTTP API and SQL account-scoped lookup | Passed in the 19/19 payment integration suite: both return `404` with no status payload. | PASS |
| Worker restart recovery | A command accepted while workers are stopped is persisted and projected exactly once after a real worker-host disposal/recreation. | `Create_WhenWorkerIsRestartedAfterCommandAcceptance_ShouldProjectOneOperationAndPreserveLifecycle` | Recreated .NET worker host, Kafka, EventStoreDB, MongoDB, SQL Server | Passed in the 19/19 payment integration suite. The integration factory retains a stable EventStore persistent-subscription group across worker-host recreation. | PASS |

### Release state

**READY**. Every mandatory distributed-boundary scenario in the release matrix has deterministic, green evidence. The optional-header compatibility row remains a documented transition-period coverage gap, not an idempotency safety gap.

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
| Timeout after durable acceptance | PARTIAL | The create-retry test discards the first response semantics and proves the same-key retry; an actual HTTP response-loss/fault-injection seam is not present. |
| Kafka redelivery | PARTIAL | `PaymentOperationsEventStoreIdempotencyTests.SendBatchAsync_WhenKafkaRedeliversSameMessage_ThenDoesNotDuplicate` exercises duplicate logical delivery against real EventStoreDB; a real Kafka consumer redelivery is not orchestrated. |
| EventStore duplicate | PASS | `PaymentOperationsEventStoreIdempotencyTests` writes identical message/event identities twice to real EventStoreDB and asserts one stored event. |
| Projection duplicate | PARTIAL | HTTP idempotency tests prove one projected history operation and one balance effect; direct repeated projection-delivery coverage is not yet present. |
| Status endpoint | PARTIAL | Create retry proves account-scoped status reaches `Projected` with accepted/published/persisted/projected timestamps. Unknown-command and wrong-account cases are not covered. |
| Status monotonicity | NOT TESTED | No deterministic late-transition test currently proves a newer lifecycle state cannot be overwritten by an older update. |
| Worker restart/redelivery | PARTIAL | Component consumer tests cover retry/restart decision paths with mocks; a process restart using Kafka and EventStoreDB is not orchestrated. |
| Missing idempotency header compatibility | PARTIAL | Existing API creation tests continue to pass without the header; this is not yet an explicit regression assertion. |
| Integration harness repeatability | PASS | Payment API integration suite passed twice without manual Docker cleanup: 14/14 in 1m51s and 14/14 in 1m58s. A Docker Desktop restart was required between attempts because its daemon stopped externally; no container was manually removed. |

### Release state

**NOT READY**. The acceptance, concurrency, HTTP conflict, EventStore duplicate, and repeatable-harness invariants have evidence. End-to-end Kafka consumer redelivery, direct repeated projection delivery, lifecycle monotonicity, and a true response-loss timeout scenario still require deterministic distributed-boundary tests before a production safety claim.

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

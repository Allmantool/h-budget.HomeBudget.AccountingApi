# Migration target idempotency

## Status

In Progress

## Repository findings

### CONFIRMED

- Payment-account, category, and contractor POST actions allocate random target GUIDs before writing MongoDB.
- Those reference POST actions do not currently inspect `Idempotency-Key`.
- MongoDB has unique target-GUID indexes. Category and contractor display keys are also unique; they are not migration identities.
- Ordinary payment commands hash `Idempotency-Key`, fingerprint canonical business input, and atomically register one SQL outbox row under a unique `(AggregateId, IdempotencyKeyHash)` index.
- Ordinary payment status is exposed as Accepted, Published, Persisted, Projected, or Failed.
- Transfer POST currently creates two payment commands independently and has no logical-transfer idempotency record or unified lifecycle.

### INFERRED

- Existing unkeyed UI requests require a transition path and should retain legacy behavior.
- Migration-safe reference identity can be stored as additive metadata on Mongo documents without coupling target GUIDs to Family Pro integer IDs.

### NOT VERIFIED

- Production Mongo collections contain no legacy index definition that conflicts with the additive sparse migration-idempotency index.
- Production SQL has applied every checked-in root migration through V19.

## Requirements

- REQ-001: Keyed account/category/contractor creates return one stable target GUID for repeated identical requests.
- REQ-002: Reusing a key with a different canonical request returns HTTP 409.
- REL-001: Mongo performs the keyed create as one atomic upsert protected by a unique sparse technical-key index.
- REL-002: Lost responses, restarts, and concurrent duplicates converge on one entity.
- REQ-003: Source reference and target identity remain distinct and recoverable.
- REQ-004: Unkeyed requests retain current compatibility behavior and are not advertised as retry-safe.

## Acceptance criteria and tests

| Criterion | Planned evidence |
|---|---|
| Same key/body returns the original GUID | Controller and Mongo integration tests |
| Same key/different body returns 409 | Controller and integration tests |
| Concurrent duplicates create one document | Mongo/Testcontainers integration test |
| Process-independent recovery | Retry through a new HTTP request/client against persisted Mongo |
| Blank-key compatibility | Controller regression test |

## Progress and resume state

- Decision: persist only SHA-256 of the idempotency key; fingerprint business fields plus source reference, never tracing headers.
- Decision: use a partial unique Mongo index on the technical key so existing unkeyed data is unaffected.
- Remaining: implementation, integration validation, production migration/runbook update.

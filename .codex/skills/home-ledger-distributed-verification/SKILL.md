---
name: home-ledger-distributed-verification
description: Verify Home Ledger payment, messaging, persistence, projection, and financial invariants at the distributed boundary that can prove them.
---

# Home Ledger Distributed Verification

Use this skill when a change reaches Kafka, EventStoreDB, MongoDB, SQL Server, an outbox/inbox, persistent subscription, payment projection, account balance, retry/DLQ, or migration compatibility. Use `home-ledger-sdd` to document the resulting requirements and `home-ledger-tdd` for the behavioral loop.

## Establish the actual boundary

Inspect current code and runbooks. State, for the affected flow:

- authoritative source of truth versus derived projection;
- durable acceptance point and caller-visible success semantics;
- write, acknowledgement/offset, and transaction boundaries;
- delivery/consistency model (for example at-least-once, idempotent, eventual consistency, optimistic concurrency);
- idempotency key and duplicate effect boundary;
- retryable, permanent, poison, cancellation, and restart behavior.

Do not claim exactly-once from a passing duplicate test. Do not call an in-memory queue durable. Do not use a Mongo projection as authoritative state without an explicit architecture decision.

## Repository-specific evidence

The payment pipeline has SQL outbox/inbox tracking, Kafka, EventStoreDB, and Mongo projection behavior. Consult the payment message and operational runbooks for existing acknowledgement, retry, DLQ, and recovery semantics. Reuse `HomeBudget.Accounting.Api.IntegrationTests` fixtures and `TestContainersService`; do not rebuild containers or topology for a single test.

Use `PaymentProjectionWaiter` for bounded, condition-based API/projection checks with diagnostics. Avoid fixed waiting unless timing itself is the requirement and its rationale is recorded.

## Relevant invariants

Select only the invariants the change can affect:

- an accepted payment cannot be permanently lost before its durable boundary;
- a duplicate message or retry cannot produce a second logical financial effect;
- EventStore append and expected revision behavior preserve ordering/concurrency expectations;
- a failed projection remains recoverable and replay produces the required Mongo/balance state;
- offset/subscription acknowledgement does not bypass required durable work or poison-message policy;
- payment amount, sign/direction, operation/account identity, balance delta, and reconciliation remain correct;
- migration-facing contracts remain compatible, or the required follow-up is explicit.

## Failure evidence

For distributed tests, make failure output useful: include relevant operation, account, command/message, stream, known projection state, outbox/inbox state, and elapsed timeout where practical. Preserve cancellation semantics and ensure retry loops terminate. Logging an exception is not recovery; determine what upstream will acknowledge, retry, or expose.

## Completion gate

Map each selected reliability or financial invariant to a test or runtime check in the specification. Report the exact proven guarantee, evidence, and remaining unproven risk; do not replace that analysis with “all tests pass.”

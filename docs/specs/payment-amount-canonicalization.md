# Payment amount canonicalization

## Status

Release verification blocked: the Testcontainers fixture returns `503` while
creating its seed account, before the new HTTP/SQL assertions execute.

## Problem

The payment API currently accepts both positive and negative non-zero values
for a category-backed payment. The balance calculation treats those values as
the same economic operation, which allows two persisted representations for
one payment magnitude.

## Goal

New create and update payment commands accept only a positive amount and
persist that positive magnitude. Existing EventStore payment events, including
negative category-backed payments, remain readable and project to the same
category-directed balance increment.

## Non-Goals

- Rewriting EventStore events, Mongo history, or SQL outbox records.
- Changing transfer signed-amount semantics.
- Changing the public transfer request contract.
- Adding a migration/import implementation not present in this repository.

## Repository Findings

### Confirmed

- `CreateOperationRequest` and `UpdateOperationRequest` reject only zero;
  their actions calculate the idempotency fingerprint after model validation.
- `PaymentCommandFingerprint.Create` hashes the raw decimal amount; `23` and
  `-23` therefore produce different request fingerprints today.
- `FinancialTransactionFactory.CreatePayment` copies its amount unchanged;
  `PaymentOperationsService.UpdateAsync` constructs the update transaction
  directly.
- Add and update commands are independently validated through the MediatR
  validation pipeline. Removal reuses the base validation, so a new positive
  amount rule must not apply to removals of historical records.
- The outbox serializes `PaymentOperationEvent`, Kafka transports it, and the
  consumer appends the same payload to EventStoreDB. The Mongo history
  projection derives records from EventStore events.
- `CalculateIncrement` uses `abs(amount)` for categorized payments and raw
  signed amounts for transfers (and uncategorized records). The SPA mirrors
  this logic and its payment editor requires a minimum amount of `0.01`.
- Transfers are constructed as a negative sender and positive recipient
  transaction; their existing signed semantics must remain intact.
- Kafka is committed after EventStore append and inbox processing. Projection
  is eventual and derives from EventStore; neither is a new-write authority.

### Assumed

- External callers that submit category-backed payments should follow the SPA
  magnitude contract. No in-repository caller submits a negative payment.

### Unknown / Open Questions

- This workspace has no EventStoreDB data access or export, so the presence of
  historical negative payment events cannot be established from repository
  evidence. Replay compatibility is required regardless.
- No Family Pro importer is present in this repository. Its caller contract is
  therefore recorded as a release/migration requirement rather than changed
  here.

## Current and Desired Behavior

Today, a category-backed payment with either `23` or `-23` can be accepted and
persisted, while projection converts both to the same increment according to
category type. New payment writes must instead require `amount > 0`; income
increments remain positive and expense increments remain negative. Old events
continue to use the tolerant replay calculation.

## Architecture and Consistency Context

The API DTO is mapped to `PaymentOperationPayload`, fingerprinted for optional
idempotency preflight, and sent to the operations service. The service creates
or updates a `FinancialTransaction`; MediatR validates the command and writes
a serialized `PaymentOperationEvent` to the SQL outbox. Kafka delivery is
at-least-once with SQL inbox de-duplication; EventStoreDB is the durable event
source and Mongo payment history is a derived, eventually consistent
projection. API model validation is the earliest rejection boundary, before
idempotency preflight and durable command registration.

## Requirements

- REQ-001: Create and update payment API requests reject zero and negative
  amounts and accept positive amounts.
- REQ-002: Payment creation/update command paths cannot persist a non-positive
  payment amount when invoked outside the HTTP API.
- REQ-003: Transfer validation and signed sender/recipient behavior remain
  unchanged.
- COMP-001: Existing negative category-backed EventStore payloads remain
  deserializable and project with the historical category-directed increment.
- CONS-001: A rejected negative payment does not reach idempotency preflight,
  so it cannot conflict with or register under the fingerprint of a valid
  positive payment.

## Acceptance Criteria

- AC-001: Positive create/update request validation succeeds; zero and
  negative values report an `Amount` validation error.
- AC-002: Factory and add/update command validation reject a negative payment;
  transfer validation still permits signed transfer operations.
- AC-003: Positive expense and income payments calculate negative and positive
  increments respectively; negative historical counterparts calculate those
  same increments during projection.
- AC-004: Focused API and operations tests pass, and no historical data is
  rewritten.

## Failure Scenarios and Edge Cases

- EDGE-001: The model-bound API request is invalid before action execution, so
  its raw negative value is neither fingerprinted nor registered.
- EDGE-002: A legacy negative payment can be removed because the positive
  write rule is limited to add/update validation.
- EDGE-003: Transfers retain signed values because the payment-only rule does
  not modify shared transfer validation or factory transfer construction.
- FAIL-001: An external importer that sends a negative payment now receives a
  validation failure and must normalize its payment magnitude before retrying.

## Test Strategy

Use API request-validation unit tests for the early HTTP contract; factory and
MediatR-validator unit tests for non-HTTP command paths; and calculation tests
for replay compatibility and transfer direction. These tests prove the local
invariant. Existing Testcontainers projection tests cover the unchanged
EventStore-to-Mongo pipeline; no new distributed behavior is introduced.

## Implementation Plan

1. Add focused RED tests for positive/non-positive request validation,
   factory/command validation, and legacy increment calculation.
2. Require a positive amount at the request DTO, payment factory, and only the
   add/update command validators.
3. Run focused tests, build the affected solution, inspect the diff, and
   document exact evidence.

## Verification Strategy

- `dotnet test HomeBudget.Accounting.Api.Tests/...` for request validation.
- `dotnet test HomeBudget.Components.Operations.Tests/...` for factory,
  command-validator, fingerprint, increment, and transfer behavior.
- `dotnet build HomeBudgetAccountingApi.sln --configuration Release` for
  compilation/analyzer coverage.
- Testcontainers integration tests are not required for the local write-time
  rule; their EventStore/projection behavior is deliberately unchanged.

## Requirement Traceability

| Requirement / Criterion | Implementation | Test or evidence | Status |
|---|---|---|---|
| REQ-001 / AC-001 | Create/update DTO validation | `RequestValidationTests` (12 passing) | Verified |
| REQ-002 / AC-002 | Factory and add/update validators | Factory and command-validator tests | Verified |
| REQ-003 | No transfer code change | Signed-transfer validator and increment tests | Verified |
| COMP-001 / AC-003 | Existing tolerant `CalculateIncrement` | Historical negative payment increment tests | Verified |
| CONS-001 | API validation before controller action/preflight | `[ApiController]` model validation plus controller ordering inspection | Verified |

## Progress and Resume State

- Decisions made: reject negative input rather than silently applying `abs`.
  This makes the public write contract explicit and leaves valid idempotency
  fingerprints unchanged. The model-binding validation runs before the
  controller action calls idempotency preflight.
- Implemented: create/update API validation, the create factory guard, and
  add/update command validation. The replay model and transfer path were not
  changed.
- TDD evidence: before production changes, negative create/update request
  tests failed because no validation errors were returned; factory tests failed
  because zero/negative values succeeded; command tests failed because negative
  values had no error. All became green after the narrow implementation.
- Verification passed: API unit tests (38), operations unit tests (82), and a
  release solution build with zero errors. The build emitted 651 warnings,
  including package, analyzer, and ruleset warnings.
- Current failure or blocker: historical EventStore data availability remains
  unknown; compatibility is demonstrated by the existing deserialization and
  projection path plus direct historical-sign regression tests.
- Remaining work: none for this scoped change.
- Follow-ups out of scope: audit an external Family Pro importer and query
  production EventStore data using an approved operational export.

## Release Verification Addendum

### Additional Acceptance Criteria

- **REL-001:** ASP.NET Core `[ApiController]` validation rejects create and
  update requests whose Payment amount is zero or negative before the action
  can invoke idempotency preflight.
- **REL-002:** A rejected request leaves no `OutboxAccountPayments` row for
  its account and idempotency-key hash; the same key can subsequently accept a
  positive Payment command.
- **REL-003:** A valid command remains registered when a later invalid request
  uses its key; the invalid request returns `400`, rather than an idempotency
  conflict.
- **REL-004:** The transfer HTTP request retains its positive magnitude
  validation while the accepted sender-side operation remains signed negative.

### Verification Traceability

| Criterion | Integration evidence | Status |
|---|---|---|
| REL-001 | `PaymentOperationsControllerTests` sends the request through the actual API host and asserts `400` for create/update `-23` and `0`; the Testcontainers fixture returned `503` while creating its seed account, before these requests ran | Blocked |
| REL-002 | The same tests query `dbo.OutboxAccountPayments` by `AggregateId` and the SHA-256 idempotency-key hash before retrying `+23`; not reached because fixture account creation returned `503` | Blocked |
| REL-003 | The valid-then-invalid same-key test retains the original SQL `MessageId` after the `400` response; not reached because fixture account creation returned `503` | Blocked |
| REL-004 | `CrossAccountTransferControllerTests` verifies an accepted transfer projects `-23` for its sender operation; not run because the shared fixture could not create accounts | Blocked |

TDD: Not Applicable — this addendum adds integration verification to an
already-implemented behavior; no production behavior is changed. The test
assembly builds successfully, but its required runtime dependencies must be
healthy before the HTTP/SQL assertions can be released as verified.

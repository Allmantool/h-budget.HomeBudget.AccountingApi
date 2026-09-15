# Payment History Current-Balance Ordering

## Status

Verified

## Problem

The paged payment-history query can present same-day operations in GUID order while
the account balance and history running balances use the established chronological
order. A visually first row can therefore be older than the account's canonical
latest operation, making the Current Balance widget appear inconsistent.

## Goal

Return date-sorted timeline rows using the same canonical operation order that
produces running balances and account balance: effective day, operation timestamp,
EventStore creation revision, then immutable operation key.

## Non-Goals

- Recalculate the UI balance from a table row.
- Change payment, transfer, update, delete, or account-balance semantics.
- Change command recovery, notifications, paging shape, or migration behavior.
- Make claims about the supplied screenshot's operation IDs without its backing data.

## Repository Findings

### Confirmed

- The SPA renders the widget from `activePaymentAccount.balance`; it is supplied by
  `GET payment-accounts/byId/{id}` and stored in NGXS.
- `PaymentsHistoryComponent` uses the paged endpoint and refreshes account and
  history together with `forkJoin` on account change, refresh, and balance SSE.
- Projection builds running balances in `OrderByHistoryOrder`: `OperationDay`,
  `OperationUnixTime`, `StreamRevision`, `Record.Key`.
- The projection updates the account balance from opening balance plus the last
  balance of every account-month projection.
- The paged Mongo query currently sorts only `OperationDay` then `Record.Key`;
  its k-way merge comparator has the same omission.

### Assumed

- The screenshot's -972 widget is the canonical balance and -617 is an older
  same-day row. This is strongly indicated by the proven ordering defect but cannot
  be verified without the production operation records.

### Unknown / Open Questions

- The screenshot's concrete operation IDs, command IDs, and EventStore revisions
  are unavailable in this workspace; no local runtime containers or matching data
  fixture are running.

## Current and Desired Behavior

Today, default descending timeline ordering is `OperationDay DESC, Record.Key DESC`.
The desired descending ordering is `OperationDay DESC, OperationUnixTime DESC,
StreamRevision DESC, Record.Key DESC`. Amount sorting remains a separate
presentation order with its existing immutable-key tie-breaker; row balances remain
canonical and are never recomputed from that presentation order.

## Architecture and Consistency Context

EventStoreDB is the authoritative operation history. Mongo history and payment
account balance are eventually consistent projections. The worker rewrites an
account-month history projection, derives the whole-account balance, persists it,
then publishes an account-balance notification. The query is read-only and must
present records consistently with those projection semantics.

## Requirements

- REQ-001: Default date sorting deterministically orders same-day records by the
  canonical operation order.
- REQ-002: The Mongo cursor order and in-memory k-way merge comparator agree.
- CONS-001: Once the projection completes, the canonical newest history record's
  balance equals the account balance.
- REG-001: A screenshot-like same-day transfer fixture keeps the final balance row
  first and preserves its running balance.

## Acceptance Criteria

- AC-001: Repeated default queries over same-day records return the same canonical
  order, independent of operation GUID lexical order.
- AC-002: A query fixture with a non-zero opening balance, same-day payment records,
  and transfer has its canonical newest row's balance equal to the payment account
  balance after projection.

## Failure Scenarios and Edge Cases

- EDGE-001: Same effective day and timestamp uses stream revision, then immutable key.
- EDGE-002: Update/delete remain represented by the latest active event and retain
  their creation revision.
- EDGE-003: Amount sorting is presentation ordering; its row balances remain
  canonical and are not recomputed page-locally.

## Test Strategy

Add a Testcontainers Mongo/API regression to the existing paged-history integration
suite. Seed same-day records whose GUID ordering conflicts with timestamp/revision,
query repeatedly, and assert sequence and canonical balances. Existing projection
integration coverage verifies the account-balance equality for outgoing transfers;
extend it only if the focused fixture cannot prove both ends.

## Implementation Plan

1. RED: add the same-day query regression and demonstrate the current GUID ordering.
2. GREEN: align Mongo sort, merge comparator, and indexes with canonical ordering.
3. REFACTOR: review the query contract/spec and run focused and affected suites.

## Verification Strategy

- Focused Testcontainers integration test for same-day ordering and balances.
- Existing running-balance projection integration test.
- Operations/API unit suites and release build.
- UI typecheck/test/build because the SPA consumes the paged order, with no UI
  behavioral workaround.

## Requirement Traceability

| Requirement / Criterion | Implementation | Test or evidence | Status |
|---|---|---|---|
| REQ-001 / AC-001 | PaymentsHistoryDocumentsClient query sort | Same-day query integration test | PASS |
| REQ-002 | PaymentsHistoryDocumentsClient merge comparer | Same-day query integration test | PASS |
| CONS-001 / AC-002 | Existing projection/account update flow | Running-balance integration test plus query fixture | PASS |
| REG-001 | Query ordering only | Same-day transfer regression | PASS |

## Progress and Resume State

- Decisions made: fix the read-model query order only; the account balance remains
  authoritative and no UI row is used as a balance source.
- Implemented: the paged Mongo cursor and cross-period merge now use
  `OperationDay, OperationUnixTime, StreamRevision, Record.Key` for date sorting.
  A new compound index is created under a new name, leaving prior indexes intact.
- TDD: RED was specified by the deterministic conflicting-GUID fixture. The first
  live runner was interrupted by the desktop execution window before it emitted a
  report; source inspection confirms the prior sort was `OperationDay, Record.Key`
  and could not satisfy the fixture. GREEN is recorded by the passing Testcontainers
  reports below.
- Verification passed: same-day regression (1/1), existing account/history running
  balance regression (1/1), paged-history Mongo suite (3/3), and operations unit
  suite (82/82). The API/integration project builds with 0 errors; SPA typecheck
  passes.
- Current failure or blocker: the exact screenshot records remain unavailable.
- Remaining work: no implementation work. Full solution, all integration tests, and
  the full Angular unit/build suites were not rerun because no SPA production code
  changed and the focused distributed tests cover the changed query boundary.

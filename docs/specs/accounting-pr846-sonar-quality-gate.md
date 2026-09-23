# Accounting PR #846 Sonar quality-gate remediation

## Status

Implemented

## Problem

The current analysis for Accounting PR #846 reports new-code coverage of 69.7% and a C reliability rating. The scanner identified one reliability issue in the payment-history projection worker: its bounded disposal wait does not state whether cancellation is intentionally ignored. The same analysis shows materially untested idempotent-reference and projection-settlement behavior added by the migration PR.

## Goal

Restore an A new-code reliability rating and at least 80% new-code coverage with a focused cancellation-policy correction and behavior-based regression tests.

## Non-Goals

- No migration, financial, API-contract, persistence, or deployment behavior changes.
- No Sonar threshold, coverage import, analyzer, workflow, dependency, release, or packaging changes.
- No production rehearsal, source-data mutation, or vm2 access.
- No broad cleanup of non-blocking maintainability findings.

## Repository Findings

### Confirmed

- Sonar analysis `374b9f6b-9ef2-420e-9ead-5122ac087c07` covers PR head `bc51a7bba92c1785ec1caea175fe40c8d08937f9`.
- All eight OpenCover inputs were imported and all 309 source files were classified as main code; the coverage failure is not a scanner-import defect.
- The only new-code bug is rule S8949 at the bounded worker-disposal wait.
- Disposal cancels the worker-owned token before waiting, so that canceled token cannot be used to cancel the wait itself.
- `EventStoreSubscriptionContext` guarantees at-most-once successful settlement while resetting settlement state after a failed callback.
- `ProjectionBatchContext` merges all subscription contexts and acknowledges or retries each context after batch processing.

### Assumed

- Explicitly choosing `CancellationToken.None` for the already bounded disposal wait satisfies S8949 while preserving the existing five-second shutdown bound.
- Focused coverage of the reported zero/low-coverage branches provides sufficient margin above the 80% new-code gate.

### Unknown / Open Questions

- The final Sonar new-code percentage and rating remain CI-only evidence until the pushed head is analyzed.

## Current and Desired Behavior

Current runtime behavior is preserved. The worker cancels its processor, completes the channel, and waits no longer than five seconds during disposal. The desired source makes the non-cancelable bounded wait explicit. Tests must demonstrate reference-create validation/conflict/replay behavior and projection settlement, merge, retry, and failed-callback recovery.

## Architecture and Consistency Context

- Reference-create controllers derive an idempotency context from the request header, request fingerprint, and source reference, then delegate atomic registration to the document client.
- EventStore persistent-subscription delivery is accepted only after projection processing succeeds; failures are retried rather than acknowledged.
- `EventStoreSubscriptionContext` is the acknowledgement boundary. Settlement is at most once after success and retryable after callback failure.
- `ProjectionBatchContext` coalesces events but retains every delivery context, preserving acknowledgement/retry for each consumed EventStore message.

## Requirements

- REQ-001: Worker disposal must retain its five-second upper bound and explicitly opt out of cancellation for the post-cancellation wait.
- REQ-002: Idempotent account, category, and contractor creation must reject incomplete identity, return conflict for mismatched replay, and return the registered target for create/existing replay.
- REL-001: Merged projection deliveries must settle every retained subscription context exactly once.
- REL-002: A failed settlement callback must be retryable; a successful settlement must suppress subsequent acknowledgement/retry callbacks.
- NFR-001: Sonar new-code reliability must be A and new-code coverage must be at least 80%.

## Acceptance Criteria

- AC-001: The S8949 issue is absent from the final PR analysis.
- AC-002: Focused tests pass for context validation and all idempotent controller outcomes.
- AC-003: Focused tests pass for projection merge/acknowledge/retry and settlement recovery.
- AC-004: The repository build, unit/component tests, integration tests, harness verification, and required PR checks pass.
- AC-005: Sonar reports at least 80% new-code coverage and an A new-code reliability rating.

## Failure Scenarios and Edge Cases

- FAIL-001: A callback throwing during acknowledgement must reset settlement state so the delivery can be retried.
- FAIL-002: A conflict registration must not publish a new account record.
- EDGE-001: Empty, whitespace, or overlong idempotency/source values must not produce an idempotency context.
- EDGE-002: A lower-sequence projection merged after a higher-sequence projection must not replace the latest event, while its settlement context is retained.

## Test Strategy

Use API unit tests for idempotency validation and controller results, without external dependencies. Use worker/infrastructure-focused tests in the existing integration-test assembly to access internal projection types; its namespace-level fixture starts the shared Testcontainers dependencies even though these focused test bodies use no external service. Existing distributed integration coverage remains the evidence for the EventStore/document-store runtime boundary. The reliability correction is analyzer-driven; a behavioral RED test cannot distinguish implicit from explicit non-cancellation, so the existing Sonar failure is the RED evidence.

## Implementation Plan

1. Preserve the failing Sonar analysis as RED evidence and add characterization/regression tests for uncovered contracts.
2. Make the bounded disposal wait explicitly non-cancelable.
3. Run focused tests, complete repository verification, independent review, and the remote PR checks.

## Verification Strategy

- Run focused API idempotency/controller tests.
- Run focused projection/subscription-context tests.
- Run canonical fast/full Accounting harness verification and CI-equivalent solution tests with coverage.
- Confirm release workflows do not run on the topic-branch push and auto-merge remains disabled before pushing.
- Use the final Sonar and GitHub check results as authoritative remote evidence.

## Requirement Traceability

| Requirement / Criterion | Implementation | Test or evidence | Status |
|---|---|---|---|
| REQ-001 / AC-001 | `PaymentOperationsEventStoreSubscriptionReadClient.Dispose` | local analyzer clean; final Sonar analysis pending | Implemented |
| REQ-002 / AC-002 | existing reference-create controllers and context factory | 17 focused API tests | Verified |
| REL-001 / AC-003 | existing `ProjectionBatchContext` | 7 focused projection/settlement tests | Verified |
| REL-002 / AC-003 | existing `EventStoreSubscriptionContext` | 7 focused projection/settlement tests | Verified |
| NFR-001 / AC-005 | tests and explicit cancellation policy | final Sonar quality gate | Planned |

## Progress and Resume State

- Decisions made: correct the single reliability finding; add behavioral coverage only where Sonar identifies migration behavior as untested; do not change scanning or thresholds.
- Implemented: explicit non-cancelable bounded disposal wait; idempotency/context, compatibility, projection, and settlement tests.
- Verification passed: 17 focused API tests; 7 focused projection tests; `eng/verify-fast.ps1 -Area Accounting`; `eng/verify-full.ps1 -Area Accounting` with 73 API, 2 category, 94 operations, and 124 integration tests (123 passed, 1 intentionally skipped).
- Current failure or blocker: the previous remote analysis remains at 69.7% new-code coverage and C reliability until the new head is pushed and analyzed.
- Remaining work: independent review, commit, push, and observe required checks/Sonar.
- Follow-ups out of scope: non-blocking Sonar maintainability findings and any migration/release execution.

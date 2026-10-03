---
name: home-ledger-tdd
description: Apply honest RED-GREEN-REFACTOR and regression-first development to behavior changes in Home Ledger Accounting.
---

# Home Ledger TDD

Use this skill for behavioral changes whenever a meaningful automated test can express the desired behavior. Use it with `home-ledger-sdd` for non-trivial work; the specification selects the invariant and appropriate test boundary.

## RED

Before changing production behavior:

1. Add or identify the smallest relevant test that expresses the next requirement or bug invariant.
2. Run that scope and confirm failure.
3. Confirm the failure demonstrates the missing behavior, rather than a compilation issue, bad fixture, or unrelated defect.

For a bug, reproduce first, identify the root-cause invariant, and make the regression test fail before fixing it where technically practical.

## GREEN

Implement only the smallest change that satisfies the active requirement. Keep the design consistent with the existing codebase; do not hide a failing distributed path with a delay, broad retry, or catch-and-log block whose acknowledgement semantics are unknown.

Run the focused test suite and record the passing result against the relevant requirement or acceptance criterion.

## REFACTOR

After GREEN, simplify naming, duplication, responsibilities, and test seams only while preserving behavior. Re-run applicable tests. Keep unrelated cleanup outside the change or record it as a follow-up.

## Test integrity

Never achieve green by deleting assertions or tests, using `Ignore`/`Skip`, replacing an exact invariant with a vague check, suppressing a deterministic failure with larger arbitrary timeouts, adding broad retries, swallowing exceptions, or changing expectations to match a known defect.

A test may change when the specification proves its previous expectation is wrong. State that reason in the specification or final evidence. Tests written after production implementation remain valuable regression coverage, but they are not TDD; report the sequence honestly.

## Choose the boundary that proves the claim

Use unit/domain tests for local rules, handler/service tests for orchestration, and the repository's Testcontainers integration suite for guarantees involving Kafka delivery, SQL outbox/inbox state, EventStore persistence/concurrency, Mongo projection, retries, replay, or process restart. Do not make all tests integration tests, and do not claim a distributed guarantee from a shallow unit test.

For eventual consistency, prefer the existing `PaymentProjectionWaiter` or an equivalent bounded condition with cancellation and actionable timeout diagnostics. Add duplicate-delivery, retry, concurrency, restart, or replay coverage when the changed invariant depends on it.

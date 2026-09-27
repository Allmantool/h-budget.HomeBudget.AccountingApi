# Payment-history generation publication and recovery

## Status

Implementation and Local Verification Complete; Independent Review Accepted with Follow-up

## Scope Contract

Goal: eliminate cross-process destructive payment-history rewrites and provide a bounded backend-owned read-model reconstruction path for already persisted events.

Expected behavior: multiple workers may concurrently rebuild a period, but readers observe only one complete generation, older work cannot replace newer committed work, account balances cannot be overwritten by a stale cross-period calculation, and an explicit same-revision rebuild can replace damaged legacy output without changing authoritative events.

Allowed scope: Accounting worker, Operations and Accounts Mongo clients/models, focused component/integration tests, worker-owned rebuild command mode, documentation, and local/disposable verification.

Non-goals: production deployment or rebuild, importer execution, EventStore/Kafka/SQL repair, financial event mutation, Gateway/runtime remediation, or a generic release framework.

Risk: CRITICAL.

Key invariants: EventStoreDB remains authoritative; incomplete generations are never reader-visible; period ordering uses only the revision of that exact account-month stream; stale writers cannot change the active head; balance writes are fenced account-wide; acknowledgement and Projected status occur only after generation and balance publication; rebuild does not append or alter events.

Verification: deterministic legacy interleaving reproduction, safe-generation component/integration tests, real Mongo standalone tests, separate-process worker smoke tests, full Accounting gates, focused independent review, and an executable but non-production operator handoff.

## Problem

`RewriteAllAsync` upserts live period documents with a random projection-run GUID and then deletes every document carrying another run GUID. Five independent workers have only a process-local account semaphore. Interleaved successful rewrites can therefore delete one another's output while commands are marked Projected. A triggering-event fallback can also publish a one-event replacement when the complete stream read is empty. Existing checkpoints may be current although the read model is damaged.

## Goal

Publish complete payment-history snapshots through a standalone-Mongo-compatible atomic visibility boundary, fence derived balance updates, reject incomplete source reads, and supply a dry-run-by-default reconstruction command that reuses the corrected projector at an explicitly confirmed account/period scope.

## Non-Goals

- No change to financial event content, event identity, D1-D4, or public HTTP payment contracts.
- No Mongo replica-set conversion or cross-store transaction assumption.
- No production deployment, scaling, restart, rebuild, or migration Resume.
- No direct importer dependency on Mongo/EventStoreDB.
- No repair by reposting payment commands or resetting subscription/offset state.

## Repository Findings

### Confirmed

- EventStoreDB account-month streams are the authoritative payment history. Mongo payment history and payment-account balance are derived read models.
- Deployed revision `84e114b2e55331f886ae3dc4d333ebee58ed1560` and current HEAD `780c26cb19cb0ee9e79eb91389e0d67ebca065c6` contain the same destructive `RewriteAllAsync`, process-local semaphore, and single-event fallback. Staged local changes in the subscription reader add metrics/tracing but do not change the rewrite protocol.
- Production `shared-mongo-db` is MongoDB 7.0.43 without a replica-set command-line option. Multi-document transactions cannot be a prerequisite.
- Normal subscription acknowledgement is deferred until the handler returns. The handler currently publishes history, recomputes/replaces the Mongo payment-account balance, marks SQL commands Projected, and only then returns for acknowledgement.
- The stream reader assigns each event its EventStore stream revision in `SequenceNumber`; a complete forward read can therefore prove continuity from revision zero through its high-water revision.
- Payment-account balance is stored in Mongo and is currently replaced without an ordering fence.

### Assumed

- A single Mongo document compare-and-swap is atomic on the deployed standalone topology, as guaranteed by MongoDB for single-document writes.
- Retaining hidden obsolete generation rows is operationally acceptable for the bounded recovery window; garbage collection is separate and must never race a reader holding an older head.

### Unknown / Open Questions

- Exact production rebuild scope/high-water marks must be discovered during the later authorized dry-run after old writers are contained.
- Gateway/API exit-139 incidents remain a separate operational issue and are not attributed to this defect.

## Current and Desired Behavior

Today, projection output is written directly into the reader-visible period collection. Cleanup is destructive before there is any durable stale-writer decision. The source checkpoint is notification metadata rather than the actual full read boundary, and an empty read falls back to one event.

The corrected path builds an immutable generation in a reserved collection, validates its count and content fingerprint, then atomically publishes a small period-head document. Readers resolve only the generation named by the head, with a compatibility fallback to an untouched legacy period collection when no head exists. A candidate with a lower revision of the same stream loses without changing visibility. Equal revision and equal fingerprint is idempotent; equal revision with different content fails closed. A same-revision rebuild can publish over legacy output because legacy data has no generation head.

After period publication, the projector allocates an account-wide monotonic fence, calculates the balance from currently published period heads plus initial balance, and conditionally updates only the balance and fence fields. A lower fence cannot overwrite a newer result. Status and acknowledgement follow successful balance publication.

## Architecture and Consistency Context

- Source of truth: EventStoreDB account-month payment streams.
- Derived stores: Mongo `_payment_history_generations`, `_payment_history_projection_heads`, `_payment_history_projection_accounts`, legacy period collections, and Mongo `payment_accounts` balance.
- Visibility boundary: atomic compare-and-swap of one period-head document.
- Period ordering: numeric EventStore revision from that exact period stream only.
- Account ordering: Mongo-allocated monotonically increasing balance fence, enforced in the payment-account update filter.
- Delivery: at-least-once persistent-subscription delivery. Duplicate/newer coverage is idempotent; failures before status/ack are retried.
- Cross-store boundary: Mongo generation/head/balance complete before SQL command status; no cross-store transaction is claimed. Crash after Mongo publication is recovered by idempotent redelivery.

## Requirements

- REQ-001: Readers return only a complete published generation or the legacy collection when no generation head exists.
- REQ-002: Projection supports create, update, delete, transfer, and empty-period snapshots without resurrecting deleted operations.
- REQ-003: A worker-owned rebuild mode defaults to plan-only and requires an explicit target confirmation plus account/period scope before execution.
- REL-001: Partial staging or process failure cannot change the active reader-visible generation.
- REL-002: Duplicate delivery and retry after publication/status/ack gaps converge without changing authoritative events.
- REL-003: A same-revision explicit rebuild can replace damaged legacy output and repeated rebuild is logically idempotent.
- CONS-001: An older stream revision cannot replace a newer published revision.
- CONS-002: Equal revision with different snapshot content fails closed.
- CONS-003: A stale account-balance writer cannot overwrite a newer cross-period calculation.
- CONS-004: Status/ack completion covers the triggering event or a demonstrably newer complete source read.
- NFR-001: The protocol works on standalone MongoDB and does not require a schema migration or replica-set conversion.
- NFR-002: Existing HTTP contracts and payment/account metadata remain compatible.
- NFR-003: Rebuild requires trusted local worker execution; no unauthenticated repair endpoint is added.

## Acceptance Criteria

- AC-001: A deterministic two-projector legacy reproduction loses/stales records, while the corrected protocol retains the complete newest snapshot.
- AC-002: Independent projector instances/processes sharing Mongo cannot expose partial staging or let an older revision win.
- AC-003: Concurrent different-period publications cannot leave a stale account balance.
- AC-004: Empty/truncated/non-contiguous reads fail and preserve the prior head.
- AC-005: by-ID, period, account, pagination, and balance readers exclude inactive generations.
- AC-006: Rebuild at an already current revision repairs a damaged legacy projection; a repeated rebuild produces the same identities and values.
- AC-007: EventStore event count, IDs, revisions, and payload hashes are identical before and after rebuild tests.
- AC-008: Five independent worker processes under duplicate/restart pressure converge on identities and payloads, not count alone.

## Failure Scenarios and Edge Cases

- FAIL-001: Worker dies after staging and before head CAS; staged rows remain invisible and retry converges.
- FAIL-002: Worker dies after head CAS and before balance/status/ack; redelivery republishes idempotently and repairs the remaining steps.
- FAIL-003: Stale worker resumes after a newer head; its CAS loses and cannot touch the active generation.
- FAIL-004: Source read is empty, truncated, has a sequence gap, or does not cover the trigger; projection fails/retries without publication.
- EDGE-001: Complete snapshot contains zero active operations after valid delete events; an empty generation is published intentionally.
- EDGE-002: Two identical snapshots have different attempt/run IDs but the same deterministic content fingerprint.
- EDGE-003: Legacy projection has no head at the same source revision; explicit rebuild may establish the first correct head without lowering source revision.

## Test Strategy

- Component tests: reducer semantics, deterministic fingerprint, publication decisions, command/status ordering, and fenced account updates.
- Mongo Testcontainers: legacy destructive interleaving with synchronization barriers; staged-generation CAS; reader isolation; crash points; same-revision rebuild; 98-record analogue; pagination/by-ID; different-period balance races.
- EventStore/Mongo integration: complete-read validation, source high-water, no event mutation, status/ack retry behavior.
- Process smoke: start independent worker/rebuild processes against disposable existing topology and repeat deliveries/restarts; compare complete identity/value sets.
- Eventual assertions use bounded condition polling and diagnostic output, never timing sleeps as synchronization.

## Implementation Plan

1. Add the deterministic barrier-based legacy rewrite reproduction and record its passing proof of the old destructive behavior.
2. Add desired safe-publication tests and confirm RED.
3. Implement immutable staging, source-revision CAS head publication, head-aware readers, and deterministic snapshot fingerprints.
4. Implement account balance fence allocation and conditional balance update; order status/ack after it.
5. Remove the incomplete-read fallback and validate actual stream continuity/trigger coverage.
6. Add dry-run-by-default worker rebuild mode using explicit scope/target confirmation and the corrected projector.
7. Run focused, distributed, full area, format/dependency/safety checks and independent review.
8. Produce exact later-rollout/rebuild/reconciliation instructions; do not execute production changes.

## Verification Strategy

Focused RED/GREEN tests run first in the Operations component and Accounting integration projects. Full verification uses the repository Accounting fast/full gates plus the CI build/test commands. Docker-backed tests use the existing `TestContainersService` and deployed-relevant standalone Mongo topology. The operational process test may use built worker binaries and disposable containers only.

The legacy reproduction mirrors the current two-step bulk-upsert/delete protocol directly against Mongo because the production method has no safe synchronization seam. It is evidence of the exact storage interleaving, not a substitute for corrected production-path tests.

## Requirement Traceability

| Requirement / Criterion | Implementation | Test or evidence | Status |
|---|---|---|---|
| REQ-001, CONS-001, CONS-002 | generation store and period head CAS | deterministic legacy interleaving plus generation-publication Mongo tests | Passed |
| REQ-002 | normal reducer plus immutable publication | existing reducer suite plus empty-generation and redelivery tests | Passed |
| REQ-003, REL-003 | one-shot worker rebuild mode | dry-run, guard, execute, and same-revision damaged-legacy tests | Passed |
| REL-001, FAIL-001 | stage then atomic head publication | corrupt/incomplete staged generation remains invisible | Passed |
| REL-002, FAIL-002 | idempotent publication and ordered status/ack | repeated publication and handler ordering/failure tests | Passed |
| CONS-003 | account fence and conditional balance update | stale fence and two-period deterministic total tests | Passed |
| CONS-004, AC-004 | complete stream read validator | missing start, gap, and trigger-coverage tests | Passed |
| AC-005 | head-aware read paths | account, period, by-ID, pagination, inactive-generation tests | Passed |
| AC-007 | read-only EventStore reconstruction | real EventStore before/after identity and financial evidence comparison | Passed |
| AC-008 | production-host process behavior | actual two- and five-worker executable processes against disposable Mongo, including kill-after-publication restart/redelivery and full identity/value/hash comparison | Passed |

## Progress and Resume State

- Decisions made: immutable shared generation data plus atomic per-period heads; standalone Mongo; no transaction/topology dependency; separate account balance fence; one-shot plan/execute worker mode with no normal consumers registered.
- Implemented: generation staging/validation/head CAS, generation-aware readers, actual stream-revision checkpoints, incomplete-stream refusal, fenced balance publication, truthful audit publication state, all-scope discovery, dry-run/execute rebuild guards, process harness, and operator handoff.
- Verification passed: Release solution build with zero errors; Accounting fast gate; Accounting full gate with API 76/76, Categories 2/2, Operations 106/106, and Docker-backed integration 151 passed/1 skipped/0 failed; deterministic legacy reproduction; focused Mongo/EventStore/rebuild suites; actual two- and five-worker-process 98-record publication, kill/restart, full-value/hash, balance, audit-state, and EventStore-immutability evidence.
- Independent review: `ACCEPT WITH FOLLOW-UPS`; no blocking or should-fix findings remain. The low-priority follow-up is to measure immutable-generation storage growth and design safe delayed retention/garbage collection that cannot race readers holding a prior head.
- Current failure or blocker: none for local release-candidate preparation. Production deployment/rebuild remains intentionally outside authorization and requires the runbook's separate operational approvals and preconditions.
- Local release evidence: separately named API/worker archives, the tracked working-tree delta, all untracked source files, and SHA-256 identities are preserved under the private incident evidence root in `accounting-projection-fix-20260927-reviewed`.
- Remaining work: a separately authorized operator rollout must build/record one immutable image digest from this reviewed source set, deploy API and worker together per the runbook, execute the bounded rebuild/reconciliation plan, and only then reconsider the same-journal migration Resume.
- Follow-ups out of scope: Gateway/API exit-139 root cause and production rollout/rebuild/migration execution.

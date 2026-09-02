# <Change name>

## Status

Draft | Ready | In Progress | Implemented | Verified

## Problem

What business or engineering problem exists?

## Goal

What observable outcome must exist?

## Non-Goals

What is explicitly outside this change?

## Repository Findings

### Confirmed

- <Fact verified in current code, tests, configuration, or runbook.>

### Assumed

- <Inference that has not yet been verified.>

### Unknown / Open Questions

- <Question that affects design, public behavior, or verification.>

## Current and Desired Behavior

Describe what happens now and what must change. Keep business requirements separate from implementation mechanisms.

## Architecture and Consistency Context

List only affected components and boundaries. For distributed work, identify source of truth, write/read models, durable acceptance point, transaction and acknowledgement boundaries, idempotency/concurrency boundary, and consistency model.

## Requirements

- REQ-001: <Observable functional requirement.>
- REL-001: <Relevant durability, retry, ordering, duplicate-delivery, restart, or recoverability requirement.>
- CONS-001: <Relevant consistency or concurrency requirement.>
- NFR-001: <Relevant security, performance, observability, or compatibility requirement.>

Remove categories that are not relevant.

## Acceptance Criteria

- AC-001: <Measurable outcome that proves a requirement.>
- AC-002: <Measurable outcome that proves a reliability or consistency property.>

## Failure Scenarios and Edge Cases

- FAIL-001: <Relevant dependency, crash, retry, poison, or partial-success scenario.>
- EDGE-001: <Relevant boundary condition.>

## Test Strategy

State the lowest test level able to prove each requirement. For distributed guarantees, name the existing Testcontainers fixture, real dependency, or runtime observation that supplies the evidence. Use bounded condition-based polling for eventual consistency.

## Implementation Plan

1. <RED test or regression reproduction.>
2. <Smallest GREEN implementation step.>
3. <Refactor/verification step.>

## Verification Strategy

List focused tests, integration/runtime checks, expected result, and any CI-only validation. Explain any honest TDD exception.

## Requirement Traceability

| Requirement / Criterion | Implementation | Test or evidence | Status |
|---|---|---|---|
| REQ-001 | <file/symbol> | <test/check> | Planned |
| AC-001 | <file/symbol or N/A> | <test/check> | Planned |

## Progress and Resume State

- Decisions made: <decision and reason>
- Implemented: <completed work>
- Verification passed: <command/check and result>
- Current failure or blocker: <if any>
- Remaining work: <next bounded steps>
- Follow-ups out of scope: <items intentionally deferred>

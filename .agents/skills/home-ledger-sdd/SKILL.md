---
name: home-ledger-sdd
description: Specify and plan non-trivial Home Ledger Accounting changes with measurable requirements, traceability, and resumable verification state.
---

# Home Ledger Specification-Driven Development

Use this skill for a behavior, contract, persistence, security, performance-sensitive, cross-project, or major refactoring change. Do not use a full specification for a typo, comment correction, formatting-only edit, obvious namespace cleanup, or other clearly non-behavioral work.

## Discover before specifying

Inspect the owning host/component, related tests, registration/configuration, data boundary, and CI command before writing requirements. Record findings as **CONFIRMED**, **ASSUMED**, or **UNKNOWN**. Do not turn an inference into a fact.

For distributed payment work, map only the affected stages: API, MediatR command/query, SQL outbox or inbox, Kafka, consumer, EventStoreDB, persistent subscription, Mongo projection, balance update, notification, and observability. Identify source of truth, write/read models, transaction and acknowledgement boundaries, and the actual consistency guarantee.

## Specification workflow

Create or update a focused file in `docs/specs/` using [the template](../../../docs/specs/TEMPLATE.md). A specification must answer:

1. What problem, outcome, and non-goals define the work?
2. What does the system do today, and what must observably change?
3. Which requirements, reliability/consistency properties, and acceptance criteria apply?
4. What failure and edge cases affect the change?
5. Which test level and runtime evidence can prove each requirement?
6. What remains to implement or verify if work resumes later?

Use stable identifiers (`REQ-001`, `REL-001`, `CONS-001`, `AC-001`, and similar) only when they improve traceability. Keep a traceability table linking requirement, implementation, verification/evidence, and status. Mark a completed specification `Verified` only after its acceptance criteria have evidence.

## Definition of ready

Before production implementation, the work has a bounded goal and non-goals, relevant current behavior is known, acceptance criteria are observable, unknowns that affect the design are resolved or explicitly accepted, and a test/verification approach is selected. For a bug, the failing invariant and reproduction path are known.

## Verification design

Use the lowest test level that can prove the requirement. Escalate to existing Testcontainers integration tests for real Kafka, EventStoreDB, MongoDB, SQL Server, process/restart, or projection behavior. Eventual-consistency tests must use bounded condition-based polling with useful diagnostics, not arbitrary sleeps.

Examples:

- A zero payment amount is a local validation invariant: a focused validator/handler RED test can be sufficient.
- A duplicate payment message must have no second durable financial effect: verify delivery and durable state at the inbox/EventStore/projection boundary, not only a mapper.

## Definition of done and resume

During lengthy work, keep the spec's status, decisions, traceability, completed checks, current failures, and next steps current. Work is done only when the specified behavior is implemented, every applicable acceptance criterion has linked evidence, the focused diff is reviewed, relevant risks or unrun checks are disclosed, and the specification records the final state. A successful build or test run alone is not the whole contract.

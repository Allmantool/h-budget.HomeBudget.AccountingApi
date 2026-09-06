# Release verification determinism

## Status

Complete

## Problem

Payment validation and idempotency integration coverage was blocked before its HTTP assertions because the Testcontainers Kafka fixture reserved fixed host ports. Browser verification also relied on mutable remote data.

## Goal

Make the API integration environment independent of an occupied host Kafka port, then establish and verify a public-API acceptance fixture rather than relying on remote pre-existing data.

## Non-Goals

- Change production payment or transfer semantics.
- Change the external transfer magnitude contract.
- Disable availability checks or add arbitrary waits/retries.

## Repository Findings

### Confirmed

- `PaymentOperationsControllerTests` uses an in-process `WebApplicationFactory` API and in-process worker, backed by real MongoDB, SQL Server, Kafka, and EventStoreDB Testcontainers; no gateway is involved.
- Fixture account creation is `POST /payment-accounts`, handled by `PaymentAccountsController` through Mongo and an in-process channel.
- The prior failing test setup reported a Docker `InternalServerError`: host bind `0.0.0.0:9092` already allocated, from `KafkaContainerFactory.BuildWithZkMode`.
- The standard Testcontainers Kafka module maps its host listener dynamically and generates its advertised listener from that mapping.
- Payment validation/idempotency and signed-transfer tests already exist in `PaymentOperationsControllerTests` and `CrossAccountTransferControllerTests`.

### Resolved

- A local HTTP SPA at `http://192.168.5.9:4201` consumed the configured
  gateway at `https://vm2.linux:7398/gateway`; its public Accounting API write
  endpoint was `http://vm2.linux:5307`.
- The gateway's current route table exposes the payment-history read, but not
  the standalone fixture-write resources. The acceptance helper therefore
  uses the public Accounting API writes and verifies the same projection
  through the gateway read used by the SPA.

## Requirements

- REQ-001: The integration fixture must start when host port 9092 is occupied.
- REQ-002: Existing HTTP payment validation/idempotency and transfer assertions must execute against real dependencies.
- REQ-003: Browser acceptance must create and verify its own data through public APIs and command lifecycle polling.

## Acceptance Criteria

- AC-001: The non-positive create test reaches its HTTP/outbox assertions with a dynamic Kafka mapping.
- AC-002: Negative/zero create and update, positive create, idempotency ordering, and positive transfer signed-result scenarios pass.
- AC-003: The browser fixture has a Projected command and one verified history operation before UI verification begins.

## Test Strategy

Use the existing Docker/Testcontainers payment test harness and its condition-based projection waiter. Verify the port fix by reserving localhost:9092 during a focused test. Use a bounded command-status poll for browser fixture establishment; no fixed wait is acceptable.

## Requirement Traceability

| Requirement / Criterion | Implementation | Test or evidence | Status |
|---|---|---|---|
| REQ-001 / AC-001 | `KafkaContainerFactory.BuildWithZkMode` | Focused non-positive create test while port 9092 was reserved | PASS |
| REQ-002 / AC-002 | Existing controller integration tests | 8/8 focused runtime cases passed | PASS |
| REQ-003 / AC-003 | `scripts/acceptance/New-ReleaseAcceptanceFixture.ps1` | Run `release-acceptance-20260906-browser`: command `ac80ce97-51ab-4cff-a738-9086aa23c0e0` projected; gateway history returned the exact record and balance | PASS |

## Progress and Resume State

- TDD: RED was the deterministic global fixture failure before any HTTP request: Docker could not bind fixed `0.0.0.0:9092`. GREEN replaces fixed bindings and the obsolete forced Kafka hostname with the supported dynamic Testcontainers listener.
- Verification passed: `Create_WhenAmountIsNonPositive_ShouldReturnBadRequestWithoutCreatingAnOutboxOrIdempotencyRecord` passed 2/2 after the correction; an occupied local 9092 did not prevent Kafka from mapping to a random host port. The focused payment validation/idempotency and transfer suite passed 8/8.
- The acceptance helper accepts Accounting and gateway base URLs plus a run ID;
  it reuses exact run-scoped accounts and canonical handbook nodes, posts one
  idempotent payment, polls command status with a timeout, and verifies the
  gateway history invariant before emitting IDs as JSON.
- Run `release-acceptance-20260906-browser`: Account A
  `5ed2cb00-6579-435a-a2b4-725c80ac1431` (Priorbank, initial USD 45), Account
  B `0ad74b21-83e8-490a-bc2f-825d3a44b354`, operation
  `f2d5f3d2-d12b-424a-ba97-b550a3516bc3`, and the canonical category and
  contractor IDs were verified through the gateway. The expected final
  balance was USD 22.
- Browser acceptance completed against the same gateway fixture: initial
  load, refresh, switch to B, switch back to A, and a second refresh all
  rendered the account and its history. See the SPA specifications for the
  visible evidence.

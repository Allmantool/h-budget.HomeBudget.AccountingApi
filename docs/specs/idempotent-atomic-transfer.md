# Idempotent atomic cross-account transfer

## Status

In Progress

## Problem

The current transfer handler independently registers two payment outbox rows. A crash can separate acceptance, retries can duplicate effects, exact cross-currency source amounts cannot be represented, and no unified lifecycle/readback exists.

## Required design

- One logical transfer identity, idempotency-key hash, canonical fingerprint, and source reference are persisted durably.
- Parent registration and both child outbox rows commit in one SQL transaction.
- The request carries exact positive sender and recipient amounts and both account currencies; migration never derives one side from `RE_KURS`.
- Same key/request returns the original transfer and child identities. Same key/different payload returns 409.
- Lifecycle is the conservative aggregate of both child commands. Projected requires both children Projected; either terminal child failure makes the transfer Failed.
- Readback returns both account/operation IDs, amounts, currencies, date, source reference, and lifecycle.

## Consistency boundaries

- Source of truth for acceptance/idempotency: SQL transfer-command row plus two SQL outbox rows.
- Event source: account EventStoreDB streams, one idempotent child event per account.
- Read model: Mongo history and account documents.
- Projection is eventually consistent. Status/readback must never claim Projected from only one child.

## Required failure evidence

Focused unit tests cover fingerprint conflict and aggregate lifecycle. Docker integration tests must cover atomic registration, lost response, concurrent duplicate, worker restart/redelivery, independent child delay/failure, exact two-side amounts, and exactly two final effects.

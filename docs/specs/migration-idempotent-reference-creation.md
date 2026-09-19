# Migration-idempotent reference creation

## Status

In Progress

## Contract

Accounts, categories, and contractors accept an optional transition-period `Idempotency-Key`. Migration callers must also provide a stable `sourceReference` such as `FamilyPro12:<sourceHash>:ACCOUNT:<SCH_ID>`.

For a keyed request:

- same key + same canonical request returns the stored target GUID;
- same key + different canonical request returns HTTP 409;
- the database operation is an atomic upsert, so concurrency creates one logical entity;
- only the key hash is persisted;
- target GUID remains a Home Ledger identity and is not a raw legacy ID.

Category source identity is intentionally distinct from target-category identity. Family Pro categories 3/5/6 must share one target-category idempotency key while SQLite retains three source mappings.

## Canonical fingerprints

- Account: account type, currency, initial balance, agent, description, source reference.
- Category: category type, ordered name nodes, source reference.
- Contractor: ordered name nodes, source reference.

Volatile HTTP, tracing, and correlation metadata is excluded.

## Compatibility

An unkeyed request follows the existing endpoint behavior. It has no retry-safety guarantee and cannot make the capability contract migration-safe for callers that omit the key.

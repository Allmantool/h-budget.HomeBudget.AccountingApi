# Home Ledger Portability Contract v1

This directory defines the application-owned Home Ledger business-state interchange format. It is independent of SQL tables, MongoDB collections, EventStoreDB stream names, Kafka topics, and .NET runtime types.

## Package form

A package is a ZIP whose entry names exactly match `entry-catalog.json`. Unknown entries are rejected. A full package contains the required JSON, JSON Lines, schemas, and README entries. Optional CSV files are analysis-only and are never imported as financial operations.

JSON and JSONL are UTF-8 without a byte-order mark. JSONL contains one complete JSON object per non-empty line. UUIDs are canonical lowercase strings. Decimal values are canonical invariant strings without exponent notation or implicit rounding. Business dates use `YYYY-MM-DD`; system timestamps are UTC and end in `Z`.

The package is a business-state snapshot at one common cutoff. Import can reproduce equivalent business state but cannot recreate the original event mutation timeline. Native recovery sets preserve original history.

## Financial model

- Opening balances are represented once as `opening-balance` operations. Accounts reference the operation with `openingBalanceOperationId`; import must not turn reconciliation balances into operations.
- Income, expense, adjustment, and opening-balance records have explicit direction, amount, currency, identities, and references.
- A transfer is one canonical parent record with exact source and destination side identities. Cross-currency side amounts do not need to sum numerically to zero. The rate basis is explicit, and an optional fee has its own identity, account, currency, amount, direction, and category.
- Splits preserve their own identities and category references. Whole-package preflight later verifies that split amounts equal the parent amount.
- `reconciliation.json` contains derived balances keyed by account and currency, plus period totals keyed by account, calendar month, and currency, for verification only.

## Entries and field meanings

- `manifest.json` is the package envelope. `commonCutoffUtc` is the single business-state cutoff; `projectionWatermarkUtc` proves the exported read model reached it. `files` is the complete checksummed inventory excluding the manifest itself.
- `accounts.jsonl`, `currencies.jsonl`, `categories.jsonl`, and `contractors.jsonl` contain stable reference identities. `archived` and `archivedUtc` preserve lifecycle state; `legacyReference` preserves a source-system reference without embedding database coordinates.
- `operations.jsonl` contains the canonical financial records described above. `businessDate` is user-visible accounting time, while `originalCreatedUtc` and `updatedUtc` are system timestamps; neither changes cutoff membership.
- `settings.json` contains ledger business settings. `rates.jsonl` contains the historical rates needed by exported operations. `lineage.jsonl` links logical entities to migration runs and mappings.
- `reconciliation.json` is non-authoritative verification data. `operationIdentitySha256` is SHA-256 over operation UUIDs sorted by ordinal string comparison, encoded as UTF-8, one UUID per line, including a final LF.
- `schemas/` contains the exact schemas used for package validation. Its files must be byte-identical to the selected contract version.

For example, amount `"125.45"` is an exact decimal value, `"2025-01-12"` is a business date without a time zone conversion, and `"2025-01-12T11:00:00Z"` is a UTC system timestamp.

## Import compatibility

Read `compatibility-matrix.json` before accepting a package. Version 1 import supports only the declared `1.0.x` state contract. Unknown versions fail closed. A partial package is never accepted as a complete replacement. Import must first validate every file, checksum, identity, reference, and financial invariant; task 3.1 defines the contract but does not implement import.

An importer must stage the archive without mutating the target, reject entries outside the catalog, validate the manifest and complete inventory, validate every record against the packaged schema, verify dependency closure and reconciliation, and only then submit records through the authorized idempotent import boundary. It must never import reconciliation balances as operations. Runtime import, activation, and resume behavior are specified in later tasks.

## Attachments

Home Ledger has no attachment subsystem in v1. Every manifest must declare `attachments.status` as `unsupported-not-present`. `attachments.jsonl`, attachment binaries, links, and nested archives are not allowed.

## Synthetic sample

`samples/full-ledger` is deterministic synthetic data with fixed UUIDs and UTC timestamps. It includes opening balances, income, split expense, adjustment, a multi-currency transfer, fee, historical rate, settings, archived reference semantics, lineage, and reconciliation values. It contains no secrets or real financial data.

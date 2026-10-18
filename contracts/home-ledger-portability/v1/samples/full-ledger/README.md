# Synthetic Home Ledger v1 package

This is deterministic fictional data for contract validation. Import as a full business-state snapshot only after validating `manifest.json`, every listed checksum, every schema, and all relationships. It is not an original event-history archive and contains no attachment payloads.

Decimal strings are exact source values. Business dates are date-only. System timestamps are UTC. Reconciliation balances are verification facts and must not be imported as additional operations.

The manifest defines the common cutoff and complete checksummed inventory. Reference JSONL files contain stable logical IDs; `operations.jsonl` contains the authoritative business-state operations; `settings.json`, `rates.jsonl`, and `lineage.jsonl` preserve settings, historical rate inputs, and migration lineage. The packaged schemas define every field. `reconciliation.json` is validation evidence only.

Before import, reject unknown entries, verify every manifest size and SHA-256 value, validate all JSON/JSONL records, resolve every reference, and compare reconciliation facts. Only after successful preflight may an authorized importer submit the package through the idempotent application boundary. This sample must never be treated as production data or original event history.

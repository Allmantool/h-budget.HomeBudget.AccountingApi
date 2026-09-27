# Payment-history projection recovery handoff

## Safety state

This handoff is for a later, separately authorized production change. It does not authorize deployment, scaling, reconstruction, or migration execution. Keep the Family Pro importer stopped and financial editing paused until the terminal reconciliation gate passes.

The API and worker must be released from the same reviewed source set. The API contains the generation-aware readers; the worker contains the corrected writer, complete-stream guard, balance fence, and one-shot rebuild mode. Old and new projection writers must not overlap. No compatibility claim is made for an old API reading newly published generations.

No SQL migration or Mongo topology change is required. Mongo creates three additive reserved collections and their indexes on first use:

- `_payment_history_generations`
- `_payment_history_projection_heads`
- `_payment_history_projection_accounts`

Payment-account documents gain an additive `PaymentHistoryProjectionFence` field. Existing period collections remain untouched as rollback evidence and are used only while a period has no published head.

## Release gate

Before selecting an artifact, require:

1. The independent review decision is `ACCEPT` or all blocking findings are corrected and re-reviewed.
2. The final source hash, API package hash, worker package hash, and container image digest are recorded together.
3. Full Accounting build, component tests, and Docker-backed integration tests pass on that exact source.
4. The current production journal snapshot remains readable and bound to the original source, target, run, batch, and request identities.
5. The two ambiguous commands are classified by read-only command/EventStore lookup using their original keys. Projection reconstruction must not invent or repost them.

## Contain old writers

On `vm2.linux`, resolve the current worker service and all replicas from the deployment orchestrator. Record container ID, image digest, start time, and service labels. Stop only the Accounting payment-consumer worker service, then prove that zero old worker containers/processes remain. Keep EventStoreDB, MongoDB, SQL, Kafka, Gateway, and the Accounting API running unless the separately approved deployment plan requires an API replacement.

Do not stop containers by historical PID or a broad `dotnet`/image-name kill. Do not start rebuild mode until all old destructive worker replicas are stopped. Do not scale old and new worker revisions together.

## Deploy the corrected readers and projector

Deploy the reviewed Accounting API and worker image with one immutable digest. Replace the API so generation-aware read paths are active. Keep normal payment-consumer workers at zero during reconstruction. Verify API readiness and low-load history/by-ID behavior before running the plan.

The one-shot rebuild mode deliberately does not register Kafka or EventStore subscription workers, so it cannot overlap its own normal consumers. It still requires the external old-writer containment proof above.

## Create the non-mutating plan

Use the corrected worker package with its normal trusted production configuration. Set an operator-owned absolute output path and a stable target label. `Execute` is false by default; specify it explicitly as false for the recorded command:

```bash
export ProjectionRebuild__Enabled=true
export ProjectionRebuild__Execute=false
export ProjectionRebuild__ExpectedTarget=vm2.linux-production
export ProjectionRebuild__PlanOutputFile=/var/lib/home-ledger-recovery/payment-history-projection-plan.json
dotnet ./HomeBudget.Accounting.Workers.OperationsConsumer.dll
```

The plan discovers every account-period represented by a legacy period collection or an existing generation head, reads the complete account-month EventStore stream, proves continuity from revision zero through the recorded high-water revision, and records account, period, stream, revision, event count, published revision, Mongo endpoint/database, and EventStore endpoint. It does not publish projections.

Before execution, reconcile the plan scope against all periods represented by already submitted migration identities in the preserved production journal/approved manifest—not only the 359 unresolved identities. Stop if a submitted period is absent from the plan, a stream is empty/incomplete, the endpoint/database is unexpected, or a high-water/event count changes. Add no not-yet-submitted split, adjustment, or transfer identities.

Archive the plan read-only and record its SHA-256:

```bash
sha256sum /var/lib/home-ledger-recovery/payment-history-projection-plan.json
```

## Execute the frozen plan

Execution requires the same corrected worker package, the unchanged plan file, exact target confirmation, and explicit old-writer containment confirmation:

```bash
export ProjectionRebuild__Enabled=true
export ProjectionRebuild__Execute=true
export ProjectionRebuild__ExpectedTarget=vm2.linux-production
export ProjectionRebuild__ConfirmTarget=vm2.linux-production
export ProjectionRebuild__OldWritersStopped=true
export ProjectionRebuild__ScopeFile=/var/lib/home-ledger-recovery/payment-history-projection-plan.json
dotnet ./HomeBudget.Accounting.Workers.OperationsConsumer.dll
```

The runner refuses a changed Mongo endpoint/database, EventStore endpoint, event count, high-water revision, account binding, incomplete stream, missing stopped-writer confirmation, or target-label mismatch. It reuses the normal reducer, immutable generation publication, account-balance fence, and status ordering. It does not append, delete, or rewrite EventStore events.

Stop immediately on any nonzero exit, source-change refusal, conflicting same-revision fingerprint, target mismatch, incomplete stream, API instability, or unexplained identity/value/balance difference. Do not edit the plan, Mongo, EventStore, SQL status, or migration journal to bypass a refusal. Correct the cause, produce a new dry-run plan, and obtain authorization before another execution.

## Reconciliation and return to service

Before starting normal workers, compare every planned period to its authoritative stream and the API-visible projection by operation identity and financial value: event type, account, date, signed amount, category, contractor, owned reference/comment, update/delete effect, transfer side where already submitted, and period/account balances. Counts alone are insufficient. Confirm source event count, event IDs, revisions, and payload hashes are unchanged.

Start one corrected worker replica and perform bounded duplicate/redelivery and low-load API checks. Then scale only the corrected immutable image to the intended five replicas and repeat cross-replica history/by-ID/balance checks. Stop if any active generation becomes incomplete, a lower revision wins, balances regress, or Gateway/API instability prevents reliable reads.

The same-journal migration Resume remains a separate gate. It may occur once only after full persisted-prefix reconciliation, original-key classification of the two ambiguous sends, stable Gateway/API checks, and verification of the original journal/source/approval/target/run/batch bindings. Use the already corrected CLI package and original journal; never create a fresh Import or new logical identities.

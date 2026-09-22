# Accounting migration PR scope reduction

## Status and identity

Status: **Implementation and local verification complete; companion deployment blocked**  
Risk: **Critical** (distributed financial writes and schema coordination)  
Prepared: 2026-09-21

Remote PR [#846](https://github.com/Allmantool/h-budget.HomeBudget.AccountingApi/pull/846)
remains unchanged. Its base is `master` at
`324b7677e6b9583da534dcc2a6d24952e7ec981a`, its head is
`tech/data-migration` at `320833b14e7ef4bd345c38657b029a5509fe86d7`, and the
merge base is the same base SHA. The committed PR initially contained 84 files,
6,428 insertions, and 153 deletions. This document describes uncommitted local
scope-reduction edits on top of that head; it does not claim a remote PR change.

The proposed base-to-local-tree delta is 73 files, 4,872 insertions, and 150
deletions, versus the remote PR's 84 files, 6,428 insertions, and 153 deletions.
The application/test tree (excluding this self-describing report) is identified by a
sorted path/content manifest SHA-256 of
`FE265D103D4333439992B98DA5E035545967F1581AB5D89854F02A830F5A0722`.

The clean pre-edit head is recoverable from
`C:\Users\pavel\.codex\recovery\accounting-pr846-scope-reduction-20260921`.
Its verified bundle SHA-256 is
`A993DE60BA25F004297CE805B94D5122C6B83545872219984500294DA7BF58EB`;
the same directory contains five binary/full-index format patches and the recorded
clean staged/unstaged/untracked state.

## Scope disposition

| Change group | Requirement / caller | Scope | Decision | Reason and verification |
|---|---|---|---|---|
| Gateway metadata and Accounting routes (companion repository) | M1; `TargetCapabilityProbe` and `HomeLedgerApiClient` | Production companion | PRESERVE_SEPARATELY | The Windows CLI calls Gateway identity/capabilities and every migration write/read route. Keep in Gateway release and mounted configuration; verify its own tests and the disposable CLI/Gateway test. |
| Keyed account/category/contractor creates, fingerprints, Mongo metadata/indexes | M2, M4; CLI reference provisioning | Production | KEEP | Provides atomic same-key/same-body replay and deterministic conflict while preserving unkeyed application behavior. Verify unit/controller and Mongo concurrency tests. |
| Existing ordinary-payment command acceptance/status/readback | M3-M5, M7; CLI payment lifecycle client | Production/base plus retained fixes | KEEP | The importer depends on durable command identity and terminal observation. Verify API and integration idempotency/restart tests. |
| Atomic transfer registration, SQL store/outbox factory, lifecycle/status/readback | M3-M5; CLI transfer lifecycle client | Production | KEEP | One SQL transaction registers the logical transfer and both child commands; retry and conflict semantics are migration requirements. Verify component, controller, and distributed failure-matrix tests. |
| Persistent-subscription deferred acknowledgement, redelivery, projection batching | M6-M8; Operations worker | Production | KEEP | Prevents acknowledgement before projection and preserves replay/restart correctness for migration and normal writes. Verify consumer and distributed restart/redelivery tests. |
| Integration-test V8 transfer schema | M9; disposable SQL setup | Test schema | KEEP | Byte-identical companion for production V20. Verify hashes and disposable migration execution. |
| Production V20 SQL migration (workspace root) | M9; Flyway deployment | Production companion | PRESERVE_SEPARATELY | Owned by the orchestration repository, not this PR. Deploy before the new API/worker. |
| Focused controller, Mongo, component, failure-matrix, and contract tests | M2-M8 | Test | KEEP | These are the regression evidence for the retained defects, not rehearsal scaffolding. Run focused and full suites. |
| Small-data real CLI/Gateway test and actual process-interruption tests | M1-M8 | Cross-process integration test | SIMPLIFY | Retains the only real CLI/Gateway path and real kill/resume evidence. Moved out of `Release`, detached from candidates/hashes/full-run plans, and limited to generated approved small data. |
| Bounded child-process capture/progress and worker host used by those tests | Actual process tests | Test-only | SIMPLIFY | Retained only as the minimum process boundary needed to launch/kill real CLI/API/Gateway processes and hold/restart workers. Renamed as test infrastructure; no production reference. |
| Full-dataset plan, candidate selection, runtime inventory/hashes, expected-results loader, six-hour executor | Historical candidate-55 rehearsal | Release/diagnostic | REMOVE_FROM_PR + PRESERVE_SEPARATELY | No production caller and no focused regression purpose. Removed locally; recoverable in the verified bundle and original candidate-55 evidence. |
| Console-wide capture and container diagnostic dump | Rehearsal diagnostics | Diagnostic | REMOVE_FROM_PR | Per-process bounded logs remain sufficient for the retained tests. |
| `HomeBudget.Release.ProcessProbe` and capture-helper stress tests | Test-framework self-test | Diagnostic/test tooling | REMOVE_FROM_PR | It served only the removed release helper test and was referenced only by the integration test project. |
| `Microsoft.Data.Sqlite` | Actual process-recovery tests reading the CLI journal | Test-only dependency | KEEP | Required only by the retained integration test project; absent from production projects/publish. |
| `KubernetesClient` 17.0.14 transitive pin | API health-check UI parent | Production transitive security fix | KEEP | The base resolves vulnerable 15.0.1 through `AspNetCore.HealthChecks.UI`; the pin is not Kubernetes orchestration code. Verify API graph, advisories, and publish output. |
| `SSH.NET` 2026.0.0 transitive pin | Testcontainers parent | Test-only transitive security fix | KEEP | The base resolves vulnerable 2025.1.0 through Testcontainers. No API/worker path or call site exists. Verify test graphs and production publish absence. |
| SQL Client 7.0.3 and MongoDB 3.11.2 rollback | None specific to migration | Production dependency | REMOVE_FROM_PR | The test-stabilization commit undid base versions 7.1.0/3.12.0 without a retained need. Restored the base versions and verify restore/build/tests. |

## Migration completeness map

| Requirement | Base capability | Retained PR implementation | Evidence | Coordinated deployment requirement |
|---|---|---|---|---|
| M1 identity/capability and target checks | Normal Gateway/API routing | Accounting retains the underlying write/status/readback routes; the real CLI test probes the companion Gateway identity/capability contract | Gateway metadata/route tests plus `FamilyProCliGatewayTests` | Gateway source/routes and host-mounted `appsettings.json`/`ocelot.json`; CLI must fix remote confirmation because `vm2.linux` is currently classified local. |
| M2 reference provisioning | Unkeyed creates/readback | keyed fingerprint/context, Mongo atomic upsert and partial unique index for accounts/categories/contractors | fingerprint, controller, and Mongo concurrency tests | Deploy Accounting API before import. |
| M3 financial representations | ordinary commands and history | exact two-side transfer contract; splits and adjustments remain ordinary-payment representations | command contract, controller, small fixture strict reconciliation | Compatible Gateway routes and Windows CLI package. |
| M4 stable identity/retry/conflict | ordinary-payment idempotency | reference and transfer key hashes/fingerprints, stable target IDs, deterministic 409 | unit, Mongo, controller, concurrent duplicate and repeat tests | Preserve the production CLI journal and target binding. |
| M5 durable acceptance/status | ordinary outbox and command status | atomic transfer command + two outbox rows; unified status endpoints | component/service, SQL integration and actual process-kill tests | Apply V20 before API/worker; expose status routes through Gateway. |
| M6 worker redelivery/projection | existing persistent subscriptions | acknowledgement after successful projection, retry on failure, batching by account/period | consumer failure matrix and distributed worker restart/redelivery | Deploy matching Operations worker and configured Kafka/EventStore subscription. |
| M7 readback/reconciliation | account and history endpoints with paging | transfer two-side readback plus retained account-scoped pagination/balance behavior | API history tests and small fixture terminal reconciliation | Gateway read routes and complete pagination must be mounted on vm2. |
| M8 normal application behavior | all existing unkeyed/UI behavior | unkeyed reference/transfer paths remain; worker changes are shared reliability fixes | full unit/component/API/integration suites | Deploy API and worker from one reviewed source revision. |
| M9 schema/config compatibility | SQL through V19 and current Gateway config | integration V8 equals production V20; Accounting write/status/readback DI and routes remain | SHA-256 equality, schema test, publish/startup checks | Root `Scripts/ms-sql/V20__Add_atomic_transfer_commands.sql`; release-bound mounted Gateway capability configuration. |

The approved D1-D4 policy, source interpretation, manifest, and financial expected
results are unchanged. The small fixture asserts reference creation, ordinary payment,
split line, adjustment, exact two-side transfer, terminal reconciliation, and a repeat
invocation with zero additional logical effects.

## Dependency audit

The base graph resolves `KubernetesClient` 15.0.1 into the API for `net10.0`,
`net10.0/linux-x64`, and `net10.0/win-x64` through
`AspNetCore.HealthChecks.UI` 9.0.0. NuGet reports advisory
`GHSA-w7r3-mgwf-4mqq`. The narrowed tree centrally pins 17.0.14 and retains
`CentralPackageTransitivePinningEnabled`; the package is therefore expected in the API
publish, but it is an existing health-check runtime dependency rather than migration
Kubernetes behavior.

The base test graphs resolve `SSH.NET` 2025.1.0 through Testcontainers 4.9.0 and
report high-severity advisories `GHSA-q939-rpr3-3284` and
`GHSA-mggc-4xg6-vcxf`. The narrowed tree pins 2026.0.0. It resolves only in projects
that consume Testcontainers (`HomeBudget.Test.Core` and test projects); the API and
Operations worker have no `SSH.NET` dependency. There are no SSH or Kubernetes call
sites added by this PR.

## Change impact and handoff

- Runtime behavior retained: migration-safe reference creation, atomic/idempotent
  transfers, durable status, and worker projection/redelivery behavior are unchanged by
  the scope reduction.
- Contract/schema: no cleanup change; Accounting still requires companion V20 and the
  Gateway migration contract/routes.
- Dependencies: release-only ProcessProbe is removed; base SQL Client and MongoDB
  versions are restored; security pins remain; SQLite remains test-only.
- Test/tooling: full-dataset candidate orchestration is removed, while focused and
  real small-data process/recovery coverage remains. The three cross-process tests are
  intentionally `[Explicit]` and are skipped by ordinary `dotnet test`; the checked-in
  `scripts/verification/verify-familypro-cli-gateway.ps1` gate publishes every runtime
  from the requested source commits, hashes the complete publish outputs, runs the
  explicit tests, and retains its result and logs. Its caller-selected output directory
  contains complete publish outputs and must be private and access-controlled.

Run the focused gate with explicit source roots, output location, and reviewed commits:

```powershell
./scripts/verification/verify-familypro-cli-gateway.ps1 `
  -GatewaySourceRoot <gateway-repository> `
  -MigrationSourceRoot <migration-repository> `
  -OutputRoot <evidence-directory> `
  -ExpectedAccountingCommit <accounting-sha> `
  -ExpectedGatewayCommit <gateway-sha> `
  -ExpectedMigrationCommit <migration-sha>
```
- Historical evidence: candidate 55 remains evidence only for its exact sealed bundle.
  Its manifests, reports, hashes, and accepted result under
  `C:\Dev\POC\SanuelFamilyDb\reports\disposable-release-verification-20260920-full-rehearsal`
  were not modified or rebound to this tree.

Minimum later deployment order is: (1) release a Windows CLI package with the
`vm2.linux` remote-confirmation correction; (2) apply root-repository V20; (3) deploy
Accounting API and Operations worker from the reviewed Accounting revision; (4) deploy
the compatible Gateway image and separately deliver its host-mounted migration
`appsettings.json` and `ocelot.json`; (5) pass authenticated TLS, target identity,
capability, schema, backup/restore, permissions, capacity, writer-control, and
read-only baseline gates before any owner-authorized import.

The existing CLI runbooks provide these supported operator command shapes. They are
prepared only and were not executed here. Preflight is read-only against the source
and target, but writes local evidence and initializes its separate review-state file:

```powershell
dotnet .\FireBirdV25Client.dll `
  --mode preflight `
  --source-db '<HARDENED_READONLY_COPY>' `
  --firebird-user 'MIGRATION_READONLY' `
  --target-url 'https://vm2.linux:7398' `
  --frozen-source-hash '<APPROVED_SHA256>' `
  --frozen-source-length '<APPROVED_BYTE_LENGTH>' `
  --state-db '<PREFLIGHT_EVIDENCE_ROOT>\state\family-pro-preflight.sqlite' `
  --output '<PREFLIGHT_EVIDENCE_ROOT>\reports' `
  --log-dir '<PREFLIGHT_EVIDENCE_ROOT>\logs' `
  --run-id '<APPROVED_PRODUCTION_RUN_ID>'
```

Never reuse the preflight state file as the production journal. The first separately
authorized import must create the production journal at a verified nonexistent path.

After all gates and separate import authorization, the supported mutation shape is:

```powershell
$migrationExitCode = 1
try {
  $env:FAMILYPRO_MIGRATION_Migration__TargetToken = '<FROM_APPROVED_SECRET_STORE>'
  dotnet .\FireBirdV25Client.dll `
    --mode import `
    --manifest '<APPROVED_MANIFEST_PATH>' `
    --approval '<APPROVAL_PATH>' `
    --target-url 'https://vm2.linux:7398' `
    --accounting-prefix '/gateway/accounting' `
    --expected-gateway-service 'HomeBudget.Backend.Gateway' `
    --expected-gateway-environment 'production' `
    --migration-contract-version '1' `
    --expected-target 'vm2.linux' `
    --confirm-target 'vm2.linux' `
    --state-db '<PRODUCTION_EVIDENCE_ROOT>\journal\family-pro-vm2-production.sqlite' `
    --output '<PRODUCTION_EVIDENCE_ROOT>\reports' `
    --log-dir '<PRODUCTION_EVIDENCE_ROOT>\logs' `
    --log-format 'json' `
    --run-id '<APPROVED_PRODUCTION_RUN_ID>' `
    --batch-id '<APPROVED_PRODUCTION_BATCH_ID>' `
    --progress-interval '15' `
    --readback-attempts '3000' `
    --readback-delay-ms '25' `
    --max-in-flight '128'
  $migrationExitCode = $LASTEXITCODE
}
finally {
  Remove-Item Env:FAMILYPRO_MIGRATION_Migration__TargetToken -ErrorAction SilentlyContinue
}
if ($migrationExitCode -ne 0) { exit $migrationExitCode }
```

A known-safe continuation changes only `--mode` to `resume`; `reconcile` performs
readback without resubmission. The authoritative companion templates remain
`docs/runbooks/family-pro-migration-preflight.md` and
`docs/runbooks/family-pro-vm2-target-deployment.md` in the migration repository.

No commit, push, merge, image/package publication, vm2 mutation, or production import
is part of this patch.

## Remaining gaps

- PR/code: the narrowed patch and its local evidence are complete, but remain
  uncommitted; remote PR 846 still contains its original head.
- CI/package: local restore, build, tests, publish inspection, dependency audit, and
  focused source-built gate pass. Remote checks predate this patch, and the existing
  SonarCloud Code Analysis check remains failed until the owner publishes a revision.
- Companion release: V20, the matching Accounting API/worker, Gateway image plus
  host-mounted routes/contract configuration, and a CLI with corrected remote-target
  classification must be released together in the documented order.
- Live operations: authenticated TLS/identity/capability, schema, backup/restore,
  permissions, capacity, writer-control, and read-only baseline gates remain to be
  performed against vm2 under the deployment runbook.
- Authorization: no merge, release, deployment, import, or repeat authorization was
  granted by this task.

## Verification record

- Baseline restore: PASS with pre-existing duplicate/prunable package warnings.
- Baseline Release build: PASS, 0 errors and 724 analyzer/compiler warnings.
- Baseline API unit tests: PASS, 56/56.
- Baseline Operations component tests: PASS, 94/94.
- Initial selected Docker integration attempt: BLOCKED before execution because the
  local Docker endpoint was unavailable to Testcontainers (`NullReferenceException`
  in `DockerApiClient` construction). Docker Desktop was then started locally and all
  Docker verification below passed.
- Base dependency audit: confirmed vulnerable `KubernetesClient` 15.0.1 and `SSH.NET`
  2025.1.0 paths described above.
- Fresh post-cleanup restore: PASS. Warnings are pre-existing `NU1504` duplicate
  `SonarAnalyzer.CSharp` and `NU1510` prunable framework package references.
- Final Release solution build: PASS, 0 errors and 720 existing analyzer/compiler
  warnings.
- Final test projects: API 56/56 PASS; Categories 2/2 PASS; Operations 94/94 PASS;
  Accounting integration 116 PASS, one existing `[Ignore("Not implemented so far")]`.
- Source-built explicit CLI/Gateway gate: PASS, 3/3 in 1m44s. The gate verified the
  requested Accounting, Gateway, and migration repository commits, freshly published
  all four runtimes, recorded complete publish manifests and SHA-256 hashes, and then
  ran the small fixture plus both actual process-recovery tests. It covered reference
  provisioning, ordinary/split/adjustment/transfer representations, strict terminal
  reconciliation, balances, stable identities, second-run zero delta, CLI kill after
  durable payment acceptance, and transfer interruption/kill followed by same-journal
  resume to one two-sided transfer. Ordinary test runs intentionally skip these three
  `[Explicit]` tests; this focused gate is required for migration handoff.
- Focused-gate regression: the first run passed 2/3 and exposed SQLite error 8 after a
  genuine transfer-process kill because a pooled read-only inspection handle blocked
  hot-journal recovery. Test inspection now disables pooling, clears existing pools
  before the immediate read/write recovery open, and the complete source-built gate
  passes. The failed and passing runs both retain logs, TRX, result JSON, source status,
  and artifact manifests under
  `C:\Users\pavel\.codex\verification\accounting-pr846-focused-gate-20260921`.
- Workspace harness: `eng/verify-fast.ps1 -Area Accounting` PASS and
  `eng/verify-full.ps1 -Area Accounting` PASS. The full gate independently reported
  the same 56 + 2 + 94 + 116 tests and one existing ignored integration test; its
  final integration run completed in 27m02s.
- Schema: integration V8 and production V20 both SHA-256
  `A5BDF02D2EFCF5E6A555BDFD00380C3A02054BFBC71F9833584134EBE6C721A6`;
  harness migration checks and disposable SQL execution passed. `git diff --check`
  reports the intentional second terminal newline in V8; it is retained to preserve
  byte equality with the already-owned V20 migration.
- Publish: local API and worker publish passed. API contains `KubernetesClient.dll`
  through the retained health-check parent; worker contains no Kubernetes/SSH.NET/
  SQLite/Testcontainers assembly. Neither output contains tests, ProcessProbe,
  Family Pro fixtures/databases, manifests, journals, or rehearsal evidence.
- Dependency graph/advisories: `dotnet nuget why` confirms the documented parents and
  scopes; fresh `dotnet list ... package --vulnerable --include-transitive` reports no
  vulnerable packages in any solution project.
- Formatting/scans: `dotnet format ... whitespace --verify-no-changes` passed for all
  65 changed C# files. Changed-scope private-key/credential, TLS-bypass, vm2/IP,
  source-database, and hardcoded-workstation scans returned no source matches.
- Remote PR status remains OPEN/UNSTABLE at the original base/head. Eleven checks are
  successful and the existing SonarCloud Code Analysis check is failed; the remote
  checks do not include these uncommitted scope-reduction edits.
- Independent critical-risk review: ACCEPT. The final reviewer confirmed the
  source-built gate, forced-termination proof, SQLite recovery, ownership boundaries,
  fail-closed operator templates, and access-controlled evidence guidance.

# Implement Home Ledger data protection — Accounting control plane foundation

## Status

In Progress — tasks 1.1 through 1.5 and 3.1 through 3.3 are implemented and locally verified for the Accounting-owned control-plane, generation foundation, portability contract, and bounded package inspection. Task 1.7 documentation is present; the required workspace full-area gates remain separate completion evidence.

## Authority and Scope

- Root change: `C:/Dev/h-budget/openspec/changes/implement-home-ledger-data-protection`.
- Authoritative tasks: 1.1 through 1.5 and the Accounting documentation portion of task 1.7 in the root change's `tasks.md`.
- This increment owns the Accounting integration-test persistence contract, additive root/integration SQL migrations, internal SQL operation store/processor/worker, and ledger-generation propagation across Accounting persistence and asynchronous boundaries. It does not add public administrative APIs, authorization policy, backup/import/reset executors, or mark the root task complete.

## Problem

Home Ledger has durable payment-processing state but no protected control plane for long-running data-protection operations. There is consequently no executable contract for restart-safe operation state, replica-safe scope exclusion, plan-bound execution, append-only audit, or preserving control state while selected-ledger business data is reset.

## Goal

Establish and implement the minimum durable Accounting control-plane component required to enqueue, resume, fence, cancel, poll, and settle long-running operations across worker/process restarts.

## Non-Goals

- Implementing public administrative operation APIs, authorization, operation-kind executors, or production reset.
- Defining backup provider, portability archive, recovery, or reconciliation behavior beyond the control-plane fields needed by these tests.
- Accessing or changing any live environment, including `vm2.linux` / `192.168.5.159`.
- Treating the test harness reset as the future production ledger-reset implementation.

## Repository Findings

### Confirmed

- The integration suite creates isolated Testcontainers SQL Server, MongoDB, EventStoreDB, and Kafka resources in `TestContainersService`.
- Before task 1.2, the integration SQL migrations created only `HomeBudget` and `HomeBudget.Accounting`; V9 now creates `HomeBudget.DataProtection` and the control-plane tables described below.
- SQL payment command acceptance and processing data currently live in `HomeBudget.Accounting` (`OutboxAccountPayments`, `TransferCommands`, and `PaymentInboxMessages`).
- The integration-only reset helper deletes Accounting business/processing rows and resets the disposable MongoDB, Kafka, and EventStoreDB containers.
- Root design decision D1 places operation journals, leases, manifests, audit, and related control data in a dedicated `HomeBudget.DataProtection` database outside selected-ledger reset scope.
- Root design decision D10 requires one durable operation state machine and a database lease/scope fence.
- The OperationsConsumer is the existing long-running Accounting worker host; it can register an internal hosted control-plane worker without exposing an HTTP surface.

### Assumed

- Plan fingerprints are canonical lowercase SHA-256 hex strings and scope keys are opaque canonical strings chosen by the owning service.
- Conditional updates are the persistence primitive for expected-state and expected-plan compare-and-set behavior; higher-level transition legality remains owned by the task 1.3 component.
- Protected audit immutability is enforced against ordinary `UPDATE` and `DELETE`, including the privileged disposable test connection.

### Unknown / Open Questions

- Final production principal/role separation and whether audit immutability is implemented with triggers, denied permissions, or an equivalent mechanism.
- Final retention/archive mechanism for audit rows and the independent recovery catalog.
- The production principal/grant model and independently protected audit/catalog publication required by later operational tasks.

## Current and Desired Behavior

The task 1.2 migrations create `HomeBudget.DataProtection` and its initial control schema additively. The task 1.3 component now enqueues operations with an audit record, enforces the legal durable transition graph, resumes the same operation ID through new store/processor instances, honors durable cancellation at execution/reconciliation checkpoints, and polls row-version changes within caller-provided bounds. A renewable unique scope lease excludes competing replicas; next-work selection excludes actively leased scopes so unrelated work is not starved. Protected audit rows accept appends but reject mutation and deletion. Control rows remain present after the integration harness resets selected business state.

Task 1.5 adds the generation compatibility boundary. Existing SQL and Mongo records are adopted as ledger `default`, generation `legacy` only while generation zero is active. New durable writes must explicitly stamp both identifiers; SQL has no persistent scope defaults. Outbox, transfer, inbox, Kafka, EventStore metadata/stream names, authoritative Mongo documents, and projection documents are scoped independently of the existing payment-history publication `GenerationId`. The active generation comes from the protected control database. Its raw unique `GenerationToken` is transported as an exact quoted strong ETag (generation zero: `"legacy-token-v1"`), and `If-Match` is an opaque precondition rather than a client-selected scope.

## Architecture and Consistency Context

- Source of truth for this increment: dedicated SQL control database.
- Durable acceptance point: commit of the operation/audit/lease row in SQL.
- Concurrency boundary: an atomic conditional `UPDATE` for operation state and a unique database key for a scope lease.
- Idempotency/fencing boundary: operation ID plus expected current state and immutable plan fingerprint.
- Reset boundary: `HomeBudget.Accounting` business/processing data may be reset; `HomeBudget.DataProtection` control data is excluded.
- Delivery guarantee: unfinished non-terminal rows remain in SQL and are rediscovered after process restart. Operation-kind executors must be idempotent because a crash can occur after an external effect and before its next durable transition.
- Cancellation guarantee: cancellation is cooperative and takes effect at the next state-machine checkpoint; host cancellation propagates to the executor and leaves non-terminal durable state available for restart.
- Replica boundary: a unique renewable scope lease plus conditional transitions prevents concurrent processing of the same scope. Every processor transition, renewal, release, and failure/cancellation checkpoint is conditioned on the current operation/lease/scope/owner/fence tuple and unexpired ownership. The monotonically increasing fence token is passed to every operation executor call so future destructive component adapters can reject stale owners at their own mutation boundary.

## Requirements

- REQ-001: A control operation records its ID, canonical scope, plan fingerprint, and lifecycle state durably across SQL connections.
- CONS-001: A state transition succeeds only when the persisted current state and plan fingerprint both match the caller's expectations.
- CONS-002: Two active leases for the same canonical scope cannot coexist, even when acquired by different replicas.
- SEC-001: A protected audit entry is append-only; ordinary update and delete attempts fail without changing or removing it.
- REL-001: Control operations and protected audit entries are outside selected-ledger reset scope.
- REQ-002: The control database contains operation journals, scope leases, active-generation heads, component receipts, per-entity import outcomes, recovery manifests, and protected audit entries with explicit keys and validation constraints.
- CONS-003: Operation identity, scope, generation, and plan fingerprint are immutable after durable acceptance; a lease receives a monotonically increasing fence token.
- REL-002: The root V21 migration and integration V9 migration are byte-identical, additive, and safe to apply again without changing the resulting schema.
- REQ-003: A new worker/store instance resumes a non-terminal operation under its original operation ID and advances only through the legal durable lifecycle.
- REL-003: A durable cancellation request is observed at the next safe checkpoint without treating acceptance of the request as immediate cancellation of an in-flight effect.
- CONS-004: A renewable SQL scope lease excludes another replica beyond the initial lease interval, while next-work selection can progress a different unleased scope.
- CONS-005: A worker may advance or fail an operation only while its exact lease owner and monotonic fence token remain current and unexpired; takeover cancels the stale executor token and makes all later stale checkpoints no-ops.
- REL-004: Executor I/O, HTTP, and provider failures are durably journaled as `failed` while the lease remains current, and the background worker continues polling later operations without terminating the host.
- REQ-004: Status reads return durable state and bounded polling returns either a changed row version or a timeout result without an unbounded wait.
- GEN-001: Existing Accounting SQL rows and configured generation zero adopt `LedgerId=default`, `LedgerGenerationId=legacy`, generation number `0`; both scope columns become required after backfill.
- GEN-002: Outbox, transfer, and inbox identity, idempotency, status, and join predicates include ledger and ledger-generation scope so identical logical identities in different generations do not conflict.
- GEN-003: Kafka headers and EventStore metadata carry server-selected `LedgerId` and `LedgerGenerationId`; legacy EventStore stream names remain byte-for-byte unchanged while later generations use isolated stream names.
- GEN-004: Missing scope is adopted as `default`/`legacy` only while the legacy generation is active. A stale or ambiguous delivery records a durable terminal disposition, performs no EventStore/projection/business effect, and permits Kafka offset completion.
- GEN-005: Mongo authoritative/reference and derived projection documents and their uniqueness predicates are ledger-generation scoped. Existing payment-history publication `GenerationId` remains separate from `LedgerGenerationId`.
- GEN-006: Payment and transfer command status and mutation responses expose the server-selected ledger identity, ledger-generation identity, and opaque generation token. Generation zero accepts a missing precondition for deterministic legacy compatibility; later generations require an explicit matching `If-Match` precondition.
- GEN-008: Request admission resolves one expected generation and carries that immutable scope through command handling. The SQL transaction that durably accepts an outbox row or complete canonical transfer atomically verifies that the same environment/ledger/generation/token remains active; both transfer sides are inserted in one guarded transaction. Activation between HTTP admission and SQL acceptance fails with HTTP 412 and creates no durable financial effect.
- GEN-007: Account-topic producers stamp the server-selected ledger scope. Account consumers commit-and-ignore stale scope without invoking the account handler, and adopt unscoped legacy deliveries only while generation zero is active.

## Acceptance Criteria

- AC-001: `queued -> running` committed through an expected-state compare-and-set remains `running` after reconnect, while another transition expecting `queued` affects zero rows.
- AC-002: After one lease is inserted for a scope, a second owner inserting the same scope receives a SQL constraint failure and the original owner remains recorded.
- AC-003: Presenting a fingerprint different from the operation's stored fingerprint affects zero rows and leaves the state unchanged.
- AC-004: Updating and deleting an appended protected audit row each receive a SQL failure, and the original row remains unchanged.
- AC-005: An operation and audit entry inserted in `HomeBudget.DataProtection` remain after `TestContainersService.ResetContainersAsync()` resets disposable ledger stores.
- AC-006: A fresh disposable migration chain creates all seven tables and the expected primary, foreign, unique, check, index, and trigger objects.
- AC-007: Directly applying V9 twice after normal Evolve startup succeeds and preserves the schema signature.
- AC-008: Invalid lifecycle values and attempted changes to accepted plan binding fail at the SQL boundary.
- AC-009: Starting with existing V0–V8 Accounting state and no control database, V9 creates the complete control schema without changing the existing Accounting marker row.
- AC-010: A processor created with a new store instance resumes a persisted `validating` operation and settles the same ID through reconciliation.
- AC-011: Illegal lifecycle edges are rejected before SQL mutation; stale expected-state transitions affect no row.
- AC-012: Cancellation requested during execution produces `cancelled` at the following checkpoint and suppresses reconciliation.
- AC-013: A second replica cannot execute a leased scope, including after the original lease expiry when the active processor renews ownership.
- AC-014: While the oldest runnable scope is leased, another unleased scope is selected and processed.
- AC-015: Row-version polling observes a durable change and returns `null` inside the configured timeout when no change occurs.
- AC-016: The OperationsConsumer hosts the worker through DI, disabled by default until configuration enables it and a concrete operation executor is registered.
- AC-017: Clean and upgraded SQL state contains required ledger scope columns, deterministic legacy backfill, and scope-aware unique keys for outbox, transfer, and inbox rows.
- AC-018: Outbox events publish ledger-generation headers; consumers propagate them to EventStore metadata and reject stale generations without effects while committing the durably disposed delivery.
- AC-019: Two otherwise identical operations in different ledger generations use different EventStore streams/idempotency domains, while `default`/`legacy` resolves to the exact historical stream name.
- AC-020: Mongo accounts, categories, contractors, payment history, projection heads/accounts, and projection audit rows expose ledger scope and use scope-aware uniqueness.
- AC-021: Command status and mutation responses return ledger ID, ledger-generation ID, and generation token with the same strong ETag; stale tokens and missing tokens outside generation zero return HTTP 412 without accepting a command.
- AC-023: A deterministic race that activates a replacement generation after request admission but before the SQL acceptance transaction leaves the old and new generations without the attempted command.
- AC-022: Account-topic messages carry ledger scope; stale account deliveries durably record a terminal duplicate outcome before offset commit and do not reach the account mutation handler.
- AC-024: After forced lease expiry and takeover, the stale executor is cancelled, its failure update cannot change the journal, its fence token is lower than the takeover token, and a replacement owner can resume and settle the same operation.
- AC-025: Representative `IOException`, `HttpRequestException`, and provider-SDK exceptions settle the affected operation as `failed`; the same hosted worker then processes and reconciles a later queued operation.

## Delivered Schema

| Table | Responsibility | Principal protections |
|---|---|---|
| `DataProtectionOperations` | Durable long-operation journal | constrained kinds/states, immutable target/plan binding, row version, scope/state index |
| `DataProtectionLeases` | Cross-replica exclusion and fencing | unique scope, unique monotonic fence token, operation FK, expiry ordering |
| `ActiveLedgerGenerations` | Current generation registry | one active row per environment/ledger, generation uniqueness, required unique server-issued token, row version |
| `ComponentReceipts` | Component watermarks/cutoff evidence | operation FK, unique component receipt, valid JSON watermark |
| `ImportOutcomes` | Per-entity import identity and settlement | source/idempotency uniqueness, fingerprint and state constraints |
| `RecoveryManifests` | Recovery-set publication metadata | operation uniqueness, JSON/checksum/status/cutoff/publication constraints |
| `ProtectedAuditEntries` | Protected administrative audit | operation FK, fingerprint/cutoff checks, update/delete rejection trigger |

The database recovery model and production principal grants are deliberately not changed by this schema migration. Full-recovery/log-chain configuration, least-privilege roles, independently stored audit/catalog copies, and provider validation remain later operational/security tasks; this increment does not claim PITR readiness.

## Schema Compatibility and Deployment Order

The workspace migrations are the production sequence. The repository-local integration migrations are byte-identical test twins and must not be applied as a separate production sequence.

| Production migration | Integration twin | Database and compatibility purpose |
|---|---|---|
| Root `V21__Add_data_protection_control_plane.sql` | Accounting `V9__Add_data_protection_control_plane.sql` | Additively creates `HomeBudget.DataProtection`, its seven control tables, constraints, indexes, and append-only/immutable-binding triggers. It does not clear ledger data. |
| Root `V22__Add_ledger_generation_scope.sql` | Accounting `V10__Add_ledger_generation_scope.sql` | Backfills `default`/`legacy`, makes Accounting SQL scope explicit, replaces unscoped uniqueness with ledger-generation keys, adds a unique generation token, and seeds generation zero. It requires V21/V9's control database and registry. |
| Root `V23__Add_rates_commit_receipts.sql` | Rates `V16__Add_rates_commit_receipts.sql` | Adds the Rates commit-order receipt table used by the coordinated frontier. It is owned by Rates at runtime but ordered in the shared production migration chain. |
| Root `V24__Add_data_protection_plan_confirmations.sql` | Accounting `V11__Add_data_protection_plan_confirmations.sql` | Adds immutable, expiring, actor/target/generation/plan-bound confirmation records and supports atomic one-use consumption with operation enqueue. It requires V21/V9. |
| Root `V25__Add_account_delivery_dispositions.sql` | Accounting `V12__Add_account_delivery_dispositions.sql` | Adds queryable, generation-scoped terminal account-delivery dispositions outside ledger reset scope. It binds message identity to the exact payload hash and records idempotent redelivery before Kafka offset completion. It requires V21/V9. |

Generation zero is the compatibility boundary: `EnvironmentId=default`, `LedgerId=default`, `LedgerGenerationId=legacy`, generation number `0`, and raw token `legacy-token-v1` transported as the exact strong ETag `"legacy-token-v1"`. Existing EventStore stream names stay unchanged for this generation. Later generations use server-issued unique tokens and isolated stream names. The existing payment-history publication `GenerationId` is a different concept and must never be migrated, compared, or serialized as `LedgerGenerationId`.

Deploy this foundation in the following order through the normal guarded release process:

1. Apply root V21, then V22, then V23, then V24, then V25. Verify the schema objects, legacy backfill, generation-zero row/token, migration parity, Rates receipt table, plan-confirmation table, and account-delivery disposition table before replacing any process.
2. Deploy the generation-aware Accounting OperationsConsumer worker before the Accounting API while generation zero remains active. The new worker can adopt legacy unscoped deliveries only while `legacy` is active, which lets it safely drain messages produced by the preceding API version.
3. Deploy the matching Accounting API. API and worker images must come from one compatible candidate; do not activate a non-legacy generation during a mixed-version rollout.
4. Deploy the Rates API only after V23 exists. Gateway and SPA changes follow their own later tasks; destructive administrative routes remain disabled.

Rollback is binary/configuration rollback while generation zero is still active and no later-generation work has been accepted. The additive migrations remain in place; do not drop the control database, generation columns, keys, or receipt table as a rollback shortcut.

Readiness fails closed. The worker is disabled by default, and its current executor is deliberately unsupported. A missing `HomeBudget.DataProtection` database, missing configured active-generation row, invalid generation configuration, or absent Rates receipt schema is an activation failure, not permission to fall back to an implicit writable generation. Enabling the worker or advertising administrative data-protection readiness additionally requires the later authorization, provider, independent-copy, key, reconciliation, and restore-drill prerequisites. Ordinary API health alone is not evidence that those capabilities are ready.

## Failure Scenarios and Edge Cases

- FAIL-001: Missing control database or table fails with an explicit assertion naming the absent capability; it is not reported as a generic connection failure.
- FAIL-002: A stale worker holding an earlier state cannot overwrite a later durable state.
- FAIL-005: A stale worker that loses its lease during an external effect receives cancellation and cannot transition, cancel, fail, renew, or release work under the replacement owner's fence.
- FAIL-006: An executor/provider exception is an operation failure, not a background-host termination signal; the worker logs the failure without payload data and continues bounded polling.
- FAIL-003: A stale plan confirmation cannot start an operation after its plan fingerprint changes.
- FAIL-004: Resetting ledger-owned stores cannot erase the protected recovery/control journal.
- EDGE-001: Tests use unique operation, audit, and scope identities so repeated runs do not depend on cleanup of immutable records.

## Test Strategy

Use the existing `TestContainersService` and its process-owned SQL Server container. The fixture itself is the positive disposable-target identity; no hostname, Docker alias, external endpoint, or live configuration is accepted. Tests connect to `master` only to distinguish an absent control database from a fixture failure, then exercise the real SQL transaction/constraint boundary. Processor tests use controlled task-completion checkpoints. The lease-renewal test deliberately waits beyond a short initial lease interval; the takeover regression advances SQL lease time against this positively identified disposable database and verifies the monotonic fence. Hosted-worker regressions use real scoped processors and durable journals, inject representative provider exceptions, and wait on durable state rather than arbitrary completion timing. Status polling uses explicit timeout and interval bounds.

The upgrade scenario is destructive only to `HomeBudget.DataProtection` inside that process-owned container. The test project disables parallelization, the fixture is explicitly non-parallel, initial clean-schema evidence is captured in one-time setup, and a `finally` block reapplies V9 so test ordering or a failed assertion cannot leave the shared disposable fixture without its control schema.

## Implementation Plan

1. RED: add focused persistence tests under `HomeBudget.Accounting.Api.IntegrationTests/DataProtection` and prove they compile and fail because `HomeBudget.DataProtection` is absent.
2. GREEN schema: add byte-identical additive root V21/integration V9 migrations and bring the persistence contracts green.
3. GREEN component (task 1.3): implement the smallest control-plane repository/worker state machine satisfying restart, cancellation, polling, and cross-replica lease contracts.
4. REFACTOR/verify (later tasks): add authorized public API behavior and concrete operation-kind executors, then run the full Accounting verification gate.
5. Task 1.4 RED: establish compile-valid generation compatibility tests at SQL, outbox/transfer/inbox, Kafka/EventStore, Mongo, and command API boundaries.
6. Task 1.5 GREEN: add upgrade-safe SQL scope, server-owned active-generation resolution, exact generation ETags, scoped messaging/EventStore behavior, and scoped Mongo reads/writes/indexes while preserving legacy stream names.

## Verification Strategy

- Focused command: `dotnet test HomeBudget.Accounting.Api.IntegrationTests/HomeBudget.Accounting.Api.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~DataProtectionControlPlanePersistenceTests`.
- Task 1.1 RED evidence: project compiled and all five selected tests failed with the named missing control-database assertion.
- Task 1.2 GREEN command: `dotnet test HomeBudget.Accounting.Api.IntegrationTests/HomeBudget.Accounting.Api.IntegrationTests.csproj --configuration Release --no-restore --filter FullyQualifiedName~DataProtectionControlPlane --logger "console;verbosity=minimal"`.
- Expected task 1.2 result: all persistence and migration tests pass against a fresh Testcontainers SQL Server.
- Task 1.3 focused command: `dotnet test HomeBudget.Accounting.Api.IntegrationTests/HomeBudget.Accounting.Api.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~DataProtectionOperationComponentTests" --logger "console;verbosity=minimal"`.
- Task 1.3 combined command: `dotnet test HomeBudget.Accounting.Api.IntegrationTests/HomeBudget.Accounting.Api.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~DataProtection" --logger "console;verbosity=minimal"`.
- Expected task 1.3 result: seven component tests and all sixteen DataProtection tests pass against the disposable SQL Testcontainer.

## Requirement Traceability

| Requirement / Criterion | Implementation | Test or evidence | Status |
|---|---|---|---|
| REQ-001, AC-001 | V21/V9 `DataProtectionOperations` | `OperationTransition_WhenCommitted_SurvivesReconnectAndRejectsStaleState` | GREEN schema |
| CONS-002, AC-002 | V21/V9 `DataProtectionLeases` | `LeaseAcquisition_WhenScopeAlreadyOwned_RejectsSecondReplica` | GREEN schema |
| CONS-001, AC-003 | Conditional update contract | `OperationStart_WhenPlanFingerprintIsStale_IsRejectedWithoutStateChange` | GREEN schema |
| SEC-001, AC-004 | `TR_ProtectedAuditEntries_AppendOnly` | `ProtectedAudit_WhenUpdateOrDeleteIsAttempted_RemainsImmutable` | GREEN schema |
| REL-001, AC-005 | Dedicated control database | `LedgerReset_WhenControlRowsExist_PreservesOperationAndProtectedAudit` | GREEN schema |
| REQ-002, AC-006 | V21/V9 seven-table schema | `FreshMigration_WhenFixtureStarts_HasRequiredTablesKeysConstraintsAndIndexes` | Verified |
| REL-002, AC-007 | Idempotent V21/V9 batches | `MigrationScript_WhenAppliedAgain_IsIdempotentAndPreservesSchema` | Verified |
| CONS-003, AC-008 | state check and immutable-binding trigger | `OperationConstraints_WhenStateOrPlanBindingIsInvalid_RejectMutation` | Verified |
| REQ-002, AC-009 | additive V9 upgrade | `UpgradeMigration_WhenV0ThroughV8StateExists_AddsControlPlaneWithoutChangingAccountingState` | Verified |
| REQ-003, AC-010 | `SqlDataProtectionOperationStore`, `DataProtectionOperationProcessor` | `Restart_WhenNewStoreAndProcessorAreCreated_ResumesTheSameOperationId` | Verified |
| REQ-003, AC-011 | `DataProtectionOperationTransitions`, conditional update | `Transition_WhenRequested_IsDurableAndRestrictedToTheLegalStateGraph` | Verified |
| REL-003, AC-012 | cancellation flag plus processor checkpoints | `Cancellation_WhenRequestedDuringExecution_IsHonoredAtTheNextSafeCheckpoint` | Verified |
| CONS-004, AC-013 | renewable unique scope lease | `Lease_WhenAnotherReplicaOwnsTheScope_ExcludesConcurrentProcessing` | Verified |
| CONS-004, AC-014 | leased-scope exclusion in next-work query | `Selection_WhenOldestScopeIsLeased_ProcessesAnotherRunnableScope` | Verified |
| CONS-005, AC-024 | fence-bound SQL transitions plus executor cancellation on renewal loss | `Lease_WhenExpiredLeaseIsTakenOver_StaleOwnerCannotFailOrContinueTheOperation` | Verified |
| REL-004, AC-025 | durable failure settlement and catch/re-poll background loop | `Worker_WhenExecutorThrowsUnexpectedProviderFailure_JournalsFailureAndContinuesPolling` (`io`, `http`, `provider`) | Verified |
| REQ-004, AC-015 | row-version bounded polling | `StatusPolling_WhenStateChanges_ReturnsTheDurableUpdatedStatus`; `StatusPolling_WhenNoStateChanges_ReturnsWithinTheConfiguredBound` | Verified |
| AC-016 | worker DI registration and validated disabled-by-default options | Accounting solution build | Verified |
| GEN-001, AC-017 | V22/V10 additive Accounting scope migration and deterministic generation-zero seed | `LedgerGenerationCompatibilityTests` | Verified |
| GEN-002, AC-017 | scoped SQL repositories, transfer joins/PK, outbox/inbox keys, and no-default required scope | `OutboxPaymentStatusServiceTests`; `PaymentMessageInboxServiceTests`; `TransferCommandStoreGenerationTests`; `LedgerGenerationCompatibilityTests` | Verified |
| GEN-003, GEN-004, AC-018, AC-019 | Kafka headers, EventStore metadata, exact legacy names, later-generation names, stale terminal inbox disposition | `PaymentCommandOutboxTests`; `PaymentOutboxPublisherTests`; `PaymentOperationsConsumerTests`; `PaymentOperationsDeliveryHandlerTracingTests` | Verified |
| GEN-005, AC-020 | scoped Mongo authoritative/projection reads, writes, adoption, and unique indexes | `LedgerGenerationCompatibilityTests`; `MongoDocumentUniquenessTests` | Verified |
| GEN-006, AC-021 | protected active-generation resolver, unique token, payment/transfer status ETag, exact `If-Match` guard | `LedgerGenerationCompatibilityTests`; `PaymentOperationsControllerTests`; `CrossAccountTransferControllerTests` | Verified |
| GEN-008, AC-023 | admission-bound expected generation and SQL active-generation guard in the outbox/complete-transfer acceptance transaction | deterministic handler/batch-SQL tests; generation-switch integration race retained for disposable execution | Verified (unit/component); isolated runtime pending |
| GEN-007, AC-022 | account Kafka producer scope headers and consumer active-generation fence | `AccountGenerationScopeTests` | Verified |

## Task 2.1 Authorization and Confirmation RED Contract

### Current Behavior and Boundary

- **CONFIRMED:** Accounting calls authorization middleware but does not register authentication, OIDC validation, named data-protection policies, recent-auth evaluation, or an administrative data-protection controller.
- **CONFIRMED:** the canonical direct Accounting route prefix is `/data-protection/v1`; the Gateway route is a separate later boundary.
- **CONFIRMED:** durable operation creation is observable in `HomeBudget.DataProtection.dbo.DataProtectionOperations`; a full ordered snapshot including row version detects creation and mutation.
- **DECIDED for the cross-repository contract:** the configured administrator value is `data-protection-administrator` and the required administrative scope is `home-ledger.data-protection.admin`; the administrator claim type remains issuer-configurable.

### Security Requirements and Acceptance Criteria

- AUTH-001: an anonymous caller reaching Accounting directly receives HTTP 401 and cannot create or mutate a control operation.
- AUTH-002: an authenticated caller without the data-protection administrator role receives HTTP 403 and cannot create or mutate a control operation.
- AUTH-003: a destructive-plan caller whose numeric OIDC `auth_time` is outside the configured recent-authentication window receives HTTP 403 and cannot create or mutate a control operation.
- CONF-001: an execution confirmation is one-use and is bound to its actor, operation kind, exact target environment/ledger, ledger generation, expiry, and canonical plan fingerprint.
- CONF-002: reuse or any target, generation, or plan-fingerprint change receives a non-404 client rejection and creates no operation or additional durable effect.
- TEST-SEC-001: integration authentication is test-only, derives claims from explicit test headers, and does not weaken or substitute for production OIDC validation.

### RED Verification and Traceability

| Requirement | Test | Durable no-effect evidence | Task 2.1 status |
|---|---|---|---|
| AUTH-001 | `Plan_WhenCalledDirectlyWithoutAuthentication_ReturnsUnauthorizedWithoutOperationMutation` | complete operation-table snapshot before/after | RED — route currently returns 404 |
| AUTH-002 | `Plan_WhenCallerLacksAdministratorRole_ReturnsForbiddenWithoutOperationMutation` | complete operation-table snapshot before/after | RED — route currently returns 404 |
| AUTH-003 | `Start_WhenAuthenticationIsStale_ReturnsForbiddenWithoutOperationMutation` | complete operation-table snapshot before/after | RED — plan route currently returns 404 |
| CONF-001 | `Start_WhenConfirmationIsReused_RejectsSecondUseWithoutAnotherOperationEffect` | exactly one row after first acceptance; identical snapshot after retry | RED — plan route currently returns 404 |
| CONF-001, CONF-002 | `Start_WhenConfirmedTargetChanges_RejectsWithoutOperationMutation` | complete operation-table snapshot before/after | RED — plan route currently returns 404 |
| CONF-001, CONF-002 | `Start_WhenConfirmedGenerationChanges_RejectsWithoutOperationMutation` | complete operation-table snapshot before/after | RED — plan route currently returns 404 |
| CONF-001, CONF-002 | `Start_WhenPlanFingerprintChanges_RejectsWithoutOperationMutation` | complete operation-table snapshot before/after | RED — plan route currently returns 404 |

Focused command: `dotnet test HomeBudget.Accounting.Api.IntegrationTests/HomeBudget.Accounting.Api.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~DataProtectionAuthorizationApiTests" --logger "console;verbosity=minimal"`.

Task 2.1 evidence: the integration project builds successfully; all seven selected tests execute against the positively identified disposable Testcontainers SQL fixture and fail for the expected missing `/data-protection/v1` plan/start surface (HTTP 404 instead of the required 401/403/200/202 behavior). Production authorization and API behavior remain deliberately unimplemented until tasks 2.2 and 2.3.

## Tasks 2.2 and 2.3 Authorized Administration API

### Implemented boundary and decisions

- `/data-protection/v1` is the direct Accounting prefix. Every capability, plan, start, status, and cancellation route requires the named `DataProtectionAdministrator` policy; destructive start additionally requires `DataProtectionDestructive`, which validates numeric OIDC `auth_time` against the configured recent-authentication window.
- The production scheme uses ASP.NET Core JWT bearer validation with explicit issuer, audience, signing-key, lifetime, subject-name, and issuer-configurable administrator-claim settings. The canonical required scope is `home-ledger.data-protection.admin`. Integration authentication exists only in the `IntegrationTesting` host profile.
- `DataProtectionApi.Enabled` defaults to `false`. An enabled non-test profile fails option validation unless HTTPS authority, audience, administrator claim type/value, canonical scope, recent-authentication window, confirmation lifetime, and clock skew are valid.
- V11 adds `DataProtectionPlans` to the protected `HomeBudget.DataProtection` database. Only the SHA-256 confirmation-token fingerprint is stored. Immutable plan binding covers actor, operation kind, environment, ledger, generation, expiry, and canonical plan fingerprint.
- V12 adds `AccountDeliveryDispositions` to the protected `HomeBudget.DataProtection` database. The unique message/ledger/generation binding stores only a SHA-256 payload fingerprint plus bounded transport coordinates and redelivery count. An identical retry updates observation metadata; changed content under the same identity raises a conflict and leaves Kafka uncommitted. The disposition is written before account-topic offset completion and remains outside selected-ledger reset scope.
- Confirmation consumption, operation enqueue, and protected audit append occur inside one SQL transaction under `UPDLOCK, HOLDLOCK`; concurrent replicas cannot create two financial/control effects from one confirmation. Invalid, expired, consumed, or mismatched confirmation failures deliberately return one bounded conflict response without exposing which binding or secret failed.
- Accepted operations expose durable polling and safe cancellation through the existing operation store. The status URI is explicit and stable, avoiding MVC async-action-name route generation after the transaction commits.
- Capability reporting is intentionally truthful: the default unsupported executor advertises no executable operation kinds, reports `blocked` with `operation-executors-not-configured`, and plan/start return 503. Recognized contract kinds remain visible for negotiation, but the service does not advertise unavailable backup/import/reset/PITR work as executable.

### Traceability and verification evidence

| Requirement | Implementation | Focused evidence | Status |
| --- | --- | --- | --- |
| AUTH-001, AUTH-002 | named administrator policy on the controller; JWT bearer middleware ordering | anonymous, wrong-role, and missing-scope tests with complete operation snapshots | GREEN |
| AUTH-003 | `RecentAuthenticationHandler` and destructive named policy | stale `auth_time` rejection with no durable mutation | GREEN |
| CONF-001, CONF-002 | V11 plan table, hashed token, immutable binding, atomic SQL consume/enqueue/audit | sequential reuse, concurrent reuse, changed actor/operation/target/generation/fingerprint tests | GREEN |
| REQ-004 | versioned plan/start/get/cancel/capability DTOs and controller | accepted operation polling and cancellation contract test | GREEN |
| OPS truthful-capability rule | executor-supported-kind contract and empty production unsupported executor | blocked capability plus 503 plan/no-mutation test | GREEN; concrete executors remain later work |
| SEC production fail-closed | validated `DataProtectionApiOptions`; production JWT bearer registration | disabled-default, enabled-missing-config, and canonical-scope tests | GREEN |

Focused authorization/capability/configuration command: `dotnet test HomeBudget.Accounting.Api.IntegrationTests/HomeBudget.Accounting.Api.IntegrationTests.csproj --configuration Release --no-build --filter "FullyQualifiedName~DataProtectionAuthorizationApiTests|FullyQualifiedName~DataProtectionBlockedCapabilityApiTests|FullyQualifiedName~DataProtectionSecurityConfigurationTests" --logger "console;verbosity=minimal"` — 18/18 passed against disposable Testcontainers.

Focused control/migration/worker regression command: `dotnet test HomeBudget.Accounting.Api.IntegrationTests/HomeBudget.Accounting.Api.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~DataProtectionControlPlaneMigrationTests|FullyQualifiedName~DataProtectionControlPlanePersistenceTests|FullyQualifiedName~DataProtectionOperationComponentTests|FullyQualifiedName~DataProtectionOperationWorkerTests" --logger "console;verbosity=minimal"` — 20/20 passed.

Deployment order: apply byte-identical root `V24__Add_data_protection_plan_confirmations.sql` / integration `V11__Add_data_protection_plan_confirmations.sql` before deploying the API. Both currently have SHA-256 `C144EB3447FB0129EC8FCDF1E533BA6DEC30E48D33521B5F1F16963D88E0498C`. Issuer/audience/claim configuration and concrete operation executors are separate prerequisites; until executors exist, the capability remains correctly blocked.

## Task 3.1 Portable Contract v1

### Scope and confirmed boundary

- **CONFIRMED:** the root portability design defines a business-state snapshot, not an original event-history archive, and requires an application-owned format independent of SQL tables, Mongo collections, EventStore streams, and .NET runtime type names.
- **CONFIRMED:** format v1 has no attachment subsystem. The manifest must declare `unsupported-not-present`, and `attachments.jsonl` is not an allowed entry.
- **CONFIRMED:** no versioned portability schema or JSON Schema validator previously existed in this repository.
- This task owns only the static v1 contract, deterministic synthetic sample, and contract-validation tests. ZIP inspection/extraction, export generation, import execution, reconciliation behavior, and runtime API surfaces remain later tasks.

### Requirements and acceptance criteria

- PORT-001: `contracts/home-ledger-portability/v1` contains JSON Schema 2020-12 schemas for the manifest, accounts, currencies, categories, contractors, operations, rates, settings, lineage, and reconciliation records.
- PORT-002: the manifest identifies format/profile/version, full-versus-partial completeness, export and ledger/generation identities, UTC cutoff/watermark, a checksummed file inventory, reconciliation entry, and attachment status without physical-store or runtime-type names.
- PORT-003: financial values are canonical invariant decimal strings; business dates are date-only; system timestamps are UTC; UUID identities, references, transfer sides/rate/fee, splits, adjustments, opening balance, archived semantics, and migration lineage are explicit.
- PORT-004: a machine-readable entry catalog is deny-by-default, identifies required full-package entries and record schemas, permits only documented optional CSV analysis files, and excludes attachment and nested-archive entries.
- PORT-005: the compatibility matrix accepts `1.0.x` for v1 state import, rejects unknown major/minor versions by default, and never treats a partial package as a complete replacement.
- PORT-006: the deterministic synthetic full-package directory contains fixed identities/timestamps, every required entry, exact record counts and SHA-256 values, representative operations, and no credentials or attachment payload.
- AC-PORT-001: every JSON/JSONL sample instance validates against its declared schema with JSON Schema 2020-12 evaluation.
- AC-PORT-002: the sample contains exactly the catalog's required entries, the manifest inventory matches file bytes/counts, all references resolve, and `attachments.status` equals `unsupported-not-present`.
- AC-PORT-003: repeated validation reads the same committed sample bytes and hashes; no test creates or mutates an archive or target store.

### Test and implementation plan

1. RED: add focused API-unit contract tests and a test-only JSON Schema 2020-12 validator dependency; prove they fail because the versioned contract root is absent.
2. GREEN: add the smallest self-contained schema/catalog/compatibility/README/sample tree needed by the tests.
3. REFACTOR: review the catalog and schemas for closed-world entry/type handling, financial fidelity, secret exclusion, and future task 3.3 streaming-validation use without implementing that validator now.

Focused command: `dotnet test HomeBudget.Accounting.Api.Tests/HomeBudget.Accounting.Api.Tests.csproj --configuration Release --filter FullyQualifiedName~PortabilityContractTests --logger "console;verbosity=minimal"`.

### Traceability and verification evidence

| Requirement | Repository evidence | Verification |
| --- | --- | --- |
| PORT-001, AC-PORT-001 | `contracts/home-ledger-portability/v1/schemas/*.schema.json`; packaged schema copies; `PortabilityContractTests.FullSample_WhenValidated_EveryStructuredRecordConformsToItsSchema` | JSON Schema 2020-12 validation with required format checks passes for every JSON/JSONL instance. |
| PORT-002, PORT-006, AC-PORT-002, AC-PORT-003 | `samples/full-ledger/manifest.json`; deterministic sample files; `PortabilityContractTests.Manifest_WhenVerified_MatchesSampleBytesCountsAndAttachmentPolicy`; `PortabilityContractSampleIntegrityTests` | Exact entry set, bytes, SHA-256 values, JSONL record counts, packaged schema identity, reconciliation counts, and dependency closure are verified without mutating the sample. |
| PORT-003 | Record schemas, `operations.jsonl`, `rates.jsonl`, `lineage.jsonl`, and the contract README | The representative opening balance, income, split expense, adjustment, multi-currency transfer, fee, rate, archived reference, and lineage records pass schema and reference validation. |
| PORT-004 | `entry-catalog.json`; `PortabilityContractTests.FullSample_WhenComparedWithCatalog_ContainsExactlyRequiredEntries` | The full sample is exactly the required allowlist; unknown entries are rejected and attachment entries are absent. |
| PORT-005 | `compatibility-matrix.json`; `PortabilityContractTests.CompatibilityMatrix_WhenRead_DefinesStrictV1ImportPolicy` | Only `1.0.x` state imports are declared, partial replacement is rejected, unknown versions fail closed, and original-history recreation is not claimed. |

- RED evidence: the first behavioral run completed with 5/5 expected failures because `contracts/home-ledger-portability/v1` did not exist.
- GREEN evidence: the focused `FullyQualifiedName~PortabilityContract` run completed with 7/7 passing tests after the contract and deterministic sample were added.
- Dependency decision at the task 3.1 checkpoint: `JsonSchema.Net` 9.4.0 was test-only. Task 3.3 subsequently promotes the same centrally pinned version to Infrastructure for production contract evaluation; the task 3.3 section records that rationale.
- Repository status: task 3.1 is locally verified in Accounting; the root OpenSpec checkbox remains owned by the cross-repository coordinator.

## Task 3.2 Untrusted Archive RED Contract

### Scope and security boundary

- **CONFIRMED:** SEC-02 and AT-15 require central-directory and streamed-entry limits before extraction or target mutation. This task owns only compile-valid RED unit/security tests; task 3.3 owns the production inspector, extraction, hashing, cleanup, and validation implementation.
- Test archives remain in memory. Each extraction destination is nested in a process-created OS temporary directory carrying a per-instance identity marker. Recursive cleanup refuses a root outside the exact OS temp parent, a missing/mismatched marker, or a reparse-point root.
- After every rejection, the fixture requires the marked envelope to contain only its marker and empty extraction directory. Traversal probes are bounded so a vulnerable implementation cannot intentionally target repository or live storage.
- The tests do not connect to databases, services, Docker, or any Home Ledger environment.

### Required production seam for task 3.3

The compile-valid RED bridge resolves these internal Infrastructure types by reflection so production code is not added prematurely:

- `HomeBudget.Accounting.Infrastructure.DataProtection.Portability.PortabilityPackageInspector`
- `HomeBudget.Accounting.Infrastructure.DataProtection.Portability.PortabilityPackageInspectionOptions`
- constructor `PortabilityPackageInspector(PortabilityPackageInspectionOptions)`
- `Task InspectAndExtractAsync(Stream archive, string extractionRoot, CancellationToken cancellationToken)`

Rejected packages expose a stable string `Code` on their validation exception. Limit options are `MaxEntryCount`, `MaxArchiveCompressedBytes`, `MaxEntryCompressedBytes`, `MaxTotalUncompressedBytes`, `MaxEntryUncompressedBytes`, `MaxExpansionRatio`, `MaxJsonLineBytes`, `MaxJsonlRecordCount`, and `MaxJsonDepth`. Cancellation escapes as `OperationCanceledException`, not as a validation result.

### Requirements and acceptance criteria

- ARCSEC-001: reject forward/backslash and percent-encoded traversal with `archive.path-traversal`; reject root, drive-root, UNC, drive-relative, and alternate-data-stream syntax before extraction.
- ARCSEC-002: reject symbolic links, exact duplicate entries, ordinal-ignore-case collisions, extension- or signature-detected nested archives, unknown catalog entries, and executable/unsupported entry types before extraction.
- ARCSEC-003: independently enforce entry count, archive/entry compressed bytes, aggregate/entry uncompressed bytes, expansion ratio, UTF-8 JSONL line bytes, JSONL record count, and JSON depth limits with stable error codes.
- ARCSEC-004: reject runtime `$type` metadata before deserializing arbitrary types, and reject a manifest/entry SHA-256 mismatch.
- ARCSEC-005: observe both an already-cancelled token and cancellation delivered during archive reading, then leave no extracted or staging file behind.
- AC-ARCSEC-001: all attack fixtures are in-memory or under a positively identified disposable root and never leave an output file outside the approved extraction result.
- AC-ARCSEC-002: tests compile without production inspector types, then fail only because the expected task 3.3 production seam is absent.

### Traceability and RED evidence

| Requirement | Focused evidence | Expected task 3.3 result |
| --- | --- | --- |
| ARCSEC-001 | `PortabilityPackagePathSecurityTests` traversal/root/UNC/drive/ADS cases | Exact path classification code; no archive output remains. |
| ARCSEC-002 | link, duplicate, case collision, nested extension/signature, unknown entry, and executable-entry cases | Rejection precedes extraction and catalog completeness handling. |
| ARCSEC-003 | `PortabilityPackageResourceLimitTests` with one independently lowered option per test | Exact configured limit code; bounded processing. |
| ARCSEC-004 | `PortabilityPackageContentSecurityTests` runtime metadata and checksum mutations | No runtime type materialization; checksum mismatch is explicit. |
| ARCSEC-005, AC-ARCSEC-001 | pre-cancel and read-triggered cancellation plus `DisposablePortabilityTestRoot` teardown | `OperationCanceledException`; marker is the only remaining file. |

- RED command: `dotnet test HomeBudget.Accounting.Api.Tests/HomeBudget.Accounting.Api.Tests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~PortabilityPackage" --logger "console;verbosity=minimal"`.
- RED result: 30/30 tests fail with `Expected production portability inspection contract '...PortabilityPackageInspector' is missing.` This is the intended missing behavior, not a compile or fixture failure.
- Existing-regression command: the same project with filter `FullyQualifiedName!~PortabilityPackage` passes 83/83 tests.
- Task 3.2 remains intentionally RED until task 3.3 implements the bounded inspector. The root OpenSpec checkbox remains owned by the cross-repository coordinator.

## Task 3.3 Bounded Portability Package Inspection

### Scope and trust boundary

- The Accounting Infrastructure owns inspection and extraction of the v1 application interchange package. This boundary validates an archive before any record can reach an import handler; it does not perform an import, activate a ledger generation, or accept raw database content.
- The inspector accepts a readable ZIP stream and an existing, empty, non-root, non-reparse-point destination. It creates a random marker-bound staging directory as a direct child, streams entries into that directory, validates the complete package, and promotes only validated top-level content.
- Extraction is deny-by-default against the embedded v1 entry catalog. Entry paths are canonicalized and checked before filesystem use; links, traversal, absolute/UNC/drive/alternate-stream paths, duplicate or case-colliding names, directories, executable types, unknown entries, and nested archives by extension or signature are rejected.
- Limits are independently configurable for archive and entry compressed bytes, entry and aggregate uncompressed bytes, entry count, expansion ratio, JSONL line bytes and record count, and JSON depth. Cancellation is observed during archive copying, central-directory inspection, extraction, schema loading, and record validation.
- Each entry is SHA-256 hashed while it is streamed. The manifest must validate, describe the exact package closure, match catalog media/schema declarations, and match extracted byte counts, record counts, and hashes. Packaged schemas must be byte-identical to the embedded normative schemas before they are evaluated.
- Failures expose stable codes and optional entry/record/field coordinates without echoing archive payloads. Runtime `$type` metadata is recursively rejected and no package value is deserialized into an arbitrary runtime type.

### Staging, cleanup, and permissions

- The caller-owned destination is never recursively deleted. Failed inspection removes only marker-verified staging paths created by this operation. A promotion failure removes only the direct-child paths recorded as promoted, then cleans the verified staging directory.
- If verified cleanup itself fails, the inspector attempts to rename the staging directory to a marker-derived quarantine path under the same caller root and returns `archive.cleanup-failed`; it never broadens cleanup outside the positively identified destination.
- Unix staging directories and files are restricted to owner-only `0700` and `0600`. Windows staging directories and files receive a protected DACL granting full control only to the current service identity; this is applied immediately after creation within the caller-protected root. Failure to establish the protection fails inspection closed.
- Local runtime validation executed the Windows protected-DACL branch. The Unix `0700`/`0600` branch is compile-verified but still requires a disposable Linux runtime check before production adoption; no Linux or live environment was accessed for this task.
- Successful validation moves the complete staged package into the empty destination, then removes the staging identity. No partial package is visible before validation completes.

### Production dependency decision

`JsonSchema.Net` 9.4.0 is now an Infrastructure dependency because task 3.3 must faithfully evaluate the repository's JSON Schema 2020-12 contract at the production trust boundary. Reimplementing references, formats, unevaluated properties, and draft semantics locally would create an unsafe competing validator. Each inspection uses a private schema registry, preventing cross-operation/global registration state while still using the embedded byte-identical contract.

### Traceability and GREEN evidence

| Requirement | Production evidence | Verification |
| --- | --- | --- |
| ARCSEC-001, ARCSEC-002 | `PortabilityPackageInspector.Archive.cs`; embedded `entry-catalog.json` | Traversal/absolute/UNC/drive/ADS, link, duplicate/case collision, nested archive, unsupported entry, and executable cases reject before promotion. |
| ARCSEC-003 | `PortabilityPackageInspectionOptions`; streamed central-directory, extraction, JSON, and JSONL checks | Every independent compressed/uncompressed/count/ratio/line/record/depth limit returns its stable limit code. |
| ARCSEC-004 | `PortabilityPackageInspector.Validation.cs`; incremental SHA-256 extraction | Runtime type metadata, schema mismatch, manifest closure, bytes/counts, and checksum mismatches fail without importing data. |
| ARCSEC-005, AC-ARCSEC-001 | cancellation propagation; marker-verified cleanup/quarantine; `DisposablePortabilityTestRoot` | Pre-cancel and mid-read cancellation leave the disposable envelope empty; no caller-owned or outside path is removed. |
| AC-ARCSEC-002 | 30 task 3.2 security tests plus positive and non-empty-target regressions | The former intentional RED suite is GREEN and a valid deterministic v1 package is promoted byte-for-byte. |

- GREEN focused command: `dotnet test HomeBudget.Accounting.Api.Tests/HomeBudget.Accounting.Api.Tests.csproj --configuration Release --filter "FullyQualifiedName~PortabilityPackage" --logger "console;verbosity=minimal"`.
- GREEN result at the task 3.3 checkpoint was 32/32 focused tests; the later CSV consistency regression brings the current inspector suite to 33/33. The 30 security cases, valid-package promotion, caller-file preservation, and documented CSV package all pass.
- Task 3.3 does not expose an import API, extract attachments, mutate a ledger, or alter the root OpenSpec task state; those remain separately owned work.

## Task 3.4 Portable Export RED Contract

### Scope and production seam

- **CONFIRMED:** this task owns compile-valid RED tests and traceability only. It does not implement export readers, an exporter, archive publication, API endpoints, or ledger mutation. Task 3.5 owns the GREEN implementation.
- The test bridge expects `HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Export.PortabilityExportService`, `IPortabilityExportReader`, `PortabilityExportOptions`, and `PortabilityExportRequest`.
- The service constructor accepts `(IPortabilityExportReader, PortabilityExportOptions)` and exposes `Task`/`Task<T> ExportAsync(PortabilityExportRequest, CancellationToken)`. Required source operations are `AcquireCutoffAsync`, `WaitForProjectionAsync`, and streamed `ReadAccountsAsync`, `ReadCurrenciesAsync`, `ReadCategoriesAsync`, `ReadContractorsAsync`, `ReadSettingsAsync`, `ReadRatesAsync`, `ReadOperationsAsync`, `ReadLineageAsync`, and `ReadReconciliationAsync` methods.
- Data readers return `IAsyncEnumerable<JsonElement>` so authoritative adapters can map store-specific state into the application-owned JSON contract without exposing table, collection, stream, or runtime serialization types. Production must validate every emitted element against the embedded v1 schemas before publication.
- Tests use only deterministic contract fixtures and a marker-verified direct child of the operating-system temporary directory. They do not connect to databases, Docker, external services, or any Home Ledger environment.

### Requirements and acceptance criteria

- EXPRED-001: acquire one actual UTC common cutoff and pass its monotonic sequence to every authoritative reader. If projection state starts behind, wait within policy or use a proven authoritative source; an unresolved lag returns `export.projection-lag` and publishes nothing.
- EXPRED-002: a full export preserves accounts and opening-balance operation identities; currencies, categories, contractors, allowlisted business settings, and every historical rate required by exported values.
- EXPRED-003: preserve income, expense, adjustment, opening-balance, canonical cross-currency transfer identity, both transfer sides, exact exchange rate, fee, split parent/lines, and per-record references without treating derived balances as operations.
- EXPRED-004: preserve explicit archived/deleted tombstones and their UTC timestamps, plus Family Pro 12 source system, run, batch, mapping, source-entity, logical-entity, and migrated-time lineage.
- EXPRED-005: financial decimal values remain invariant strings with their exact source precision; business dates remain `yyyy-MM-dd`; system timestamps remain UTC strings ending in `Z`.
- EXPRED-006: a partial request is labelled `partial`, contains only the selected operation set plus complete account/currency/category/rate/settings/lineage dependency closure, and cannot masquerade as a full replacement package.
- EXPRED-007: source objects may contain infrastructure connection strings, passwords, sessions, tokens, or key material, but none may appear by name or value in any archive entry.
- EXPRED-008: publication is atomic and last. Source failure returns `export.source-read-failed`, and neither final archive nor staging artifact remains in the caller root.
- EXPRED-009: a deterministic 50,000-operation source is consumed as an async stream, observes no final destination during source enumeration, stays below 64 MiB managed-memory growth, records exactly 50,000 manifest/JSONL operations, and completes within the bounded test cancellation window.

### CSV contract reconciliation

The v1 entry catalog documents optional spreadsheet-safe `analysis/*.csv` files and the inspector already maps catalog kind `csv` to `text/csv`. The manifest schema previously omitted `text/csv`, which made a documented package impossible to validate. The schema now permits `text/csv`, its packaged sample copy and manifest size/SHA-256 were updated, and focused contract/inspector regressions prove documented CSV is accepted while `unknownEntries: reject` and the existing unsupported-entry tests remain unchanged.

### Traceability and RED evidence

| Requirement | Focused test evidence | Expected task 3.5 result |
| --- | --- | --- |
| EXP-02/EXP-04, EXPRED-001 | projection catch-up and unresolved-lag tests; reader cutoff observations | One actual cutoff/sequence across every reader; no silent lagged export. |
| EXP-01, EXP-03, EXPRED-002 through EXPRED-005 | accounts/references/settings/rates/operation kinds/transfers/splits/tombstones/Family Pro/string-form tests | Exact v1 identities, relationships, values, timestamps, and lineage. |
| EXP-02/EXP-04, EXPRED-006 | selected-transfer partial-package test | Manifest is partial and includes exactly the required dependency closure. |
| EXP-05, EXPRED-007 | secret-bearing source settings and whole-archive text scan | Only allowlisted business settings are serialized. |
| EXP-02/EXP-04, EXPRED-008 | injected operation-reader failure and marker-verified root | No final or staging artifact after failure; final path appears only after completion. |
| EXP-02/EXP-04, EXPRED-009 | non-parallel 50,000-operation async generator with 64 MiB/120-second bounds | Bounded-memory streaming, exact count, manifest inventory, and publish-last behavior. |

- RED command: `dotnet test HomeBudget.Accounting.Api.Tests/HomeBudget.Accounting.Api.Tests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~PortabilityExport" --logger "console;verbosity=minimal"`.
- RED result: 15/15 tests fail for missing `...Portability.Export.PortabilityExportService`; the scale fixture stops at that seam and does not allocate or publish a 50,000-record archive while RED.
- Existing portability regression: contract plus inspector suites pass 41/41, including documented CSV validation and strict unknown-entry rejection.
- Task 3.4 remains intentionally RED until task 3.5 implements authoritative readers and bounded atomic export. The root OpenSpec checkbox remains owned by the cross-repository coordinator.

## Task 3.5 Bounded Portable Export

### Implemented boundary and decisions

- `PortabilityExportService` owns cutoff settlement, v1 schema validation, deterministic file/manifest generation, protected staging, archive creation, and publish-last activation. It accepts only the application-owned `IPortabilityExportReader`; physical SQL/Mongo/EventStore names and runtime serialization types do not enter the portable contract.
- One `PortabilityExportCutoff` is acquired before source enumeration. A lagging projection must reach the common sequence within the configured timeout or the operation returns `export.projection-lag` without publishing an archive. Every reader receives that same settled cutoff object.
- Full exports stream records directly to JSON Lines with per-record schema validation, bounded buffers, incremental counts, and SHA-256 file inventory. Partial exports buffer only the explicitly selected operation set up to `MaxBufferedRecords`, derive account/currency/category/contractor/rate/lineage dependencies, and label the manifest `partial`.
- Settings use an explicit business allowlist. Connection/authentication fields are not copied. Decimal/date/timestamp strings and logical identities are emitted from the contract record unchanged; reconciliation counts and operation-identity hash are recomputed for the exported set.
- Analysis CSV is generated by bounded rereads of staged JSONL rather than retaining export records in memory. Values beginning with `=`, `+`, `-`, or `@` receive a leading apostrophe only in CSV; JSON remains exact. Stable IDs link accounts, operations, splits, transfers, and rates.
- The output directory receives only the final ZIP after every structured file, packaged schema, README, CSV, reconciliation entry, and manifest is complete. ZIP entry ordering/timestamps are deterministic. Failure removes only a random, marker-verified direct-child staging directory; it never deletes a caller destination created concurrently.
- `RegisterPortabilityExporter<TReader>` is opt-in. Neither runtime host calls it because task 3.5 does not yet have a proven coherent authoritative reader spanning the current SQL acceptance state, EventStore payment authority, Mongo reference authority/projection watermark, and Rates boundary. Export runtime capability therefore remains **BLOCKED** until that adapter and operation-executor wiring are implemented and integration-tested; unit success is not advertised as production availability.

### Traceability and verification evidence

| Requirement | Production evidence | Verification |
| --- | --- | --- |
| EXP-02/EXP-04, EXPRED-001 | `PortabilityExportService.AcquireSettledCutoffAsync`; reader cutoff contract | Projection catch-up uses one sequence/watermark; unresolved lag publishes nothing. |
| EXP-01/EXP-03, EXPRED-002 through EXPRED-005 | streaming writers, embedded v1 contract, unchanged `JsonElement` record emission | Accounts/opening balances, references, exact operation kinds, transfers/sides/rate/fee, splits, tombstones, and Family Pro lineage pass focused assertions. |
| EXP-02/EXP-04, EXPRED-006 | bounded selected-operation buffering and dependency sets | Partial transfer output contains the selected operation plus its two accounts/currencies, fee category, required rate, settings, and applicable lineage. |
| EXP-05, EXPRED-007 | `PortabilityExportJson.FilterSettings` | Whole-archive scan excludes source connection string, password, session-token, and encryption-key names/values. |
| EXP-CSV | `PortabilityExportCsv`; `PortabilityExportFileWriter.EscapeRow` | Formula-looking account text is apostrophe-prefixed in CSV while its JSON value remains byte-for-value exact. |
| EXP-02/EXP-04, EXPRED-008 | marker-verified staging plus final `File.Move` | Injected source failure leaves only the caller marker; the final path is not visible during enumeration. |
| EXP-02/EXP-04, EXPRED-009 | streamed JSONL, bounded CSV reread, incremental SHA/counts | The 50,000-operation case completes below the test's 64 MiB managed-growth ceiling and records exactly 50,000 operations. |
| EXP-01 package closure | exporter output passed back through `PortabilityPackageInspector` | Emitted schemas, media types, record counts, byte sizes, hashes, and allowlisted entries validate as a v1 package. |

- Focused build: `dotnet build HomeBudget.Accounting.Api.Tests/HomeBudget.Accounting.Api.Tests.csproj --configuration Release --no-restore --no-incremental` — succeeded.
- Focused GREEN: `dotnet test HomeBudget.Accounting.Api.Tests/HomeBudget.Accounting.Api.Tests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~PortabilityExport" --logger "console;verbosity=minimal"` — final run 18/18 passed in 27 seconds. This includes the original 15 RED contracts, inspector compatibility, deterministic archive bytes, and formula-safe CSV fidelity.
- Measured representative case: the isolated 50,000-operation test passed in 18,770 ms exporter elapsed time and observed 9,598,912 bytes maximum managed-memory growth, below its approved 64 MiB test ceiling. These are local synthetic measurements, not production RTO evidence.
- API unit regression: the complete `HomeBudget.Accounting.Api.Tests` project passed 140/140 in 23 seconds.
- The original split test assumed every operation had a `splits` property and selected both the active split expense and its synthetic archived tombstone. The test was corrected to select the non-archived record with a present two-line split; production behavior and contract expectations were not weakened.
- Production authoritative-reader integration, API/background-operation wiring, and cross-store cutoff evidence remain later work. No host, database, Docker resource, or live environment was accessed by these tests.

## Tasks 4.1/4.2 Import Preflight and Durable Journal

### Scope contract and observable requirements

- **Goal:** validate an entire v1 business-state package without target mutation and, only for a separately authorized non-validation operation, create a crash-readable immutable import journal outside ledger reset scope.
- **Allowed scope:** Accounting portability-import application code, its opt-in DI registration, focused unit/integration tests, Accounting V13/root V26, and this repository-local specification.
- **Non-goals:** importing business entities, raw database insertion, activation, settlement/projection waiting, reconciliation, retry execution, replacement orchestration, UI/Gateway routes, or advertising import as executable. Those remain tasks 4.3/4.4 and later delivery work.
- **Risk:** CRITICAL. Validation errors must be fail-closed and must not mutate the target. Journal identity, source bytes, target ledger/generation, and per-entity fingerprints must not drift across retry or restart.

| ID | Contract |
| --- | --- |
| IMP-VAL-01 | `validate-only` parses and validates the complete package through an application-owned read-only target probe; the preflight service exposes no target mutation boundary. |
| IMP-VAL-02 | Only `validate-only`, `empty-target`, `resume`, and `replace` are recognized. Implicit merge is rejected. Empty-target import rejects existing business state. Replacement requires a full package, confirmed maintenance isolation, and a valid preoperation recovery-set ID. |
| IMP-VAL-03 | Preflight verifies the v1.0 compatibility range, manifest/file inventory, exact sizes and SHA-256 values, canonical package digest, record limit, storage headroom, unique logical IDs, references, archived semantics, exact decimal precision, split totals, opening/income/expense direction, and canonical transfer/rate/fee invariants. Errors expose bounded code/file/entity/field coordinates and never include payload contents or secrets. |
| IMP-VAL-04 | Resume binds the exact canonical source digest and target generation. A changed source content set or target generation is an explicit conflict. |
| IMP-JRN-01 | A non-validation journal binds operation ID, export ID, canonical package digest, format/completeness/mode, target environment/ledger/generation/number, optional preoperation recovery set, and the complete entity count in `HomeBudget.DataProtection.dbo.PortabilityImportRuns`. |
| IMP-JRN-02 | Every entity has one immutable `ImportOutcomes` binding containing source identity, fingerprint, stable idempotency key, and initial `validated` state. Exact retries return the existing journal; changed bindings conflict without partial rows. |
| IMP-JRN-03 | Journal creation runs in one serializable transaction, first locks and verifies the durable administration operation kind/scope/target, then writes the run, entity outcomes, and protected audit. Restart reads the same durable journal. |

The canonical package digest is SHA-256 over each extracted entry in ordinal relative-path order: UTF-8 normalized relative path, one NUL byte, exact file bytes, and one NUL byte. This intentionally binds every logical package file byte while remaining independent of ZIP container metadata. Individual manifest checksums independently bind every allowlisted entry. File hashing is streamed; cancellation is checked between files, records, and hash buffers. Record materialization remains bounded by the configured `MaxRecords` preflight limit.

### Schema compatibility and deployment order

Accounting `V13__Add_portability_import_journal.sql` and root `V26__Add_portability_import_journal.sql` are the byte-identical migration pair. They require the V9/root V21 control plane, reuse its `ImportOutcomes` and protected audit tables, and follow Accounting V12/root V25. They add `PortabilityImportRuns`, the export/target uniqueness constraint, immutable-run trigger, and immutable entity-binding trigger. Root V27/Accounting V14 coordinated-capture state follows this pair and does not replace it.

Apply root V26 only after V21 through V25 are verified. Verify migration parity and the new keys/triggers before deploying a candidate that registers an import target probe. `RegisterPortabilityImport<TTargetProbe>` is deliberately opt-in and no current production host invokes it; therefore current capability reporting remains fail-closed and does not advertise import/replace execution. Deploying V26 alone is additive and does not enable import. A later task must supply the authoritative target probe, durable executor, operation API integration, settlement/reconciliation, and activation flow before capability status may change.

### RED/GREEN evidence and traceability

- RED preflight: 15 focused contracts failed on the absent `PortabilityImportPreflightService`; RED journal: the durable-store contract failed on the absent `IPortabilityImportJournalStore`.
- GREEN unit: `dotnet test HomeBudget.Accounting.Api.Tests/HomeBudget.Accounting.Api.Tests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~PortabilityImport"` passed 16/16 in 3 seconds.
- API unit regression: the complete `HomeBudget.Accounting.Api.Tests` project passed 162/162 in 24 seconds.
- GREEN disposable SQL: `dotnet test HomeBudget.Accounting.Api.IntegrationTests/HomeBudget.Accounting.Api.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~PortabilityImportJournalTests|FullyQualifiedName~DataProtectionControlPlaneMigrationTests"` passed 9/9 in 5 seconds. It proves exact retry/restart reads, binding conflict, transaction rollback, administration-operation binding, immutable triggers, and migration schema/idempotence in process-owned Testcontainers SQL.
- Both affected test projects built Release with zero errors. V13/V26 are byte-identical with SHA-256 `5728FCDF079468342340351DC1B0DEE384D4FDD788B3F899E25D1F2024F5B022` and contain no accidental patch-marker prefixes.
- No live host, production database, shared Docker resource, or external environment was accessed. Task 4.3 now has a tested execution coordinator; import/replace remains non-executable in production because task 4.4 lacks the required Accounting/Rate application adapter.

| Requirement | Implementation evidence | Status |
| --- | --- | --- |
| IMP-VAL-01..04 | `PortabilityImportPreflightService`, package/rule validators, read-only probe seam, 16 focused preflight tests | Verified locally |
| IMP-JRN-01..03 | V13/V26, `SqlPortabilityImportJournalStore`, five focused disposable SQL journal tests | Verified locally |
| IMP-EXE-01..05 | Coordinator, application adapter contract, durable outcome transitions, nine deterministic execution tests | Verified locally (task 4.3) |
| Production Accounting/Rate execution and real SQL/Kafka/EventStore/Mongo proof | No authoritative ID-preserving application adapter or host registration | BLOCKED (task 4.4) |

## Tasks 4.3/4.4 Import Execution and Resume

### Scope and decisions

- **IMP-EXE-01:** execution uses the application-owned `IPortabilityImportApplicationAdapter`; the coordinator has no raw SQL, Mongo, EventStoreDB, or Kafka business-write boundary. A production adapter must preserve every supplied logical identity, fingerprint, idempotency key, ledger, and generation while enforcing the same authorization and domain validation as ordinary commands.
- **IMP-EXE-02:** dependencies are processed in fixed stages: currencies, categories, contractors, settings, accounts, rates, operations, then lineage. Each stage uses a configured batch size of 1 through 1000. The coordinator never advances to reconciliation while an outcome remains unsettled.
- **IMP-EXE-03:** `accepted` and `persisted` records are status-polled under one bounded settlement deadline. Timeout-after-acceptance and caller cancellation resolve durable status without resubmission; cancellation stops new batches only after the current accepted batch settles.
- **IMP-EXE-04:** resume reruns complete preflight, binds the original package digest and generation, reloads the durable journal, skips `projected` outcomes, settles `accepted`/`persisted` outcomes, and conflicts on changed fingerprints, package bytes, or target generation. Identical reruns have zero additional effect.
- **IMP-EXE-05:** empty-target and replacement share the same coordinator. Replacement additionally requires a full package, fresh recovery-set binding, maintenance isolation, and an inactive target generation. Completion of this slice is `awaiting-reconciliation`; it never activates a generation or reports success.

`RegisterPortabilityImportExecution<TTargetProbe,TApplicationAdapter>` is opt-in. No API or worker host calls it, so capability reporting remains fail-closed. This prevents a deterministic test adapter from being mistaken for a production importer and preserves the distinction between task 4.3 orchestration and task 4.4 cross-store execution.

### RED/GREEN and runtime evidence

- RED: eight focused execution contracts compiled and failed on the intentionally absent executor seam. They covered ordering, batch bounds, safe cancellation, timeout-after-acceptance, identical retry, changed-content conflict, crash/restart, and resume without duplicate effect.
- GREEN: the final focused preflight/execution run passed 25/25 in 4 seconds (16 preflight and nine execution tests). The Release API test project build completed with zero errors.
- Disposable SQL restart coverage passed 1/1 and proves `journaled -> executing -> awaiting-reconciliation` plus `validated -> accepted -> projected` persistence through a new store instance. The Release IntegrationTests project build completed with zero errors after the concurrent capture-admission slice stabilized.
- No live or shared environment was accessed. No Kafka, EventStoreDB, MongoDB, Rates API, or production SQL business state was mutated.

### Capability status

| Capability | Status | Evidence / blocker |
| --- | --- | --- |
| Task 4.3 RED execution contracts | PASS | Eight intentional RED cases followed by nine GREEN execution tests |
| Dependency ordering, bounded batching, cancellation settlement, timeout recovery, idempotent retry, conflict, crash/resume | PASS | Deterministic adapter plus durable journal contract; focused suite 25/25 |
| Empty-target/inactive-replacement coordination through an application boundary | PASS | Opt-in coordinator and adapter contract; replacement stops at `awaiting-reconciliation` |
| Task 4.4 production Accounting/Rate execution | BLOCKED | Accounting ordinary commands do not currently preserve every imported operation/transfer identity, and Rates-owned settings/rates require a cross-service adapter not present in this repository slice |
| Real SQL/Kafka/EventStore/Mongo end-to-end import proof | BLOCKED | Durable SQL journal restart is proven, but no production application adapter may safely drive Kafka/EventStore/Mongo/Rates; direct database insertion is prohibited |
| Reconciliation contract and durable evidence | Verified locally | Task 4.5 bounded coordinator and API-readback seam; production adapter remains blocked |
| Generation activation | Not in scope | No activation path is exposed by the reconciliation boundary |

## Task 4.5 Import Reconciliation

### Contract and decisions

- **IMP-REC-01:** `IPortabilityImportReadbackAdapter` is the only target read boundary. It must normalize supported Accounting and Rates API responses into v1 portable records and the reconciliation summary; the coordinator has no direct business-store access.
- **IMP-REC-02:** reconciliation is allowed only for the package/ledger/generation bound to an `awaiting-reconciliation` journal whose outcomes are all `projected`. HTTP acceptance and projection settlement alone therefore never complete a run.
- **IMP-REC-03:** comparison is bounded by configured record and read-time limits. It compares entity counts and logical identities, detects duplicate/orphan entities and dangling references, verifies canonical payload fingerprints for accounts, currencies, categories, contractors, settings, rates, operations, lineage, opening balances, adjustments, splits, and canonical transfer sides/rate/fee, and independently compares common cutoff, counts, account/currency balances, period/currency totals, and operation identity digest. Cross-currency transfer sides are compared exactly and are not required to sum numerically to zero.
- **IMP-REC-04:** every result has a deterministic SHA-256 evidence digest. The SQL journal records it in append-only protected audit and atomically transitions every `projected` outcome to `reconciled` plus the run to `succeeded`, or records the actionable bounded error classification and moves the run to `failed`. No payload, secret, or connection detail is persisted in the error evidence.
- **IMP-REC-05:** reconciliation never activates a generation. `RegisterPortabilityImportReconciliation<TTargetProbe,TReadbackAdapter>` is opt-in and no production host invokes it.

### RED/GREEN evidence and capability status

- RED: the compile-valid suite produced five expected failures against the not-implemented coordinator; the sixth test passed because execution correctly remained `awaiting-reconciliation` after projected/HTTP acceptance.
- GREEN: the focused preflight/execution/reconciliation regression passed 33/33 in 9 seconds after the coordinator implementation. The eight reconciliation tests cover exact full-fidelity success, missing projection, per-period/per-currency total mismatch, per-account/per-currency balance mismatch, duplicate, orphan, dangling reference, and acceptance-without-reconciliation.
- Release builds for the API unit and IntegrationTests projects completed with zero errors. The disposable SQL evidence test passed 1/1 in 772 ms and proves append-only evidence plus atomic `projected -> reconciled` and `awaiting-reconciliation -> succeeded` state survive a new store instance.
- **Local contract status: PASS. Production capability: BLOCKED.** The repository has no concrete adapter that reads both Accounting and Rates through supported APIs, so cross-service production reconciliation and activation are not advertised. No live environment or shared financial data was accessed.

## Task 4.6 Family Pro Compatibility Seam

### Audited contract boundary

- The v1 `lineage.jsonl` contract already owns the validated Family Pro identity fields: logical lineage ID, target entity type/ID, `sourceSystem`, source entity ID, source run ID, optional source batch ID, optional mapping ID, and migration timestamp. Account/category/contractor `legacyReference` values preserve the validated source references. This task does not rename, reinterpret, or regenerate them.
- The existing external migration verification lives in `HomeBudget.Accounting.Api.IntegrationTests/Migration/FamilyProCliGatewayTests.cs` and `scripts/verification/verify-familypro-cli-gateway.ps1`. The executable CLI and sanitized fixture generator are built from an external migration repository; they are not present in this workspace.
- The Accounting import journal durably binds operation ID, export ID, package SHA-256, ledger/generation, record fingerprints, and idempotency keys. The compatibility seam composes that journal with the unchanged v1 lineage and carries opaque source-backup, approved-manifest, and external-journal references without copying source contents or credentials.

### Compatibility behavior

- **IMP-FP-01:** legacy resume uses the original Home Ledger operation ID and external journal identity, requires the original package/export binding, and must target the journal's exact ledger generation and generation number. A post-reset/new-generation resume returns `familypro.resume-generation-conflict` before import execution.
- **IMP-FP-02:** fresh remigration requires a new Home Ledger operation ID, new external journal identity, and new target generation. It preserves the prior operation/journal reference, source-backup reference, approved-manifest reference, source entity/run/batch/mapping identities, logical IDs, and legacy references from the validated package.
- **IMP-FP-03:** external references are bounded opaque identifiers only. Control characters, empty values, source contents, credentials, and secrets are not accepted or logged. The external migration journal remains responsible for durably retaining those references; Accounting does not invent a parallel Family Pro mapping store.
- **IMP-FP-04:** `RegisterFamilyProPortabilityCompatibility<TTargetProbe>` is opt-in. No API or worker host registers the seam, and it does not enable import, reconciliation, or activation capability by itself.

### RED/GREEN and blocked external evidence

- RED: four compile-valid compatibility tests failed 4/4 against the intentionally absent seam.
- GREEN: the compatibility plus ordinary reference-idempotency regression passed 23/23 in 2 seconds. The focused cases prove exact lineage/reference preservation, same-generation resume, rejection after generation reset, distinct remigration operation/journal identities, and preservation of the prior journal.
- Disposable distributed regressions passed 2/2 in 47 seconds: payment idempotency retry projected one operation, and exact transfer retry produced one deterministic atomic transfer. The IntegrationTests Release build completed with zero errors.
- **Local compatibility contract: PASS. OpenSpec task/production gate: BLOCKED and unmarked.** The external gate requires: (1) a `MigrationSourceRoot` containing `FireBirdV25Client/FireBirdV25Client.csproj` and `FireBirdV25Client.ReleaseFixture/FireBirdV25Client.ReleaseFixture.csproj`; (2) approved exact Accounting, Gateway, and Migration commit SHAs; (3) a positively identified disposable output root; and (4) the sanitized fixture generator/source revision that produces `approved-manifest.json`, `approval.json`, and `independent-expected.json`. None of the `FAMILYPRO_TEST_ACCOUNTING_DLL`, `FAMILYPRO_TEST_ACCOUNTING_CONTENT_ROOT`, `FAMILYPRO_TEST_GATEWAY_DLL`, `FAMILYPRO_TEST_GATEWAY_CONTENT_ROOT`, `FAMILYPRO_TEST_CLI_DLL`, `FAMILYPRO_TEST_FIXTURE_DLL`, or `FAMILYPRO_TEST_OUTPUT_ROOT` inputs are configured in this environment. No substitute fixture or PASS was fabricated.

## Progress and Resume State

- Decisions made: test the dedicated SQL persistence boundary because it is the lowest layer that can prove durability, atomic exclusion, immutability, and reset-scope separation before production types exist.
- Implemented: repository-local SDD; persistence/migration contracts; byte-identical root V21 and integration V9 migrations; SQL operation store; legal state machine; cooperative cancellation; monotonic fence context propagation; owner/token/expiry-conditioned transitions; lease-loss executor cancellation; failure-resilient worker polling; renewable scope leasing; bounded status polling; and disabled-by-default OperationsConsumer hosted worker registration.
- Independent-review remediation evidence: the focused RED run failed 4/4 cases (stale takeover changed `running` to `failed`; `IOException`, `HttpRequestException`, and provider exception each stopped polling). The GREEN control-plane run passed 11/11 cases with a real disposable SQL control database.
- Verification passed: the control/migration/worker regression passed 20/20 tests, including eight component tests, three worker failure-continuation cases, and the explicit V0–V8 state upgrade; migration files retain identical SHA-256 `7B810C5F2135570FFBD039ACC2A66DAAA9EF8561879F03038DAF8411F97844BE`.
- Current failure or blocker: none for task 1.3. Re-running the complete chain through a new Evolve instance is not a valid check because the pre-existing multi-database migration history reports V0 as out-of-order; direct V9 batch reapplication proves this migration's idempotence without altering that established harness behavior.
- Task 1.4 scope contract: goal is compile-valid RED Accounting tests only; allowed scope is the repository-local SDD and focused Accounting test files; production code, migrations, UI, other repositories, and root task state are prohibited. Risk is CRITICAL because the tests define financial idempotency and stale-delivery isolation. Key invariants are deterministic legacy adoption, distinct publication and ledger generations, exact legacy stream identity, no stale effects, and cross-generation isolation.
- Task 1.5 implementation state: Accounting generation propagation is implemented and focused compatibility/regression tests are green, including account-topic stale-delivery isolation and transfer status/precondition parity. The integration migration is V10 and its byte-identical root deployment twin is V22. No root OpenSpec task state is changed by this repository-local handoff.
- GEN-004/GEN-007 remediation: account-topic stale or ambiguous deliveries now write `AccountDeliveryDispositions` before Kafka commit. The durable key is message identity plus ledger/generation; its immutable payload SHA-256 makes identical redelivery effect-free and changed content conflict-safe. A failed disposition write leaves the offset uncommitted and never invokes the account mutation handler. Root V25 and integration V12 are byte-identical (SHA-256 `D79119F5A5442C34F0A6C09589CB843588ED221F2649FA12817F29EFA049A59E`). Focused consumer/generation tests passed 7/7, the full Operations component suite passed 123/123, and disposable SQL reconnect/idempotence/conflict tests passed 2/2. The isolated Infrastructure non-incremental build passed; the whole-solution gate remained blocked only by concurrent portability-export analyzer findings outside this remediation.
- Task 3.1 implementation state: Accounting owns the versioned v1 portability schemas, strict entry catalog and compatibility matrix, deterministic full-ledger sample, reconciliation contract, and focused validation tests. Attachments are explicitly `unsupported-not-present`; archive parsing and import/export execution remain later tasks.
- Task 3.2/3.3 implementation state: all 30 archive-security RED cases are GREEN against the bounded production inspector, with three additional positive/cleanup/CSV regressions. The inspector provides strict catalog/path/type handling, bounded streaming extraction and validation, SHA-256/manifest/schema verification, protected staging, and marker-bound cleanup/quarantine without importing business data.
- Task 3.4/3.5 state: the original 15 export contracts and three positive/determinism/CSV regressions are GREEN against the bounded exporter. The application-owned reader seam and opt-in DI registration exist, but no default host advertises export because the coherent authoritative cross-store reader and durable operation executor remain unimplemented.
- Task 4.1/4.2 state: validate-only/preflight and immutable SQL journal creation are GREEN. Accounting V13/root V26 are the byte-identical schema pair.
- Task 4.3 state: the opt-in execution coordinator, durable outcome transitions, bounded settlement, cancellation, retry/conflict, and crash/resume contracts are GREEN. Task 4.4 remains BLOCKED on a production Accounting/Rate application adapter and real cross-store proof; reconciliation/activation remains task 4.5. Production capability therefore stays fail-closed.
- Task 4.5 state: the bounded reconciliation coordinator and durable evidence transitions are GREEN locally; production Accounting+Rates API readback remains BLOCKED and no activation occurs.
- Task 4.6 state: the Accounting Family Pro compatibility seam is GREEN locally, including generation-bound resume and fresh remigration identity. The real CLI/sanitized fixture gate remains BLOCKED on the absent external migration repository and pinned revision inputs, so the OpenSpec task remains unmarked.
- Remaining work: public administration APIs, authorization, concrete backup/import/reset operation executors, cross-repository SPA/gateway propagation, and all later OpenSpec tasks.

## Tasks 5.1/5.2 Coordinated Capture Application Layer

### Scope contract

- **Goal:** implement the application coordination boundary that closes mutation admission, records component frontiers and accepted pending work, calculates one conservative common cutoff, and publishes a recovery manifest only after capture, independent copy, and verification all succeed.
- **Expected behavior:** a durable maintenance epoch is visible before component frontier acquisition; receipt sequences never regress; accepted-but-unpersisted work is drained or explicitly retained for recovery; failures leave the previously published protected cutoff unchanged.
- **Allowed scope:** `HomeBudget.Accounting.Infrastructure/DataProtection/Capture/**`, focused Accounting integration tests, additive Accounting/root SQL migration twins, this specification, and the workspace migration parity check.
- **Non-goals:** SQL Server/EventStoreDB/Mongo backup providers, artifact encryption/storage providers, ordinary payment/transfer admission integration, Rates implementation changes, import/export execution, reset, or enabling the production operation executor.
- **Risk:** CRITICAL (cross-store cutoff correctness and data-loss exposure).
- **Key invariants:** maintenance scope/generation is immutable; component receipt sequences are monotonic; every required frontier is present once; common cutoff is no later than any component frontier; pending accepted work is never silently omitted; only copied and integrity-verified manifests become discoverable as protected.
- **Verification:** deterministic coordinator tests use controllable barriers/fakes and no sleeps; disposable SQL tests prove epoch/receipt/manifest durability; migration parity and focused Accounting builds/tests provide repository evidence.

### Confirmed current boundaries

- `HomeBudget.DataProtection.dbo.ComponentReceipts` and `dbo.RecoveryManifests` exist from Accounting V9/root V21, but no production application store or state machine currently records them.
- Accounting SQL command acceptance can precede EventStore persistence; the SQL outbox is therefore recoverable pending authority at capture.
- Rates exposes `IRatesCaptureFrontierProvider.AcquireAsync`, whose durable receipt orders Rates mutations but is not a long-held maintenance lease or physical backup.
- The default `UnsupportedDataProtectionOperationExecutor` advertises no executable operation kinds. Tasks 5.1/5.2 do not change that fail-closed production capability state.
- Accounting V13/root V26 are reserved for the concurrent importer slice. This slice uses Accounting V14/root V27 and does not fill or reinterpret the reserved versions.

### Coordination decisions

1. A maintenance epoch is inserted atomically under a SQL application lock for the scope. Its active row is the durable admission-closed signal; releasing or failing the epoch is an explicit terminal transition.
2. Component frontier sources are application contracts. Accounting, EventStore, Mongo references, Rates, and projection adapters must return durable receipts; vendor capture providers remain later tasks.
3. Receipt monotonicity is enforced in the durable SQL store under a component/kind application lock, not inferred from wall-clock timestamps.
4. The pending-work source first attempts the configured bounded drain. Remaining accepted work may proceed only when its durable recovery source is declared capturable, in which case an `included` disposition and watermark are persisted.
5. The common cutoff is the earliest required component commit timestamp. Per-component sequence/watermark evidence remains in the manifest; unrelated component-local sequences are never compared as if globally ordered.
6. A staged manifest is not a protected point. Conditional publication requires explicit independent-copy and integrity-verification evidence. Capture/copy/verification failures mark only the staged set failed and cannot advance `GetProtectedCutoffAsync`.

### Implementation and verification evidence

- The application coordinator lives under `DataProtection/Capture`. It closes the durable epoch before inspecting pending work or acquiring any component frontier, acquires required component frontiers concurrently, persists each receipt, calculates the earliest component commit time, and releases the barrier only after success/failure is journaled.
- `SqlCaptureCoordinationStore` conditions epoch creation on the current operation lease/fence, uses transaction-owned SQL application locks for admission and receipt ordering, rejects a component/kind sequence regression, and exposes protected cutoffs only from verified, protected, published manifests.
- Accounting V14/root V27 add durable maintenance epochs, accepted-pending disposition, monotonic receipt lookup, failure classification, and immutable epoch binding. They are byte-identical deployment/test twins. Accounting V13/root V26 remain the independently owned import migration pair.
- Artifact capture/copy/verification and component frontier implementations are interfaces only. No default executor is registered, so production backup capability remains **BLOCKED** until tasks 5.3 through 5.6 supply validated providers and configuration.
- RED: `dotnet test HomeBudget.Accounting.Api.Tests/HomeBudget.Accounting.Api.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~CoordinatedCaptureServiceTests" --logger "console;verbosity=minimal"` failed 6/6 against the compile-valid coordinator seam with `Coordinated capture is not implemented.`
- GREEN: the same focused command passed 6/6. It proves admission closes before all frontiers, mutation attempts after the barrier are rejected by the shared test fence, the earliest receipt becomes the common cutoff, receipt regression aborts, accepted pending outbox work is explicitly included after a bounded drain fails, and capture/copy/verification failures leave the prior protected cutoff unchanged.
- Disposable SQL: `dotnet test HomeBudget.Accounting.Api.IntegrationTests/HomeBudget.Accounting.Api.IntegrationTests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~SqlCaptureCoordinationStoreTests|FullyQualifiedName~DataProtectionControlPlaneMigrationTests" --logger "console;verbosity=minimal"` passed 6/6. It proves fresh/upgrade/idempotent schema behavior, durable receipt regression rejection across operations, and publish-last protected-cutoff behavior against SQL Server Testcontainers.
- Build: `dotnet build HomeBudget.Accounting.Infrastructure/HomeBudget.Accounting.Infrastructure.csproj --configuration Release --no-restore --no-incremental` succeeded with zero errors; repository-baseline warnings remain.
- Migration parity: workspace `eng/check-migrations.ps1` passed through root V27, Accounting V14, and Rates V16.

### Task 5.2 production admission integration completion

The follow-up 5.2 handoff explicitly expanded the earlier coordination-only scope to the existing Accounting and Rates mutation boundaries. It did not enable a backup provider or destructive operation.

- `SqlCaptureCoordinationStore` now issues a session-owned shared admission permit from the durable `MaintenanceEpochs` state. Closing admission obtains the corresponding exclusive scope lock and the cross-service global lock before it records the epoch. The permit is held for the whole admitted mutation/processing unit, so an in-flight unit finishes before the close commits and a later unit observes the durable closed epoch on every replica.
- Accounting unsafe HTTP actions acquire the permit through a global action filter; GET/HEAD/OPTIONS and the data-protection control controller remain available. Ledger-generation-bound MediatR commands also acquire it, covering payment and canonical transfer handlers independently of their HTTP transport.
- The SQL outbox publisher acquires before locking retryable rows. Payment and account Kafka consumers acquire before inbox/disposition/business mutation, EventStore delivery acquires before append, and the EventStore projection subscription acquires before projection/checkpoint dispatch. A closed fence therefore leaves accepted-before-barrier work durable and retryable instead of marking it failed, committing its Kafka offset, acknowledging its subscription delivery, or silently dropping it.
- The specialized idempotent transfer path holds one permit across the atomic transfer registration, preventing a partial two-sided transfer registration. Existing-identical retries may still return their already-durable result because that branch performs no mutation.
- Rates rate/settings providers take the shared global maintenance lock and check durable closed epochs inside the same SQL transaction as the authoritative mutation and commit receipt. Frontier capture retains its Rates commit lock and remains callable while admission is closed. Missing control-plane schema or lock acquisition fails the mutation closed.
- Projection rebuild remains a separately journaled administrative operation whose conflict/fencing contract belongs to its owning rebuild/reset tasks; ordinary live EventStore projection delivery is fenced here. Read-only metrics/health queries do not mutate protected state and are intentionally not fenced.

Focused GREEN evidence on the final implementation:

- Accounting API Release build: zero errors (repository-baseline warnings remain).
- Operations command/outbox/Kafka/EventStore/direct-transfer/account regression filter: 37/37 passed, followed by 8/8 for the added account-consumer and idempotent-transfer closed-fence assertions.
- Disposable Accounting SQL/migration/EventStore-delivery filter: 10/10 passed, including exact-epoch release/resume and protected-cutoff non-advancement.
- Rates API Release build: zero errors; complete CurrencyRates component suite: 31/31 passed.
- Disposable Rates receipt/frontier suite: 10/10 passed, including atomic rejection of rate/settings writes during a closed epoch, no receipt or partial authoritative change, frontier availability, and resume after exact release.

## Task 5.3 SQL Server 2022 Protection Provider

### Scope and design

- **Goal:** provide a fail-closed SQL Server 2022 capture and PITR-planning boundary for a single allowlisted database and server-side backup root.
- **Behavior:** probe and require the exact server name, product build, edition, online database identity, and `FULL` recovery model; plan and execute native full, differential, and transaction-log backups with `CHECKSUM`; verify every media set with `RESTORE VERIFYONLY WITH CHECKSUM`; read `msdb.dbo.backupset` metadata; validate database/recovery-fork/differential-base/LSN continuity; and select the log whose `last_valid_restore_time` covers the requested UTC `STOPAT`.
- **Safety:** policy construction rejects non-allowlisted databases and roots, traversal, Windows/device paths, and invalid timeouts. Execution re-probes identity before mutation, uses parameters for server-side media targets, propagates cancellation, and converts provider errors to bounded messages containing only the SQL error number. It never logs or returns connection strings or credentials.
- **Boundary:** the provider creates backup media only. It does not restore, publish an independently protected artifact, manage retention, or advertise the production backup capability. Those remain tasks 5.6/5.7/6.3.

### Verification and capability status

- RED: the focused integration-test project initially failed compilation with `CS0234`/`CS0246` because the SQL protection contracts/provider did not exist.
- GREEN: `dotnet build HomeBudget.Accounting.Api.IntegrationTests/HomeBudget.Accounting.Api.IntegrationTests.csproj --configuration Release --no-restore --verbosity quiet` passed with zero errors (6.11 seconds on the final run; repository-baseline warnings remain).
- Disposable SQL: `dotnet test HomeBudget.Accounting.Api.IntegrationTests/HomeBudget.Accounting.Api.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~SqlServerProtectionProviderTests" --verbosity minimal` passed 4/4 (8-second test duration; about 53 seconds including build/container readiness). The fixture proved the SQL process belonged to the Testcontainers container and `/var/opt/mssql/data` was an `overlay` mount before creating a unique test database or backup.
- The tests prove verified full/differential/log metadata, a covered `STOPAT`, missing-log gap rejection, exact-build drift rejection without the password canary in the exception, and pre-connection cancellation. They also caught and drove fixes for the SQL Server 2022 `last_recovery_fork_guid` column name and `backup_set_id` integer mapping.
- **BLOCKED for production enablement:** deployment still uses a mutable SQL Server `2022-latest` image and has no approved exact server/build/edition allowlist, independent artifact destination, separately recoverable key, or restore execution target. Production must pin and configure the already-approved deployed build rather than infer it from this disposable test. No live host was accessed.

## Task 5.4 EventStoreDB 24.10.16 Protection Provider

### Scope contract

- **Goal:** provide a fail-closed EventStoreDB 24.10.16 backup and restore-planning adapter that proves one source-node identity, validates the required database/index/checkpoint inventory and writer/chaser relationship, excludes file-copy capture during scavenge, and refuses restore unless the target is stopped.
- **Expected behavior:** a trusted node probe and exact allowlisted paths produce a deterministic full or differential ordered-copy plan with SHA-256 inventory and dependency evidence; incompatible versions, path/node drift, mixed-node artifacts, incomplete inventory, active scavenge, corrupt dependencies, and a running restore target are rejected with secret-safe errors.
- **Allowed scope:** `HomeBudget.Accounting.Infrastructure/DataProtection/EventStoreDb/**`, focused `HomeBudget.Accounting.Api.Tests/DataProtection/EventStoreDb/**`, and this specification.
- **Non-goals:** executing file copies or snapshots, controlling scavenges, generic Docker-volume copying, registering/advertising production backup capability, changing coordinated-capture journals, SQL Server/Mongo providers, or accessing a live node.
- **Risk:** CRITICAL (a false-positive plan can produce an unrestorable financial event history).
- **Key invariants:** exact version `24.10.16`; one allowlisted node and exact resolved paths; `0 <= chaser <= writer`; all five database checkpoints plus chunks and a coherent index set; no reparse/symbolic-link traversal; no file-copy plan while scavenge is active; differential chains remain on one node and depend on a verified full baseline; restore target is positively identified and stopped; checksums and cancellation are mandatory.
- **Verification:** compile-valid RED provider tests followed by focused GREEN tests against marker-bound, process-owned disposable directories; affected project build/format/diff review; no live or shared storage.

### Discovery and decisions

- **CONFIRMED:** current deployment configuration pins `eventstore/eventstore:24.10.16-jammy`; the provider therefore rejects patch drift rather than upgrading or accepting a version range.
- **CONFIRMED:** the selected [EventStoreDB 24.10 backup procedure](https://docs.kurrent.io/server/v24.10/operations/backup) requires index checkpoints before other index files, database checkpoints before chunks, no node-file mixing, no file-copy capture during scavenge, and restore only to a stopped node. Restore replaces `truncate.chk` from the captured `chaser.chk`.
- **CONFIRMED:** the application control plane and artifact pipeline already remain fail-closed; this provider is not registered by either production host in task 5.4.
- **ASSUMED:** a separately privileged deployment adapter will supply the node/process/scavenge probe. Task 5.4 validates its identity-bound result and filesystem evidence but does not introduce arbitrary command or endpoint execution.
- **UNKNOWN/BLOCKED FOR PRODUCTION:** approved source/target node IDs and canonical database/index/restore paths are not configured, and there is no privileged probe deployment. Consequently this slice cannot advertise production backup or restore readiness.

The adapter is deliberately a planning boundary. It does not copy a mounted active Docker volume. A later executor must carry out the immutable ordered steps, re-probe the same node and scavenge state at the execution boundary, verify post-copy hashes, and feed the result through coordinated capture and independent artifact publication.

### Acceptance traceability

| ID | Observable acceptance criterion | Evidence | Status |
| --- | --- | --- | --- |
| ESB-01 | Exact 24.10.16, allowlisted node/path identity, connected/up-to-date source, and writer/chaser positions are required. | `EventStoreDbProtectionProviderTests` compatible and rejection cases | Verified locally |
| ESB-02 | Full/differential plans inventory required checkpoints, index files and chunks in vendor order with SHA-256, sizes and dependencies. | focused ordered-plan and checksum tests | Verified locally |
| ESB-03 | Active scavenge, incomplete inventory, symlink/reparse traversal, mixed nodes, and invalid differential baselines fail closed. | focused rejection tests plus production reparse-point validation | Verified locally; reparse branch compile-verified only |
| ESB-04 | Restore validates one verified capture, every checksum/dependency, an allowlisted empty target, and stopped-process proof; it plans `truncate.chk` from `chaser.chk`. | focused restore tests | Verified locally |
| ESB-05 | Cancellation interrupts probe/planning/hashing without publishing or mutating source/target data; error messages expose stable codes rather than paths or probe secrets. | focused cancellation and redaction tests | Verified locally |

### RED/GREEN evidence and operational boundary

- RED: `dotnet test HomeBudget.Accounting.Api.Tests/HomeBudget.Accounting.Api.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~EventStoreDbProtectionProviderTests" --logger "console;verbosity=minimal"` failed 15/15 against the compile-valid provider seam with the expected `NotImplementedException`.
- GREEN: the same focused command passed 15/15. The process-owned fixture creates isolated source/artifact/target directories below a dedicated temporary parent and writes a unique marker before use; recursive cleanup refuses any path outside that parent or without the marker.
- The provider plans but does not execute source or restore file operations. Full plans order index checkpoints, index map/tables/auxiliary data, database checkpoints, then chunks. Differential plans carry a verified parent dependency, copy changed files, and explicitly identify extraneous files for removal. Every inventory file records source node, length, SHA-256 and dependency paths.
- Source planning probes before and after inventory hashing, rejects writer/chaser or inventory drift, and refuses active scavenge. Restore re-hashes an exact allowlisted artifact inventory, rejects mixed nodes and missing dependencies, independently verifies an empty dedicated target, and requires stopped-process proof before producing a plan that overwrites `truncate.chk` from `chaser.chk`.
- Production capability remains **BLOCKED**: no production probe/control adapter, approved node/path allowlist, executor, independent artifact store, or restore drill is configured. Neither host registers this provider, and task 5.4 does not change capability reporting.

## Task 5.5 MongoDB 7.0 Continuous Backup and PITR Provider

### Scope contract

- **Goal:** implement a fail-closed MongoDB topology/capability boundary that distinguishes the current standalone service from a supported replica-set continuous-backup provider and validates snapshot, mutation-history, gap, and timestamp-recovery evidence.
- **Expected behavior:** standalone MongoDB reports PITR `blocked`; a supported adapter is ready only for an explicitly allowlisted MongoDB build and source deployment, immutable provider artifacts, a verified snapshot, and a continuous oplog/change interval with no hidden gaps. Requested and actual recovery cutoffs are UTC, actual is no later than requested, and both lie inside the advertised recoverable interval without crossing a recorded gap.
- **Allowed scope:** `HomeBudget.Accounting.Infrastructure/DataProtection/MongoDb/**`, focused Accounting unit/integration tests for that provider, and this specification.
- **Non-goals:** production provider credentials, host registration/capability advertisement, `mongodump` as PITR, capture coordination, Mongo schema/domain writes, migrations, restore execution, or connecting to any live/shared MongoDB deployment.
- **Risk:** CRITICAL because advertising unsupported historical recovery can lose authoritative account/reference/settings changes.
- **Key invariants:** exact configured server build and deployment identity; replica-set topology; continuous-backup mechanism rather than logical/live dump; immutable provider snapshot/artifact identities; ordered snapshot/oplog/recoverable intervals; explicit gap rejection; exact requested/actual UTC cutoffs; fail closed on probe/provider drift.
- **Verification:** compile-valid RED unit tests with a deterministic fake adapter, GREEN focused tests, and—where the disposable harness is available—a read-only topology probe against its process-owned MongoDB 7.0 standalone container proving the capability remains blocked.

### Confirmed boundaries and decisions

- **CONFIRMED:** deployment pins `mongo:7.0.43-jammy`, local Compose pins `mongo:7.0.27-rc0-jammy`, and the current Testcontainers harness pins `mongo:7.0.5-rc0-jammy`. Exact allowed builds are policy input; task 5.5 does not upgrade or normalize them.
- **CONFIRMED:** the [MongoDB 7.0 replica-set restore procedure](https://www.mongodb.com/docs/v7.0/tutorial/restore-replica-set-from-backup/) treats backups as snapshots and warns that later changes are absent unless a service supplies applicable point-in-time history. The [MongoDB 7.0 snapshot read contract](https://www.mongodb.com/docs/v7.0/reference/read-concern-snapshot/) is bounded by retained snapshot history and is not a 30-day backup/PITR substitute.
- **CONFIRMED:** current Home Ledger MongoDB services are standalone. They cannot advertise arbitrary PITR, regardless of a successful logical dump.
- **DECISION:** external provider operations remain behind `IMongoDbContinuousBackupAdapter`; Home Ledger validates identity and coverage evidence but does not embed provider credentials or vendor-specific runtime serialization.
- **BLOCKED FOR PRODUCTION:** no supported replica-set continuous-backup service/topology, approved source deployment identity, immutable provider destination, credentials/key configuration, or successful isolated restore drill exists in repository configuration.

### Acceptance traceability

| ID | Observable acceptance criterion | Evidence | Status |
| --- | --- | --- | --- |
| MDB-01 | Standalone or unsupported MongoDB build/topology reports PITR blocked without calling snapshot/recovery operations. | focused capability tests and disposable topology probe | Verified locally |
| MDB-02 | Supported capability exposes exact deployment/provider identity, immutable-artifact support, recoverable interval and explicit gaps; logical/live dump claims fail closed. | deterministic adapter tests | Verified locally |
| MDB-03 | Snapshot evidence binds immutable snapshot/artifact IDs, snapshot time, mutation interval, actual recoverable interval, source deployment and verification. | snapshot validation tests | Verified locally |
| MDB-04 | PITR reports requested and actual UTC cutoffs plus snapshot/oplog watermarks; outside-interval, gap, drift, or actual-after-request evidence is rejected. | focused recovery-plan tests | Verified locally |
| MDB-05 | Cancellation propagates and errors expose bounded stable codes without provider credentials or arbitrary response bodies. | focused cancellation/redaction tests | Verified locally |

### RED/GREEN evidence and operational boundary

- RED: `dotnet test HomeBudget.Accounting.Api.Tests/HomeBudget.Accounting.Api.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~MongoDbProtectionProviderTests" --logger "console;verbosity=minimal"` failed 18/18 against the compile-valid capability seam with the expected `NotImplementedException`.
- GREEN: the same focused command passed 18/18. Deterministic topology/provider fakes prove exact server/deployment/provider identity, standalone and unsupported topology blocking, rejection of logical dumps and mutable artifacts, interval/gap validation, snapshot and recovery evidence drift rejection, UTC cutoff semantics, cancellation, and secret-safe provider failures.
- Disposable topology: `dotnet test HomeBudget.Accounting.Api.IntegrationTests/HomeBudget.Accounting.Api.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~MongoDbTopologyProbeTests" --logger "console;verbosity=minimal"` passed 1/1 against the process-owned `mongo:7.0.5-rc0-jammy` Testcontainers service. The read-only driver probe observed a 7.0 standalone topology, the capability returned `standalone-topology`, and the continuous-backup adapter was not called.
- Build: `dotnet build HomeBudget.Accounting.Api.IntegrationTests/HomeBudget.Accounting.Api.IntegrationTests.csproj --configuration Release --no-restore --no-incremental` succeeded with zero errors; repository-baseline warnings remain.
- The driver probe uses `buildInfo` and `hello`; for a replica set it also reads `replSetGetConfig.settings.replicaSetId` and hashes that durable ID with the replica-set name. It never returns member addresses or connection details. Sharded and standalone deployments remain blocked before an external backup adapter can run.
- Production capability remains **BLOCKED**: the current Compose topology is standalone and there is no approved continuous-backup adapter, exact production allowlist, immutable provider destination, separately recoverable credentials/keys, or isolated restore drill. The provider and driver probe are intentionally not registered or advertised by the production host.

## Task 5.6 Protected Artifact Pipeline

### Scope contract

- **Goal:** turn component captures into authenticated-encrypted, content-addressed artifacts and publish a recovery set only after an independently identified immutable/offline copy is re-read and verified.
- **Allowed scope:** `HomeBudget.Accounting.Infrastructure/DataProtection/ArtifactProtection/**`, focused Accounting tests, opt-in artifact DI, and this specification. The existing coordinated-capture interface is implemented rather than replaced.
- **Non-goals:** production cloud credentials, a local-filesystem production fallback, retention, restore/decryption orchestration, vendor database capture, maintenance admission, or production capability advertisement.
- **Risk:** CRITICAL because false independence, leaked key material/plaintext, or early manifest visibility can turn a successful job into an unrecoverable set.
- **Safety:** the filesystem adapter accepts only a process-owned disposable root whose canonical path, expected parent, root ID, ownership token, marker, and no-reparse-point checks all match. Production profile always rejects that adapter.

### Requirements and decisions

| ID | Observable requirement |
| --- | --- |
| ART-01 | Component plaintext is streamed through an authenticated-encryption provider into protected staging; only provider/version/key references and encryption metadata are persisted. |
| ART-02 | Encrypted staging and independent objects are addressed by SHA-256 content, record plaintext/ciphertext sizes and checksums, and reject traversal, links, mutation, or checksum drift. |
| ART-03 | Provider capability must prove streaming authenticated encryption, no embedded/exported key material, and separate key recoverability for the exact requested key reference. |
| ART-04 | Independent-store capability must prove protected transport (or a local-process-only disposable test boundary), a different source host and disk failure domain, and immutable or offline state. |
| ART-05 | The independent manifest is conditionally and atomically published last, only after every copied artifact is re-read and verified. A different manifest under the same recovery-set identity conflicts. |
| ART-06 | Capture/copy/verification/cancellation failures publish no manifest, do not advance the coordinator's protected cutoff, quarantine partial protected objects, and securely clean the plaintext workspace. |
| ART-07 | The filesystem implementation is test-only and fails closed unless positive disposable-root evidence matches; it is rejected unconditionally under a production profile. |

The encryption provider owns key retrieval and cryptographic execution; the pipeline never receives key bytes. This is the enforceable boundary for an external KMS/HSM/backup-provider adapter. The disposable fixture supplies a chunked AEAD implementation only for local contract verification. Production remains **BLOCKED** until a remote independent immutable/offline store and separately recoverable key provider are configured and an isolated restore/key-recovery drill succeeds.

### Implementation and verification evidence

- `RecoveryArtifactPipeline` implements the existing coordinated-capture artifact seam. It validates an exact separately recoverable key reference, streams each source file through the authenticated-encryption provider, records plaintext/protected SHA-256 and sizes, and emits deterministic inventory without receiving or persisting key bytes.
- `DisposableFileSystemArtifactStore` uses write-once SHA-256 object paths, re-hashes collisions, re-reads independent objects before verification, and conditionally moves the final protected manifest into its discoverable location only after every object verifies. The manifest records key/provider/store versions, source scope/cutoff, checksums, transport state, and immutable/offline state.
- Plaintext workspaces are bounded to the positively identified root and are overwritten, flushed, and removed in `finally` on success, capture failure, and cancellation. Unpublished protected objects receive a bounded quarantine marker on capture/copy/verification failure. This is best-effort file erasure; storage-level guarantees still require encrypted production staging and an approved provider.
- RED: `dotnet test HomeBudget.Accounting.Api.Tests/HomeBudget.Accounting.Api.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~RecoveryArtifactPipelineTests" --logger "console;verbosity=minimal"` failed 8/8 against compile-valid `NotImplementedException` seams.
- GREEN: the final focused command passed 9/9. It proves manifest-last publication, bounded streaming AEAD, content-addressed protected files, no plaintext/key material in published content, root-marker and production-profile rejection, separately recoverable-key enforcement, host/failure-domain independence, capture/copy/verification failure quarantine, secret-safe errors, cancellation, and plaintext cleanup.
- Build: `dotnet build HomeBudget.Accounting.Infrastructure/HomeBudget.Accounting.Infrastructure.csproj --configuration Release --no-restore --no-incremental` succeeded with zero errors and no warnings attributed to the artifact-protection files; repository-baseline warnings remain elsewhere.
- Protected-cutoff non-advancement remains enforced by the already-recorded `CoordinatedCaptureServiceTests.CaptureAsync_FailureNeverAdvancesProtectedCutoff` capture/copy/verification matrix. The concrete pipeline cannot return successful verification until independent object re-read and manifest-last publication complete.
- DI is explicit opt-in through `RegisterRecoveryArtifactPipeline`; the ordinary control-plane registration does not call it. No filesystem adapter, key provider, independent store, or capability is registered or advertised by a production host.
- Production capability remains **BLOCKED**: no remote independent provider, immutable/offline destination identity, TLS configuration, separately recoverable key-provider adapter/version, credentials, or source-loss key-recovery drill was supplied. The filesystem adapter is internal, requires a matching disposable marker/root/ownership token and local test profile, and rejects `Production` unconditionally.

## Task 5.7 Retention and Recovery Triggers

### Scope contract

- **Goal:** plan chain-safe retention and durably coordinate scheduled, on-demand, named-bulk, threshold, mandatory pre-operation, and post-reconciliation backup triggers.
- **Expected behavior:** selected recovery points and the baselines/log/change segments reachable from them are never offered for pruning; continuous history intersecting the 30-day PITR interval and its dependency closure is retained; trigger counters and coalescing deadlines survive restart; forced triggers always enqueue with their reason; destructive work starts only after an existing recovery manifest proves verified independent publication.
- **Allowed scope:** Accounting `DataProtection/Retention/**`, `DataProtection/Triggers/**`, opt-in DI/options, focused tests, Accounting V15/root V28 migration twins, parity validation, and this SDD.
- **Non-goals:** artifact encryption/storage internals, vendor provider execution, portability/reconciliation implementation, ordinary admission, UI/API workflows, physical pruning, production scheduling enablement, or any live/destructive operation.
- **Risk:** CRITICAL because broken dependency reachability or a false-positive prebackup can make retained history unrestorable or permit destructive work without a recovery point.
- **Key invariants:** pruning is allowlist-only from a validated acyclic graph; missing dependencies fail closed; durable trigger state and backup operation creation commit atomically under a cross-replica scope lock; named bulk never coalesces; pending counters clear only with a durable queued operation; preoperation success requires verified, published, independent artifact evidence and never updates the protected cutoff itself.
- **Verification:** compile-failing RED tests first; deterministic policy/coordinator GREEN tests without sleeps; disposable SQL migration/restart/atomic-enqueue tests; migration parity; focused build/format/diff review.

Accounting V15/root V28 are reserved for the trigger-state migration. Registration remains opt-in, so unsupported provider/storage/key configuration cannot create a fake scheduled-backup success.

### Implementation and verification evidence

- `RecoveryRetentionPolicy` validates an acyclic dependency graph before returning any prune candidates. It retains all protected points for 48 hours, one daily point through 30 days, one monthly point through 12 months, every transaction-log/change segment overlapping the 30-day PITR window, and the complete dependency closure of every retained seed. Missing dependencies and invalid/incomplete UTC coverage intervals fail closed with the affected node identity.
- `RecoveryTriggerCoordinator` applies the 30-minute cadence, configurable record/byte thresholds, and bounded coalescing. On-demand, named-bulk, pre-operation, and post-reconciliation signals always create a new operation; trigger reason, source identity, pending counts, key reference, and required component set are durably recorded. Inputs are bounded to their database contract before persistence.
- `SqlRecoveryTriggerStateStore` uses a transaction-owned application lock plus row-version comparison. Updating the durable coalescing state, enqueueing the backup operation, recording its dispatch, and appending its protected audit entry are one SQL transaction. Pending counts are cleared only in that commit, so crash/restart and cross-replica contention cannot silently drop them.
- Every queued backup still enters `CoordinatedCaptureService`, whose frontier receipts and pending-work disposition either include durable accepted work or require settlement before publication. The trigger layer never updates the protected cutoff. Existing manifest publication remains last and requires integrity verification plus an independent immutable copy.
- `MandatoryPreOperationBackupGate` does not invoke destructive work until evidence for the exact queued operation identifies a non-empty recovery-set ID and confirms success, integrity verification, and independent publication. Capture/copy/verification failure therefore blocks the callback.
- The scheduler and trigger registrations are explicit opt-ins. Restart durability is in SQL, not a process-local timer. Production hosts must supply an approved target source and verified-publication completion adapter before enabling the hosted scheduler or preoperation gate.

RED was the expected missing-type compile failure for the retention and trigger tests. GREEN evidence on the final implementation:

- deterministic retention/coordinator tests: 13/13 passed (defaults, dependency closure, PITR history, expired unreachable pruning, missing-dependency rejection, restart coalescing, record/byte thresholds, named-bulk behavior, cadence, post-reconciliation reason, and mandatory-backup failure/success);
- disposable SQL/migration tests: 6/6 passed, including V15 schema contracts, persisted coalescing across a new coordinator/store instance, atomic queued operation creation, and distinct repeated named-bulk operations;
- `HomeBudget.Accounting.Infrastructure` Release build: zero errors after final boundary validation; repository-baseline warnings remain;
- migration parity: `eng/check-migrations.ps1` passed with Accounting V15 and root V28 byte-identical and both sequences complete.

The shared `HomeBudget.Accounting.Api.Tests` rebuild was independently blocked at final verification by concurrent telemetry-test compile errors in `DataProtectionTelemetryTests.cs`; the already-built focused 5.7 assembly passed 13/13, and both the final production project build and disposable integration-test project build passed with zero errors.

Production scheduling remains **BLOCKED and unadvertised**: no host enables the opt-in scheduler, no production `IRecoveryTriggerTargetSource` enumerates approved ledger generations, no `IVerifiedRecoverySetCompletionSource` is bound to the durable manifest/operation journal, and the broader provider/storage/key prerequisites recorded in tasks 5.3-5.6 are not configured. Physical pruning is intentionally absent; an eventual executor must consume only the policy's allowlisted `PrunableNodeIds` and revalidate the graph immediately before deletion. No live infrastructure or destructive provider operation was used.

## Tasks 6.1-6.2 Restore and PITR Planning

### Scope and design

- **Goal:** produce an immutable, execution-free saved-point or timestamp restore plan from verified application-wide evidence while failing closed on incomplete coverage.
- **Boundary:** `RecoveryPlanningCoordinator` obtains normalized evidence through `IRecoveryPlanningEvidenceSource`; `RecoveryPlanSelector` selects the baseline and continuous chains. The seam is opt-in and has no production registration. It does not decrypt/copy artifacts, restore a database, replay work, rebuild projections, or activate a generation; those remain tasks 6.3/6.4.
- **Provider relationship:** a production evidence source must call and preserve the stricter SQL Server, EventStoreDB, MongoDB, coordinated-capture, and protected-artifact contracts. The normalized selector is not permission to replace vendor chain/checksum/version validation with booleans from an untrusted caller.
- **Consistency:** all five required components—SQL processing/business state, EventStore payment events, Mongo authoritative references/settings/lineage, Rates history, and accepted pending work—must resolve one UTC actual cutoff. The selected state is ordered only by durable commit/acceptance timestamp and component-local sequence. Business dates are retained as evidence but never participate in membership.
- **Safety:** plans require an isolated, empty, inactive new generation plus notification and external-side-effect suppression. The result always records `ActivationAllowed=false`, returns read-only selected collections, and has a deterministic SHA-256 fingerprint bound to baseline, request, target generation, cutoff, and selected artifacts.

### Traceability and verification

| Requirement | Observable behavior | Evidence | Status |
| --- | --- | --- | --- |
| PITR-01/02 | Saved and between-point requests report requested/actual UTC cutoffs, precision, component watermarks, and durable-order membership; a later-committed backdated item is excluded. | `RecoveryPlanSelectorTests` | PASS locally |
| PITR-03/04 | Baseline and continuous evidence must cover payment events, accounts/categories/contractors, reference and settings update/delete, lineage, rates update/delete, transfer/processing state, and accepted pending commands. | positive membership and missing-coverage cases | PASS locally |
| PITR-GAP | Missing/duplicate components, broken timestamp/sequence continuity, unavailable intervals, checksum failure, version mismatch, or unequal component cutoffs fail closed. | focused rejection cases | PASS locally |
| REC-01/02 planning prerequisite | Only an isolated empty inactive generation with replay side effects suppressed can be planned; the plan cannot activate or expose mixed state. | target/suppression/no-activation cases | PASS locally |

Tests were authored first against a compile-valid selector stub that threw `NotImplementedException`. The intended runtime RED was temporarily blocked because concurrent trigger/retention and artifact test sources made the shared test project uncompilable; no expectation was weakened. After those paths stabilized, the first executable focused run was 17/19 with two reversed test-case labels, and the corrected final focused command passed 21/21 in 201 ms:

`dotnet test HomeBudget.Accounting.Api.Tests/HomeBudget.Accounting.Api.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~RecoveryPlan" --logger "console;verbosity=minimal"`

`dotnet build HomeBudget.Accounting.Infrastructure/HomeBudget.Accounting.Infrastructure.csproj --configuration Release --no-restore --no-incremental` passed with zero errors; repository-baseline warnings remain.

**BLOCKED for production recovery:** no production `IRecoveryPlanningEvidenceSource` currently assembles and revalidates independently published manifests, protected-artifact inventories, SQL chains, EventStore backup sets, Mongo continuous history, Rates receipts/history, and pending-work evidence. The current MongoDB deployment is standalone, and no isolated restore executor, target inventory, replay/rebuild coordinator, reconciliation/activation step, or successful restore drill exists. Therefore task 6.2's planning contract is locally PASS, while production PITR/restore capability remains BLOCKED and unadvertised. No live environment was accessed.

## Task 6.3 Isolated Restore Orchestration

### Scope and design

- **Goal:** execute an immutable recovery plan into an exact isolated target through bounded provider adapters while leaving the active ledger generation unchanged.
- **Boundary:** `RecoveryExecutionCoordinator` orchestrates seven ordered stages: SQL restore, EventStoreDB restore, Mongo restore, Rates restore, Kafka/topic/group reconstruction, and processing-state reconstruction, preceded by target preparation. It stops at `AwaitingReplayRebuild`; replay, projection rebuild, reconciliation, activation, and rollback execution remain task 6.4.
- **Durability:** `IRecoveryExecutionJournalStore` persists the request binding, completed-stage prefix, stage evidence, failure, and rollback eligibility. Resume requires the same recovery set, plan fingerprint, manifest SHA-256, application/schema versions, ledger, and target generation. Completed stages are not repeated.
- **Isolation:** the target must exactly match the immutable plan and provide positive evidence that it is isolated, empty, inactive, and not the current generation. EventStoreDB must be stopped for restore. Kafka topics, consumer groups, and EventStore subscriptions must be dedicated to the target ledger generation.
- **Integrity:** each provider completion is revalidated against the stage, recovery set, plan fingerprint, manifest checksum, application/schema versions, target identity, generation, and provider checksum before a checkpoint is committed.
- **No mixed activation:** an active-generation guard is checked before execution, before and after every provider stage, and before the terminal transition. Notifications and external effects must remain suppressed, and the result always records `ActivationAllowed=false`.

### Traceability and verification

| Requirement | Observable behavior | Evidence | Status |
| --- | --- | --- | --- |
| REC-EXEC-01 | The seven restore/reconstruction stages execute in deterministic order and stop at awaiting replay/rebuild. | `RecoveryExecutionCoordinatorTests.ExecuteAsync_CompletesStagesInOrderAndStopsBeforeActivation` | PASS locally |
| REC-EXEC-02 | Failure at any stage leaves the active generation unchanged, records rollback eligibility, and resumes without repeating a completed stage. | seven-stage failure/resume matrix | PASS locally |
| REC-EXEC-03 | Exact isolated target identity, inactive generation, stopped EventStoreDB proof, and generation-specific topic/group/subscription identities are mandatory. | target, active-generation, stopped-target, and messaging-identity rejection tests | PASS locally |
| REC-EXEC-04 | Recovery set, plan fingerprint, manifest SHA-256, application/schema versions, and provider completion evidence remain bound across start and resume. | artifact-binding, changed-manifest, and completion-evidence mismatch tests | PASS locally |
| REC-EXEC-05 | Concurrent active-generation change fails closed before mixed state can be exposed; no coordinator path activates the target. | active-generation mutation test and terminal result assertions | PASS locally |

Tests were authored first against a compile-valid coordinator seam that threw `NotImplementedException`. The intended executable RED run was initially blocked by an unrelated concurrent telemetry compile error; expectations were not weakened. After that shared blocker was corrected, the final focused command passed 41/41 recovery planning and execution tests in 285 ms:

`dotnet test HomeBudget.Accounting.Api.Tests/HomeBudget.Accounting.Api.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~RecoveryExecution|FullyQualifiedName~RecoveryPlan" --logger "console;verbosity=minimal"`

`dotnet build HomeBudget.Accounting.Infrastructure/HomeBudget.Accounting.Infrastructure.csproj --configuration Release --no-restore --no-incremental --verbosity quiet` passed with zero errors; repository-baseline warnings remain. DI is explicit opt-in through `RegisterRecoveryExecution` and per-stage `AddRecoveryRestoreStageAdapter`; production hosts do not register these seams.

**BLOCKED for production restore:** there is no production durable execution-journal store, active-generation guard binding, exact isolated-target inventory, protected-artifact decrypt/copy adapter, or concrete SQL Server, EventStoreDB, MongoDB, Rates, Kafka, and processing-state restore adapter. The current MongoDB topology still lacks supported continuous recovery, and no isolated restore/replay/rebuild/reconciliation drill has succeeded. This task verifies the orchestration contract only; it does not advertise a recoverable environment or authorize activation. No live environment was accessed.

## Task 6.4 Replay, Rebuild, Reconciliation, and Activation

### Scope and design

- **Goal:** continue an isolated restore journal through accepted-pending replay, derived projection/checkpoint rebuild, API-level reconciliation, and one atomic generation activation without ever exposing a partially validated target.
- **Boundary:** `RecoveryCompletionCoordinator` accepts only a task 6.3 journal with the complete ordered restore prefix. The application-owned adapters remain explicit fail-closed seams: `IRecoveryPendingReplayAdapter`, `IRecoveryProjectionRebuildAdapter`, `IRecoveryApiReconciliationAdapter`, and `IRecoveryGenerationActivator`. Registration is opt-in through `RegisterRecoveryCompletion`; no production host advertises this capability.
- **Replay:** evidence must bind the operation/recovery-set/plan/manifest/target, include all commands accepted at or before the actual recovery cutoff, prove every accepted command settled, preserve idempotency identities, and report zero duplicate financial effects. Notification and external-effect suppression must be present in both the immutable plan and adapter result.
- **Rebuild and reconcile:** rebuild evidence must prove derived state and checkpoints reached the actual cutoff without changing authoritative state. API reconciliation must pass identities/counts, duplicates/orphans, references, per-account/per-period/per-currency totals, balances, opening balances/adjustments, splits, canonical transfer sides/rates/fees, settings, and required rates.
- **Activation:** after all three completion checkpoints, the journal is durably moved to `ReadyToActivate`. Only then may `IRecoveryGenerationActivator` compare-and-swap the expected old generation to the validated target. A retry accepts `AlreadyActivatedByOperation` only when evidence binds the same operation and plan; conflicts fail closed.
- **Crash visibility:** before compare-and-swap the old generation remains visible. If the process loses the activated-journal checkpoint after a successful compare-and-swap, the validated new generation is visible and resume repairs the checkpoint idempotently. The journal preserves rollback eligibility and the previous generation identity.

### Traceability and verification

| Requirement | Observable behavior | Evidence | Status |
| --- | --- | --- | --- |
| REC-COMP-01 | Accepted pending work is replayed through an idempotent seam at the durable acceptance cutoff and a timeout/checkpoint retry has zero duplicate financial effect. | replay evidence validation and replay-checkpoint crash/resume test | PASS locally |
| REC-COMP-02 | Projection histories and checkpoints rebuild as derived state while authoritative state remains preserved; notifications/external effects stay suppressed. | rebuild and suppression positive/negative tests | PASS locally |
| REC-COMP-03 | Supported-API reconciliation covers financial and reference invariants before activation; missing/mismatched evidence never activates. | reconciliation success, failure, and binding-mismatch tests | PASS locally |
| REC-COMP-04 | Compare-and-swap occurs only after a durable ready checkpoint and accepts only exact operation/plan/generation evidence. | ordered happy path, ready-checkpoint failure, CAS failure, and active-generation conflict tests | PASS locally |
| REC-COMP-05 | Failure/cancellation at every completion stage remains rollback eligible and resumable; a crash after CAS exposes only the validated new generation and is journal-repairable. | three-stage failure matrix, cancellation, pre/post-CAS crash tests, and terminal idempotency test | PASS locally |

RED evidence: the compile-valid coordinator seam threw `NotImplementedException`, and the focused command failed 12/12 without weakening assertions. GREEN evidence after implementation:

- focused completion tests: 18/18 passed in 173 ms;
- combined recovery planning/execution/completion regression: 59/59 passed in 269 ms;
- `HomeBudget.Accounting.Infrastructure` Release no-incremental build: zero errors; repository-baseline warnings remain;
- scoped production and test `dotnet format --verify-no-changes`: exit 0; workspace-load warnings remain from the repository baseline;
- targeted diff whitespace validation: exit 0.

**BLOCKED for production completion/activation:** there is no production durable `IRecoveryCompletionJournalStore`, replay adapter bound to the accepted-command/outbox contract, projection-rebuild adapter, Accounting-plus-Rates supported-API reconciliation adapter, or transactional generation compare-and-swap implementation. No production generation router proves old/new visibility, and no crash-injected isolated restore/replay/rebuild/reconciliation/activation/rollback drill has run. The deterministic adapters prove orchestration behavior only; they do not establish recoverability or RTO. No live environment was accessed.

## Tasks 7.1-7.2 Managed Projection Rebuild

### Scope and design

- **Goal:** run the existing guarded payment-history rebuild as a durable data-protection operation that changes only derived histories, balances, publication heads, checkpoints, and projection audit records.
- **Source preservation:** EventStore payment events, Mongo category/reference documents and account metadata, SQL outbox/inbox/transfer state, and the protected control audit are read-only to the rebuild. Rebuild balance publication is explicitly marked as externally suppressed: it may update the derived balance and fence but cannot publish a notification or advance ordinary outbox command lifecycle state.
- **Immutable plan:** dry-run planning binds the exact environment, ledger, generation, Mongo/EventStore target identities, side-effect-suppression policy, and sorted account-period-stream inventory into a lowercase SHA-256 inventory fingerprint. The administrative plan fingerprint additionally binds that inventory hash. Changed plan bytes under the same operation identity fail before EventStore read or projection mutation.
- **Fencing and durability:** the existing SQL data-protection operation/lease journal remains outside ledger reset scope. The worker persists bounded rebuild/reconciliation stages under the active lease, polls durable cancellation between scopes, and rechecks the full active-generation identity before every scope. A restart may replay an already completed scope; the existing staged-generation/publication-head protocol makes that replay idempotent and exact.
- **Execution isolation:** managed mode does not start the ordinary Kafka or EventStore projection consumers. Reconciliation reloads the bound plan and requires every planned projection head to expose the exact account, stream, period, and authoritative revision. No new database migration was required.

### Traceability and verification

| Requirement | Observable behavior | Evidence | Status |
| --- | --- | --- | --- |
| PRJ-01 | Rebuild repairs exact derived identities/values while EventStore, Mongo references/account metadata, SQL business state, and protected audit fingerprints remain unchanged. | `ProjectionRebuildEventStoreIntegrityTests`, `ProjectionWorkerProcessConcurrencyTests` | PASS locally |
| PRJ-02 | Balance rebuild suppresses notifications and does not mark ordinary accepted commands projected. | `SyncOperationsHistoryCommandHandlerTests`, `UpdatePaymentAccountBalanceCommandHandlerTests` | PASS locally |
| PRJ-03 | Operation and plan bind the immutable scope inventory and exact generation; inventory drift or stale generation fails before source read/mutation. | `ProjectionRebuildPlanBindingTests`, `ProjectionRebuildDataProtectionExecutorTests` | PASS locally |
| PRJ-04 | Durable stages, lease fencing, cancellation polling, restart-safe replay, and exact-head reconciliation use the existing operation processor and projection publication protocol. | executor/runner focused tests plus existing data-protection worker and process-concurrency suites | PASS locally |
| AT-12 | Derived histories, balances, heads, and checkpoints reproduce authoritative events without changing authoritative reference/business data. | disposable Mongo/EventStore/SQL source-integrity test and multi-process crash/concurrency test | PASS locally |

RED evidence was established in two layers: side-effect tests failed to compile until suppression was explicit on both rebuild and balance commands, and managed-executor tests failed until immutable inventory binding, execution control, exact-generation fencing, cancellation, and reconciliation contracts existed. GREEN evidence on the final tree:

- `dotnet test HomeBudget.Components.Operations.Tests/HomeBudget.Components.Operations.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~SyncOperationsHistoryCommandHandlerTests|FullyQualifiedName~UpdatePaymentAccountBalanceCommandHandlerTests"`: 5/5 passed;
- `dotnet test HomeBudget.Accounting.Api.IntegrationTests/HomeBudget.Accounting.Api.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~ProjectionRebuildEventStoreIntegrityTests|FullyQualifiedName~ProjectionRebuildDataProtectionExecutorTests|FullyQualifiedName~ProjectionRebuildRunnerTests"`: 11/11 passed against disposable Testcontainers for the source-integrity case;
- `dotnet test HomeBudget.Accounting.Api.IntegrationTests/HomeBudget.Accounting.Api.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~ProjectionWorkerProcessConcurrencyTests"`: 2/2 passed, including crash-after-publication and concurrent independent worker processes;
- `dotnet test HomeBudget.Accounting.Api.IntegrationTests/HomeBudget.Accounting.Api.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~DataProtectionOperationWorkerTests|FullyQualifiedName~DataProtectionOperationComponentTests"`: 11/11 passed for durable status, lease fencing, cancellation, failure, and restart recovery;
- `dotnet test HomeBudget.Accounting.Api.Tests/HomeBudget.Accounting.Api.Tests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~ProjectionRebuildPlanBindingTests"`: 4/4 passed;
- IntegrationTests and worker Release no-restore/no-incremental builds completed with zero errors; repository-baseline analyzer warnings remain.

**BLOCKED for production enablement:** the Accounting API correctly remains fail-closed because executor availability is currently local to the separately deployed worker and there is no durable worker-capability heartbeat/discovery record. Consequently the API does not yet advertise or authorize creation of a projection-rebuild plan. Before enablement, operations must provide an immutable durable `ProjectionRebuild__ScopeFile` outside ledger-reset scope, configure the worker-only `ProjectionRebuild__ManagedExecutionEnabled` mode with ordinary projection consumers stopped, add cross-process executor discovery, and complete an authorized isolated drill. Losing the external inventory file prevents resume even though operation state and its fingerprint remain durable. No production host, credential, or live resource was accessed.

## Tasks 7.3-7.5 Ledger and Dedicated-Environment Reset Safety

### Scope and design

- **Goal:** define the executable, crash-safe boundary for clearing one ledger into an inactive new generation, and reject any environment-wide reset target that is not positively identified, dedicated, owned, recently authorized, and protected by a fresh recovery set.
- **Plan binding:** `LedgerResetPlanBinding` hashes the operation, environment, ledger, old/new generations, host and storage resource identities, affected counts, resource inventory, and preservation policy. Any changed count, target, generation, or resource invalidates confirmation.
- **Mandatory prebackup:** `ILedgerResetPreBackupGate` must return a succeeded, integrity-verified, independently protected recovery set whose common cutoff is at or after plan creation. Failure blocks every destructive stage.
- **Ordered isolation:** `LedgerResetCoordinator` durably journals pending-work disposition, Kafka/message isolation, EventStore isolation, Mongo isolation, SQL isolation, cache/job cleanup, inactive-generation preparation, and pristine verification. Every adapter must prove exact operation/plan/generation binding, selected-ledger scope, isolation of old work, and resource readiness.
- **Activation and visibility:** the current generation remains active during preparation. Compare-and-swap activation occurs only after all eight journaled stages and zero remaining business entities. A lost activation response is reconciled only when the generation was activated by the same operation; an unrelated generation change fails closed.
- **Resume and rollback:** the journal is outside selected-ledger reset scope, uses expected revisions, skips completed stages, and preserves the previous generation for rollback. Reimport is allowed only in the terminal succeeded state.
- **Environment reset:** `EnvironmentResetGuard` additionally binds the environment marker, recent authentication, recovery-set evidence, exact host/storage identities, resolved resource storage identities, ownership proofs, and preservation of administrator access, deployment configuration, and protected audit. `localhost`, Docker aliases, shared resources, and unowned resources are insufficient evidence.
- **Runtime boundary:** these are explicit adapter contracts only. No host registers a production reset executor, and no implementation drops shared topics, databases, or volumes.

### Traceability and verification

| Requirement | Observable behavior | Evidence | Status |
| --- | --- | --- | --- |
| RST-01 | Exact dry-run inventory, environment/ledger/generation/storage identity, and preservation policy are fingerprint-bound; stale confirmation fails. | `LedgerResetCoordinatorTests.ExecuteAsync_WithChangedPlanFingerprint_RejectsBeforeMutation`, environment fingerprint test | PASS locally |
| RST-02 | A fresh verified independently protected prebackup is mandatory before reset stages. | prebackup rejection and execution-order cases | PASS locally |
| RST-03 | Pending work and asynchronous stores are isolated before inactive-generation preparation; stage evidence must prove no unrelated scope was affected and old work cannot redeliver. | ordered-stage and invalid-evidence cases | PASS locally |
| RST-04 | Pristine verification reports zero selected-ledger business entities and resources ready before atomic activation/reimport. | invalid-pristine evidence and terminal-result cases | PASS locally |
| RST-05 | Failure, cancellation, and activation-response loss remain resumable; completed stages are not repeated and previous-generation rollback evidence is preserved. | stage failure/resume and activation-outcome recovery cases | PASS locally |
| RST-06 | Dedicated-environment reset rejects name-only targets, shared/unowned resources, stale confirmation, missing recent auth, or missing protected prebackup. | `EnvironmentResetGuardTests` | PASS locally |

RED evidence: focused reset tests initially failed to compile because the reset contracts did not exist. GREEN evidence after implementation:

- `dotnet test HomeBudget.Accounting.Api.Tests/HomeBudget.Accounting.Api.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~DataProtection.Reset" --logger "console;verbosity=minimal"`: 14/14 passed in 160 ms;
- production Infrastructure and shared test projects compiled with zero errors; repository-baseline warnings remain.

**BLOCKED for production reset:** no durable SQL reset journal adapter, protected prebackup adapter, Kafka/EventStore/Mongo/SQL isolation adapters, cache/job adapter, pristine-state API probe, or transactional generation router is registered. No positively identified disposable shared fixture has exercised delayed Kafka/EventStore/browser retries, process restart, physical resource preservation, or rollback. Environment reset has validation only and no destructive executor by design. Therefore AT-11, AT-13, and AT-14 remain BLOCKED as end-to-end capabilities even though the coordinator contract passes locally. No live environment was accessed.

## Task 9.1 Bounded recovery telemetry and alerts

### Observable contract

- Recovery-set creation, integrity verification, independent publication, and successful isolated restore drill are separate lifecycle counter states. Only independent publication advances the protected-cutoff timestamp.
- Capability readiness, cutoff/verification/drill availability and age, capacity headroom, accepted pending work, protection-stage failures, durable operation failures, and reconciliation failures use label-free gauges/counters except for the fixed `state` and `stage` allowlists.
- The safe status projection defaults to warning at 45 minutes and critical at 60 minutes. A local capture-created transition cannot change readiness, independent-protection state, or protected-cutoff age.
- Existing `homebudget.projection.lag` remains the projection-lag source; duplicating it under a second meter would create conflicting alert semantics.
- Metric APIs accept no IDs, paths, payloads, filenames, exception messages, credentials, or caller-provided label values. Unavailable ages publish the finite sentinel `-1` together with an explicit availability gauge.

### Verification and limits

- RED: the focused telemetry test did not compile because the bounded telemetry contract did not exist.
- GREEN: `DataProtectionTelemetryTests` passes 7/7 and covers all lifecycle labels, all failure-stage labels, separate unlabelled operation/reconciliation failures, rejection of invalid stages, default 45/60-minute thresholds, local-creation non-advancement, safe gauges, and finite unavailable values.
- Prometheus v3.15 `promtool check rules alerts.yml` validates all rules. `promtool test rules tests/data-protection-alerts.test.yml` proves 2,699 seconds is healthy, 2,700 seconds warns, and 3,600 seconds is critical even while the local `created` counter increases.
- The meter is registered with the existing OpenTelemetry Prometheus exporter. Coordinated capture records pending work, stages, verification, and independent publication; the durable operation processor records operation and reconciliation failures.
- Production readiness remains **BLOCKED** and therefore reports capability `0`: remote independent storage, key-provider recovery, current capacity measurement, notification routing, and an isolated restore drill have not been configured or verified. No production capability is inferred from local creation or checksum verification.

## Task 9.2 Fail-Closed Recovery Configuration

- `DataProtectionRecoveryOptions` supplies the requested initial targets: 30-minute cadence, maximum 60-minute RPO, four-hour target RTO, 30-day PITR, all points for 48 hours, daily points for 30 days, monthly points for 12 months, monthly drills, 45/60-minute warnings, and positive record/byte/coalescing thresholds.
- Feature enablement is independent from the administrative API. Enabling recovery validates bounded policy values plus non-empty provider, independent-store, separately recoverable key reference, notification destination, and recovery-host resource ID. Values are opaque references; keys and credentials are not accepted or exposed by capability status.
- The capability response reports bounded missing-prerequisite identifiers and remains `blocked` while recovery is disabled or any prerequisite/executor is absent. Empty Compose example values cannot produce a healthy result.
- Local and deploy Compose fragments map the same environment variables into the API; the worker receives only its disabled scheduling/cadence/coalescing inputs. `.env.example` contains no provider credentials or invented working endpoints.

RED/GREEN evidence: the options/readiness test first failed to compile because the contract did not exist, then passed 4/4 in 76 ms. Both local and deploy `docker compose ... config --quiet` passed using `.env.example`, and `scripts/validate-compose.ps1 -EnvFile .env.example` validated both stacks. Production remains **BLOCKED** because the example deliberately leaves all provider references empty and no executor/storage/key/notification/recovery host has been approved.

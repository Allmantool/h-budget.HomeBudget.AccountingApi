# HealthChecks UI IdentityModel runtime dependency

## Status

Verified

## Problem

The Accounting API HealthChecks UI collector repeatedly throws `FileNotFoundException`
for `IdentityModel` while polling its configured health endpoint. The endpoint itself
continues to answer, but the collector cannot persist a report and the UI API remains
empty.

## Goal

Make every runtime dependency used by the Accounting API HealthChecks UI collector
explicitly resolvable in restore and publish output, then prove that a published Linux
artifact completes repeated collection cycles.

## Non-Goals

- Changing health-check endpoint behavior, authentication, or collector error handling.
- Changing Gateway, Rates API, workers, or unrelated identity/authentication behavior.
- Deploying to or changing `vm2.linux`.
- Making unavailable local backing services healthy during isolated runtime verification.

## Repository Findings

### Confirmed

- `HomeBudget.Accounting.Api` is the affected executable. It registers
  `AddHealthChecksUI(...).AddInMemoryStorage()` and maps `/health-ui-api`; the worker
  does not register a UI collector.
- Gateway and Rates API also register HealthChecks UI, but their current restore graphs
  contain an `IdentityModel` package. Accounting is the only compared UI host whose
  graph and publish output omit `IdentityModel`.
- All three Accounting HealthChecks packages are centrally pinned to 9.0.0.
- `HealthChecks.UI.dll` 9.0.0 has an assembly metadata reference to
  `IdentityModel, Version=5.2.0.0`; this is evidence about the compiled assembly, not
  a NuGet package-version selection.
- `AspNetCore.HealthChecks.UI` 9.0.0 declares `KubernetesClient` 15.0.1.
  `KubernetesClient` 15.0.1 declares `IdentityModel.OidcClient` 5.2.1, which restores
  package `IdentityModel` 5.2.0.
- Central transitive pinning replaces that dependency with `KubernetesClient` 19.0.2.
  Its net10 assembly no longer references IdentityModel and the Accounting restore graph
  contains no `IdentityModel` package.
- Git history shows the current 19.0.2 pin was introduced after the previous 17.0.14
  security pin. The HealthChecks UI package itself was not changed.
- A fresh `linux-x64` framework-dependent publish has `HealthChecks.UI.dll` and
  `KubernetesClient.dll`, but neither `IdentityModel.dll` nor an `IdentityModel`
  `.deps.json` entry.
- Running that baseline publish in local `mcr.microsoft.com/dotnet/aspnet:10.0`
  reproduces the reported exception on every one-second collector cycle. Twelve
  occurrences were observed. `/health` separately returned HTTP 200 with an overall
  `Degraded` report because SQL Server, MongoDB, Kafka, and EventStoreDB were intentionally
  unconfigured; `/health-ui-api` returned HTTP 200 with an empty array.
- The exception is thrown when `HealthCheckReportCollector.GetHealthReportAsync` is JIT
  loaded. That method uses IdentityModel's basic-auth header type even though the local
  endpoint URI does not contain user information.
- NuGet package `IdentityModel` 7.0.0 is the latest published version and is also the
  version used by the Home Ledger Gateway. NuGet marks it legacy in favor of
  `Duende.IdentityModel`, but the latter has a different assembly identity and therefore
  cannot satisfy HealthChecks UI's direct `IdentityModel` reference. No newer stable
  HealthChecks UI package is currently published.
- The explicit `IdentityModel` 7.0.0 dependency resolves the UI's compiled reference in
  the test host and in the published .NET 10 Linux runtime. Its published assembly version
  is 7.0.0.0; compatibility was proven by executing the collector rather than inferred
  from the original 5.2.0.0 request.

### Assumed

- None.

### Unknown / Open Questions

- None that block the focused implementation. The selected package's runtime binding and
  collector behavior remain acceptance checks rather than assumptions.

## Current and Desired Behavior

Currently, restore considers the graph complete but publish omits a DLL directly referenced
by HealthChecks UI. The collector catches the resulting load failure once per polling cycle,
and the UI has no collected execution.

After the change, the dependency is explicit in the owning executable, restore and publish
contain it, and repeated collector polls populate `/health-ui-api`. A degraded endpoint
caused by deliberately unavailable databases or brokers is reported as endpoint state and
is not confused with an assembly-loading failure.

## Architecture and Consistency Context

This change is confined to the Accounting API package/publish boundary and its in-process
HealthChecks UI hosted service. It does not change a persistence, messaging, payment, or
financial consistency boundary.

## Requirements

- REQ-001: The Accounting API runtime must resolve every non-framework assembly directly
  referenced by the HealthChecks UI collector path.
- REQ-002: The existing HealthChecks UI, Client, and InMemory Storage packages must remain
  on one compatible version line.
- NFR-001: The fix must preserve the KubernetesClient security pin and avoid identity or
  authentication behavior changes.

## Acceptance Criteria

- AC-001: A focused regression test fails on the baseline because the IdentityModel
  reference discovered from `HealthChecks.UI.dll` cannot be loaded, and passes after the fix.
- AC-002: A fresh Accounting API Linux publish contains `IdentityModel.dll` and declares it
  in `HomeBudget.Accounting.Api.deps.json`.
- AC-003: The published artifact runs in the .NET 10 Linux runtime for at least three
  collector cycles with no IdentityModel/assembly-loading exception.
- AC-004: `/health-ui-api` contains the configured `[Accounting endpoint]` report after
  collection; backing-service health failures, if any, are reported separately.

## Failure Scenarios and Edge Cases

- FAIL-001: Adding a package whose assembly can be published but cannot satisfy the UI's
  compiled API usage must fail the runtime collector check.
- FAIL-002: A healthy process with unavailable backing services may report `Degraded`; that
  must not be treated as a collector or assembly-load failure.
- EDGE-001: The regression check must obtain the requested assembly identity from UI
  metadata rather than treating `5.2.0.0` as a NuGet package version.

## Test Strategy

Use an API unit test for the focused assembly-resolution invariant. Then publish the actual
API for `linux-x64`, inspect its DLL and dependency manifest, and run it from a read-only
mount in the production-family .NET 10 ASP.NET runtime container. Poll the real health and
UI API endpoints and inspect logs across multiple one-second collector cycles.

## Implementation Plan

1. Add and run the metadata-driven assembly-resolution test to establish RED.
2. Add the smallest explicit runtime package reference at the Accounting API boundary,
   with its version managed centrally.
3. Restore, run the focused test, build/test the API scope, publish, inspect, and run the
   Linux artifact for repeated collector cycles.

## Verification Strategy

- Focused RED/GREEN: filter to `HealthChecksUiRuntimeDependencyTests`.
- Existing tests: full `HomeBudget.Accounting.Api.Tests` Release suite.
- Build: Accounting API Release build (and solution build if central package changes affect
  restore across the solution).
- Publish/runtime: fresh `linux-x64` framework-dependent publish and local ASP.NET 10
  container; no remote host operations.
- Dependency inspection: `dotnet list package --include-transitive`, `dotnet nuget why`,
  publish file listing, and `.deps.json` search.

## Requirement Traceability

| Requirement / Criterion | Implementation | Test or evidence | Status |
|---|---|---|---|
| REQ-001 / AC-001 | Explicit API `IdentityModel` reference, centrally versioned at 7.0.0 | Focused test failed before and passed after the fix | Verified |
| REQ-002 | Existing central 9.0.0 package set unchanged | Final package graph: UI/Core/Data/Client/InMemory all 9.0.0 | Verified |
| NFR-001 | KubernetesClient remains 19.0.2; no auth code changed | Final graph, no-vulnerability audit, focused diff | Verified |
| AC-002 | API package reference | ReadyToRun Linux publish contains `IdentityModel.dll`; `.deps.json` declares 7.0.0 | Verified |
| AC-003 | API package reference | Three distinct collector execution timestamps; zero collector/load errors | Verified |
| AC-004 | No endpoint behavior change | `/health-ui-api` returned one `[Accounting endpoint]` report | Verified |

## Progress and Resume State

- Decisions made: fix the missing runtime dependency at the Accounting API package boundary
  with the latest published compatible package; do not revert KubernetesClient or change
  collector/authentication behavior.
- Implemented: central `IdentityModel` 7.0.0 version, direct Accounting API package reference,
  and metadata-driven runtime resolution regression test.
- Verification passed: focused RED reproduced the same missing assembly and GREEN passed;
  API tests passed 76/76; Release solution build passed with 0 errors and existing warnings;
  dependency audit found no known vulnerable packages; production-style ReadyToRun Linux
  publish contains the dependency and manifest entry; the published artifact completed three
  observed collector cycles in the .NET 10 Linux runtime with zero collector/load errors and
  exposed one UI report.
- Current failure or blocker: none. The isolated endpoint report is `Degraded` only because
  SQL Server, MongoDB, Kafka, and EventStoreDB were intentionally left unconfigured.
- Remaining work: none for this scoped fix.
- Follow-ups out of scope: any broader dependency modernization or health endpoint redesign.

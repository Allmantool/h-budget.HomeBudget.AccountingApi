# Prometheus Exporter Dependency Alignment

## Status

In Progress

## Problem

The current `origin/master` SharpAbp Prometheus exporter upgrade requires `OpenTelemetry.Exporter.Prometheus.AspNetCore >= 1.18.0-beta.1`, while Central Package Management pins that exporter at `1.13.1-beta.1`. NuGet reports NU1605 when the branches are combined for PR validation.

## Goal

Resolve a single compatible Prometheus exporter version through Central Package Management without suppressing dependency diagnostics or scattering package overrides.

## Non-Goals

- Upgrading unrelated OpenTelemetry packages.
- Changing telemetry registration or runtime behavior.
- Changing payment command behavior introduced by this PR, except for verified regressions.

## Repository Findings

### Confirmed

- `Directory.Packages.props` centrally manages package versions.
- The PR merge base is `c324194`; `origin/master` subsequently upgraded SharpAbp to `6.0.0`.
- `origin/master` pins `OpenTelemetry.Exporter.Prometheus.AspNetCore` to `1.13.1-beta.1` and SharpAbp `6.0.0` requires exporter version `>= 1.18.0-beta.1`.
- NuGet reports `1.18.0-beta.1` as the available latest exporter; no `1.18.0` stable exporter is listed.
- The CodeQL workflow manually restores and builds with .NET 9 despite the solution's `net10.0` target, then invokes a redundant autobuild.

### Assumed

- The existing Prometheus registration APIs are compatible with exporter `1.18.0-beta.1`; the build will verify this.

### Unknown / Open Questions

- None that block the central version alignment.

## Current and Desired Behavior

Current combined dependency resolution selects the direct central `1.13.1-beta.1` reference over SharpAbp's `>= 1.18.0-beta.1` transitive minimum and fails with NU1605. The centralized declaration must instead resolve `1.18.0-beta.1` for all direct references.

## Requirements

- REQ-001: The centrally managed Prometheus exporter version satisfies SharpAbp 6.0.0's declared minimum.
- REQ-002: CodeQL's manual C# restore and build use the solution's target SDK and explicit solution commands.
- NFR-001: Restore must not suppress NU1605 or weaken warning policy.

## Acceptance Criteria

- AC-001: `dotnet restore HomeBudgetAccountingApi.sln` has no Prometheus exporter NU1605.
- AC-002: Release build succeeds with the aligned central version.
- AC-003: The dependency listing resolves one exporter version compatible with SharpAbp 6.0.0.
- AC-004: The CodeQL manual restore/build commands are reproducible with the .NET 10 SDK.

## Test Strategy

Package graph requirements are proved by restore, package diagnostics, and build rather than a unit test. No production behavior changes are required, so a RED/GREEN code-test loop is not applicable.

## Implementation Plan

1. Confirm the SharpAbp dependency metadata and available exporter version.
2. Update the single central exporter declaration to the supported required beta.
3. Align CodeQL's manual build with the solution SDK and remove the duplicate autobuild.
4. Restore, inspect the resolved graph, build, and run the affected test suites.

## Verification Strategy

Run solution restore, Release build, Infrastructure package diagnostics, the API and Operations unit suites, and relevant integration tests where the environment permits.

## Requirement Traceability

| Requirement / Criterion | Implementation | Test or evidence | Status |
|---|---|---|---|
| REQ-001 | `Directory.Packages.props` | NuGet package metadata and resolved package listing | In Progress |
| REQ-002 | `.github/workflows/codeql-analysis.yml` | Reproduced manual CodeQL restore/build commands | In Progress |
| NFR-001 | N/A | Restore output and diff review | In Progress |
| AC-001 | `Directory.Packages.props` | `dotnet restore` | Planned |
| AC-002 | `Directory.Packages.props` | Release build | Planned |
| AC-003 | `Directory.Packages.props` | `dotnet list package --include-transitive` | Planned |
| AC-004 | `.github/workflows/codeql-analysis.yml` | .NET 10 Debug restore/build | Planned |

## Progress and Resume State

- Decisions made: use the only authoritative central version owner and align it to `1.18.0-beta.1`.
- Implemented: central exporter and CodeQL SDK/manual build alignment.
- Verification passed: current branch restores only because it remains on SharpAbp 4.7.3; this does not prove the PR merge result.
- Current failure or blocker: pending branch-equivalent dependency alignment and full PR audit.
- Remaining work: apply alignment, validate it, and finish reviewing the payment-idempotency diff.
- Follow-ups out of scope: existing package vulnerability and duplicate-reference warnings unrelated to the PR.

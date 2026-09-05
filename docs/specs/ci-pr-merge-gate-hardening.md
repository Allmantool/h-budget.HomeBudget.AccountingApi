# CI PR Merge Gate Hardening

## Status

Implemented

## Problem

`master` has no GitHub branch-protection rule and no repository ruleset. Therefore,
GitHub exposes a merge action even when CI Master and CodeQL checks fail. The current
CI workflow also has no stable, always-created aggregate check that represents all
mandatory Accounting verification.

## Goal

Every pull request targeting `master` must publish stable mandatory checks and must
not be mergeable through the normal GitHub UI unless build, unit/component tests,
integration tests, and quality verification succeed, and CodeQL C# analysis succeeds.

## Non-Goals

- Repair the current OpenTelemetry package downgrade or other application defects.
- Add new static-analysis products or change application test behavior.
- Create a merge queue, deployment gate, or release workflow redesign.
- Perform a broad action SHA-pinning migration.

## Repository Findings

### Confirmed

- `.github/workflows/ci-master.yml` runs on pull requests to `master`, has no path
  filters, and runs build validation before unit/component and integration tests.
- A failed `validate-application-build` causes both test jobs to be skipped by
  GitHub Actions' default `needs` behavior. `analyze-master-quality` is also skipped
  by its explicit condition, while `publish-ci-summary` still succeeds because it uses
  `if: always()`.
- CI currently has no aggregate gate job. Existing CI check names are job names and
  are not configured as required checks in GitHub.
- CI test coverage comprises `HomeBudget.Accounting.Api.Tests`,
  `HomeBudget.Components.Categories.Tests`, `HomeBudget.Components.Operations.Tests`,
  and Docker/Testcontainers-backed `HomeBudget.Accounting.Api.IntegrationTests`.
- `Directory.Build.props` enables analyzers and code style enforcement during the
  Release solution build.
- `codeql-analysis.yml` runs C# analysis on pull requests to `master` and already
  uses a .NET 10 manual build, but it uses deprecated CodeQL v2 actions. The observed
  CodeQL failure was a v2 configuration error.
- GitHub API audit on 2026-09-05: repository default branch is `master`; the branch
  protection endpoint returned `404 Branch not protected`; repository rulesets were
  an empty array. All merge methods are enabled. Pull request #823 was mergeable while
  `Validate Application Build` and `Analyze (csharp)` had failed.
- The current CI failure was a real NuGet downgrade (`NU1605`), and the CodeQL failure
  was a CodeQL v2 configuration error. These failures are not hidden by the workflow.
- CI uses `set -euo pipefail` for test commands and publishes test artifacts with
  `if: always()`. The audit found ignored Docker cleanup failures (`|| true`); the
  hardening change removes those ignored exits so unavailable Docker fails loudly.
- No reusable workflows, composite actions, repository ruleset IaC, branch-protection
  scripts, `pull_request_target` triggers, or PR path filters were found.

### Assumed

- The authenticated `gh` account has enough repository administration authority to
  create a repository ruleset after the audit. This is verified only when the API
  change succeeds.

### Unknown / Open Questions

- Whether a documented emergency break-glass process exists outside this repository.
  The target ruleset will not grant bypass actors.

## Current and Desired Behavior

Today, failed or skipped CI jobs appear in a pull request, but nothing requires them
before merge. After this change, CI Master emits `CI Master / PR Gate` for every
applicable PR, and it fails unless every mandatory CI job result is `success`.
CodeQL emits `CodeQL / Analyze (csharp)` separately. A repository ruleset requires
both contexts on the latest mergeable commit and requires a reviewed, up-to-date PR.

## Architecture and Consistency Context

Affected boundary: GitHub Actions check runs -> GitHub repository ruleset -> pull
request merge UI. The durable authority for merge permission is GitHub's active
repository ruleset, not the workflow YAML. GitHub Actions results are the gate inputs;
the final CI job is an aggregation of the mandatory CI job results.

## Requirements

- REQ-001: CI Master must publish the stable `PR Gate` job for every pull request to
  `master`, independent of upstream job outcomes.
- REQ-002: The gate must fail when any mandatory CI job is `failure`, `cancelled`, or
  `skipped`; it may succeed only when every mandatory CI job is `success`.
- REQ-003: Build validation, all three unit/component projects, integration tests,
  and existing quality/coverage validation remain mandatory.
- REQ-004: CodeQL must analyze C# using the repository's .NET 10 build and publish a
  stable `Analyze (csharp)` check.
- NFR-001: GitHub must enforce the stable CI and CodeQL check contexts for `master`,
  require PR review and conversation resolution, prohibit force pushes and branch
  deletion, require current base branch validation, and have no routine bypass actor.
- NFR-002: Test and coverage diagnostics remain available after a test failure.

## Acceptance Criteria

- AC-001: A successful build, tests, integration suite, and quality job yield a green
  `CI Master / PR Gate`.
- AC-002: Failed, cancelled, or skipped mandatory jobs yield a red PR Gate.
- AC-003: A CodeQL C# failure yields a failed `CodeQL / Analyze (csharp)` check.
- AC-004: The active `master` ruleset requires exactly the stable CI and CodeQL
  contexts and prevents normal merges while either is not successful.
- AC-005: Artifact publication runs after test failure without converting the failed
  test job into success.

## Failure Scenarios and Edge Cases

- FAIL-001: Build failure skips dependent test jobs. The PR Gate must still run and
  fail.
- FAIL-002: A test, integration, or quality job fails or is cancelled. The PR Gate
  must fail.
- EDGE-001: An unexpected job-level condition skips a mandatory job. The PR Gate
  must fail rather than treating the skip as success.
- EDGE-002: A new commit is pushed. Strict required checks must be reported for the
  new mergeable commit; stale successful checks cannot satisfy the ruleset.

## Test Strategy

Use a focused Bash truth-table test for the gate helper to prove all mandatory result
states. Validate workflow syntax with `actionlint` when available. Verify the active
GitHub ruleset via `gh api`, which is the only boundary that proves GitHub merge
enforcement. Existing CI commands remain the runtime proof of application validation.

## Implementation Plan

1. RED: add and run a truth-table test that expects a missing gate helper.
2. GREEN: add the helper and invoke it from an always-created `PR Gate`; update
   CodeQL to supported actions and a .NET 10 manual build.
3. Configure the `master` repository ruleset with the two stable contexts and verify
   it through GitHub API; document the policy and re-verification command.

## Verification Strategy

- Run the gate truth table for success, failure, cancelled, and skipped inputs.
- Run workflow lint/syntax validation and inspect the focused diff.
- Query the active ruleset and `master` branch protection/ruleset evidence with `gh`.
- Do not run the Docker-backed application integration suite locally solely for this
  workflow/governance change; CI remains its execution boundary.

## Requirement Traceability

| Requirement / Criterion | Implementation | Test or evidence | Status |
|---|---|---|---|
| REQ-001, REQ-002, AC-001, AC-002 | CI gate helper and `ci-master.yml` | Bash truth table and workflow structure validation | Verified |
| REQ-003, AC-005 | `ci-master.yml` | Focused workflow review | Verified |
| REQ-004, AC-003 | `codeql-analysis.yml` | Workflow structure validation; runtime pending first changed workflow run | Implemented |
| NFR-001, AC-004 | GitHub repository ruleset and branch-protection runbook | Ruleset `22327976` queried through `gh api` | Verified |

## Progress and Resume State

- Decisions made: Require only the stable aggregate CI context and the separate
  CodeQL context; individual CI job names remain diagnostics, not branch-protection
  contracts.
- Implemented: Added the always-created PR Gate, an executable gate evaluator and
  truth-table test, CodeQL v3/.NET 10 manual-build configuration, documentation, and
  active GitHub ruleset `22327976`.
- Verification passed: Gate truth table, Bash syntax validation, workflow structure
  validation, focused diff check, and GitHub ruleset/rules query.
- Current failure or blocker: Runtime emission of the new PR Gate context is pending
  the first push of these workflow changes; the current remote PR commit predates it.
- Remaining work: Push the workflow change, observe both required check contexts on
  its PR head, and confirm a failed mandatory CI run makes the PR UI non-mergeable.
- Follow-ups out of scope: Resolve the observed package downgrade and renovate action
  security/permissions hardening separately.

# Accounting CI/CD Traceability

## Status

Implemented — integration validation interrupted

## Problem

Accounting creates a Git tag and GitHub Release from a merged-pull-request event while
the full .NET CI workflow runs independently. A release and its Docker images can
therefore be published even when the corresponding integration tests fail. Deployment
metadata exists, but the Actions run is named only `Release Tag`, image tags are
overwriteable on rerun, and the GitHub Release does not identify the image digests or
deployment workflow.

## Goal

Give Accounting the same observable release chain as the SPA while retaining its
stronger backend verification:

`verified commit -> immutable vMAJOR.MINOR.PATCH tag -> GitHub Release and notes ->
immutable API/worker images -> GitHub production deployment`.

## Non-Goals

- Change application architecture, runtime behavior, or test semantics.
- Modify the SPA repository.
- Add an architecture-test project that does not exist today.
- Add new external CI services or a custom CI framework.
- Repair application or integration-test failures unrelated to workflow behavior.

## Repository Findings

### Confirmed

- SPA PR CI uses read-only permissions, PR-scoped concurrency with cancellation,
  lock-file caching, explicit verification steps, timeouts, and immutable checkout.
- SPA releases run after verification on `master`; semantic-release creates a stable
  SemVer tag and GitHub Release, then explicitly dispatches a version-named deployment.
- SPA deployment checks out and validates the release tag, uses the existing
  `production` GitHub Environment, publishes version/SHA image tags, prevents a tag
  from being silently rebound, and writes a GitHub Step Summary.
- Accounting PR CI builds the solution, runs three unit/component test projects, runs
  Docker/Testcontainers integration tests, merges coverage, optionally publishes to
  Sonar, uploads diagnostics, and exposes the stable `PR Gate` check required by the
  active repository ruleset.
- Accounting has no architecture-test project or dedicated format target. Release
  builds already run the repository analyzers configured by `Directory.Build.props`.
- Accounting `update_semver.yml` runs when a PR is closed and can create a tag and
  GitHub Release before `CI Master` completes. Live release `v0.0.701` was created for
  commit `cc61cb4` while that commit's integration-test run failed; the tag workflow
  nevertheless published both images and marked the production deployment successful.
- Accounting already has semantic-release dependencies/configuration, stable `v*`
  tags, a `production` environment, and API/worker Docker images.
- Existing image publication uses only the release tag and does not reject an existing
  tag that identifies a different commit.

### Assumed

- `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` remain available to the existing
  `production` deployment job.
- Repository merge policy continues to require `PR Gate` and `Analyze (csharp)`.

### Unknown / Open Questions

- The external runtime platform that pulls the published images is outside this
  repository. The existing workflow treats Docker Hub publication as deployment, so
  this change preserves that boundary.

## Current and Desired Behavior

Today, a merged PR independently starts CI and tag creation. The tag starts a build-only
image workflow and a manually-created GitHub Deployment. Reruns can push the same image
tag again.

After this change, PRs directly run `Accounting PR Verification`. A push to `master`
starts `Accounting Release`, which calls the same verification workflow and cannot
publish unless every mandatory .NET job succeeds. Semantic-release then creates at most
one stable tag/Release and dispatches `Deploy <tag>`. Deployment validates the tag/SHA,
builds both images, refuses conflicting remote tags, publishes both release and SHA
tags, records digests, updates the GitHub Release traceability section, and runs under
the existing `production` environment.

## Architecture and Consistency Context

Affected boundary: GitHub pull request/push events -> GitHub Actions verification ->
semantic-release/Git tag/GitHub Release -> Docker Hub image publication -> GitHub
Environment deployment. The Git tag is the immutable release identity. Docker tags are
accepted only when their OCI revision/version labels match that identity; image digests
are the immutable deployable identities.

## Requirements

- REQ-001: PRs to `master` expose clearly named Accounting build, unit/component,
  integration, quality, and aggregate-gate checks with superseded runs cancelled.
- REQ-002: Release publication is gated by the same build, unit/component,
  integration, and quality verification used for PRs.
- REQ-003: Stable versions are derived by semantic-release from `master` Conventional
  Commit history and map to exactly one commit/tag.
- REQ-004: Every published version has one GitHub Release with GitHub-generated change
  notes and PR links where GitHub metadata permits.
- REQ-005: Deployment visibly runs as `Deploy vX.Y.Z` or `Redeploy vX.Y.Z` in the
  existing `production` GitHub Environment.
- REL-001: Rerunning/redeploying cannot overwrite a version or SHA image tag belonging
  to different source metadata.
- NFR-001: PR execution has read-only repository permissions and no broad secret
  exposure; release/deployment write permissions are scoped to the jobs that need them.
- NFR-002: Release/deployment summaries and metadata expose version, commit, source
  ref, workflow runs, environment, image refs, and image digests without secrets.

## Acceptance Criteria

- AC-001: `Accounting PR Verification / PR Gate` remains stable for branch protection,
  and the workflow has PR-scoped cancellation and explicit job names.
- AC-002: A failed or skipped mandatory .NET verification job prevents tag/Release and
  deployment dispatch.
- AC-003: A release-producing `master` commit creates one `vMAJOR.MINOR.PATCH` tag and
  one GitHub Release at the verified commit.
- AC-004: Generated notes show changes since the previous release and include PRs when
  GitHub can associate them.
- AC-005: Both API and worker images are published as `<release-tag>` and
  `sha-<full-commit>`, with revision/version OCI labels and recorded digests.
- AC-006: A conflicting existing image tag fails before any conflicting tag is pushed;
  an identical existing tag is reused safely.
- AC-007: GitHub records a production deployment and the release page/metadata identify
  both workflow runs and exact image digests.

## Failure Scenarios and Edge Cases

- FAIL-001: Build, test, integration, or quality verification fails. The release job is
  skipped and no tag or deployment is produced.
- FAIL-002: A requested tag is malformed, missing, not reachable from `master`, or does
  not match the supplied commit. Deployment fails before Docker authentication/push.
- FAIL-003: Docker Hub already contains a release/SHA tag with different OCI source
  metadata. Deployment fails rather than overwriting it.
- FAIL-004: Image publication fails. The GitHub Environment deployment is marked failed
  automatically and release metadata is not reported as successfully deployed.
- EDGE-001: No release-producing commits exist. semantic-release publishes nothing and
  no deployment is dispatched.
- EDGE-002: A deployment is rerun for the same tag. Matching immutable tags are reused,
  metadata/release assets are updated idempotently, and no duplicate release is created.

## Test Strategy

Declarative workflow behavior has no meaningful production-code RED test. Validate the
contract with actionlint/YAML parsing, semantic-release dry-run where credentials allow,
the existing PR-gate truth table, shell syntax extraction, focused static assertions,
and the actual .NET restore/build/unit/component gates. Run Docker-backed integration
tests only if the local Docker environment can support the repository fixture.

## Implementation Plan

1. Make the existing Accounting CI workflow reusable and improve PR naming,
   permissions, checkout integrity, and cancellation without changing its test content.
2. Replace the PR-closed tag workflow with a serialized, verified `master` release that
   uses semantic-release and GitHub-generated notes.
3. Convert the tag-triggered image workflow into an explicit versioned deployment with
   environment tracking, version/SHA tags, overwrite protection, digests, release
   metadata, and summaries.
4. Align semantic-release configuration/dependencies with the SPA stable-release policy.
5. Run workflow/static validation, the PR-gate tests, relevant .NET commands, and a
   focused security/diff review; update this specification with evidence.

## Verification Strategy

- Parse all changed workflow YAML and run actionlint if available.
- Run `scripts/ci/test-evaluate-pr-gate.sh` and Bash syntax validation.
- Run `npm ci`, semantic-release configuration validation/dry-run where safe, and npm
  dependency validation.
- Run solution restore/build and the three unit/component test projects.
- Run the integration project only if Docker/Testcontainers is viable; report its exact
  result without weakening or retry-hiding failures.
- Statically assert triggers, permissions, dependencies, environment, tag/SHA checks,
  image labels/tags, release-note generation, and summary metadata.

## Requirement Traceability

| Requirement / Criterion | Implementation | Test or evidence | Status |
|---|---|---|---|
| REQ-001, AC-001 | `ci-master.yml` | YAML parse and PR-gate truth table | Verified |
| REQ-002, AC-002 | `update_semver.yml` calls reusable CI | Workflow dependency inspection | Verified |
| REQ-003, AC-003 | `.releaserc.json`, `update_semver.yml` | semantic-release dry-run/config validation | Verified |
| REQ-004, AC-004 | GitHub generated-notes API in release workflow | Workflow contract inspection | Verified |
| REQ-005, AC-007 | `release-tag.yml` run name and environment | YAML parse and workflow contract inspection | Verified |
| REL-001, AC-005, AC-006 | release-ref and remote-tag validation | Workflow contract inspection | Verified |
| NFR-001 | workflow/job permissions and secret scope | Focused security review | Verified |
| NFR-002 | summaries, release metadata, Release trace block | Workflow contract inspection | Verified |

## Progress and Resume State

- Decisions made: Preserve the stable `PR Gate` check name; reuse the existing
  `production` environment and Docker Hub image names; use semantic-release only on
  verified `master`; use GitHub-native generated notes; do not invent architecture or
  format gates absent from the repository.
- Implemented: Reusable Accounting verification; serialized `master` release gated by
  that verification; semantic-release tag generation; GitHub-generated Release notes;
  explicit versioned deployment; production environment tracking; immutable version and
  SHA Docker tags; existing-tag validation; release metadata and Release trace section.
- Verification passed: YAML/JSON parsing, workflow contract checks, PR-gate truth table,
  `npm ci --ignore-scripts`, semantic-release dry-run, solution restore/build, and all
  three unit/component test projects (103 tests).
- Current failure or blocker: The Docker/Testcontainers integration command was running
  with all required containers healthy but was interrupted before a final result when the
  local Docker daemon stopped.
- Remaining work: Re-run the integration project in a stable Docker Desktop session and
  inspect the first GitHub Actions release/deployment run after this workflow is merged.
- Follow-ups out of scope: Diagnose the application-level integration failure observed
  in CI run `33970673183`; govern the external platform that consumes Docker Hub tags.

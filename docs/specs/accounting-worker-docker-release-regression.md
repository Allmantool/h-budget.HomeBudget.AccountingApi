# Accounting Worker Docker Release Regression

## Status

Verified

## Problem

The Accounting deployment workflow still selects
`HomeBudget.Accounting.Workers.OperationsConsumer/Dockerfile`, although that file was
renamed to the repository-root `worker.dockerfile` between v0.2.16 and v0.2.17.
Consequently, the v0.2.17 and v0.2.18 worker image builds fail before reading a
Dockerfile. The API image build and publication can still run independently, leaving
both releases partially published.

## Goal

Build the worker image explicitly from root `worker.dockerfile` with repository-root
context and the `final` target; fail before either image build when a selected release
source lacks an expected build input; and require both image builds to succeed before
either image publication can begin.

## Non-Goals

- Change application behavior, payment processing, projection recovery, migration
  state, or runtime deployment.
- Change Docker base images, GitHub Actions versions, dependencies, caches, image
  names, immutable-tag checks, or release identity semantics.
- Recreate the removed nested Dockerfile, move existing immutable tags, publish images,
  dispatch workflows, or modify production hosts.
- Make two independent Docker Hub pushes atomic.

## Repository Findings

### Confirmed

- Current `master` at `79c64ac157f4f2ce78f01d53add866a899bd2f3e`
  still uses the removed nested worker Dockerfile path in
  `.github/workflows/release-tag.yml`.
- `git diff --summary v0.2.16..v0.2.17` reports the worker Dockerfile rename to
  `worker.dockerfile` with 98% similarity.
- Source commit `a305aba7c197dea20532e2f863a04d532d48dd35` contains exact-case root files
  `dockerfile` and `worker.dockerfile`, and no nested worker `Dockerfile`.
- Deployment runs `36353251477` (v0.2.17) and `36354769614` (v0.2.18) failed with
  `failed to read dockerfile: open Dockerfile: no such file or directory`.
- In both failed runs, the API image build and publication succeeded while the worker
  build failed and worker publication was skipped.
- The root worker Dockerfile has a `final` stage and entry point
  `dotnet HomeBudget.Accounting.Workers.OperationsConsumer.dll`.
- Both image build jobs check out the immutable tag output by `verify-release`, but
  current publication jobs depend only on their corresponding build.
- Existing Node tests in `tools/ci/release-policy.spec.mjs` already inspect release
  workflow contracts, but the current test reads the removed worker path and is not run
  by `ci-master.yml`.
- No active Compose definition references the removed path. The only active stale
  references are the release workflow and its workflow contract test.

### Verified Environment and Registry State

- The release-policy fixtures execute the workflow's Bash validator through Git Bash
  on Windows and explicitly prove Linux-compatible exact-case filename behavior.
- Docker Desktop's Linux Buildx builder completed real worker and API archive builds
  from clean source snapshots overlaid with the reviewed patch.
- Read-only Docker Hub inspection found API tags `0.2.17` and `0.2.18`; the matching
  worker tags and both `0.2.19` tags were absent. Remote Git tag `v0.2.19` was also
  absent at verification time.

## Current and Desired Behavior

Currently, release identity validation succeeds, both builds start, the worker build
selects a nonexistent file, and the independent API publication can proceed. Desired
behavior validates the exact release checkout inputs first, uses root
`worker.dockerfile`, and makes each push wait for both successful builds. A missing or
case-mismatched input fails with the selected tag, SHA, repository/build-context path,
and missing expected path.

## Architecture and Consistency Context

Affected boundary only: immutable Git tag checkout -> GitHub Actions input validation
-> independent API/worker archive builds -> independent immutable Docker Hub pushes ->
release trace record. The Git tag and verified commit SHA remain the release source of
truth. Each build job continues to check out that tag. Publication is still non-atomic,
but it cannot begin until both archives build successfully.

## Requirements

- REQ-001: The worker build uses context `.`, file `./worker.dockerfile`, and target
  `final`; the API build remains context `.`, file `./dockerfile`, target `final`.
- REQ-002: Release identity verification checks nonempty exact-case `dockerfile`,
  `worker.dockerfile`, and the worker project in the selected release checkout before
  either build job can start.
- REQ-003: A failed input check reports the selected tag, selected SHA, effective
  repository/build-context location, expected path, and missing input without exposing
  environment values or secrets.
- REL-001: Both image build jobs must succeed before either image publication job can
  start; existing immutable-tag checks and final release success checks remain intact.
- NFR-001: PR/master verification exercises the actual release workflow inputs,
  dependency graph, input validator, and Linux filename case behavior.

## Acceptance Criteria

- AC-001: Workflow contract tests reject the historical nested worker Dockerfile path
  and confirm separate root API/worker Dockerfiles and the `final` targets.
- AC-002: Temporary-fixture tests prove a missing/moved or case-mismatched worker
  Dockerfile fails early with actionable diagnostics, while a complete clean fixture
  passes.
- AC-003: Workflow contract tests prove `verify-release` precedes both builds and both
  builds precede either push, including a negative API-only-success simulation.
- AC-004: A real Linux Buildx build from clean reviewed inputs produces a worker Docker
  archive with local-only version/SHA metadata, the expected entry point and runtime
  files, and no API/test/private data payload.
- AC-005: A real API image build using the unchanged root `dockerfile` still succeeds.
- AC-006: The release record cannot report success when the worker path/build/push
  fails.

## Failure Scenarios and Edge Cases

- FAIL-001: `worker.dockerfile` is missing, empty, moved to its historical location, or
  differs only by filename case. Input validation fails before builds.
- FAIL-002: The worker build fails after input validation. Both publication jobs remain
  blocked, and the overall deployment remains failed.
- FAIL-003: One publication fails after both builds succeed. The other publication may
  already have occurred; the existing release record remains incomplete and failed.
- EDGE-001: A manual redeploy selects an older immutable tag that predates the new input
  validator or root worker path. The workflow revision is current, but the selected tag
  checkout is validated and rejected rather than substituted with current `master`.

## Test Strategy

Extend the existing Node release-policy suite with minimal workflow-section parsing and
temporary fixture execution of the workflow's inline Bash input validator. Run that suite from the
reusable PR/master verification workflow. Use a real Linux Buildx build for worker
packaging and archive inspection; use a real API build to guard the unchanged path.
No application integration suite is required because no application/distributed
behavior changes.

## Implementation Plan

1. RED: update the actual-workflow regression tests and fixture tests, then confirm
   failures for the stale path, absent early validator, and independent push graph.
2. GREEN: add the narrow input-validation script and workflow step, select root
   `worker.dockerfile`, gate both pushes on both builds, and run the suite in reusable
   CI.
3. REFACTOR/VERIFY: validate shell/YAML, reproduce the historical missing path in an
   isolated snapshot, build worker/API archives through Linux Buildx, inspect worker
   contents/config/labels, review the focused diff, and update this specification.

## Verification Strategy

- `npm run test:release-policy` before and after implementation.
- Bash syntax and negative/positive fixture tests on Linux semantics.
- YAML parse/actionlint if locally available, plus direct workflow contract assertions.
- Historical path reproduction from an isolated v0.2.18 source snapshot.
- Uncached Buildx archive builds with `BUILD_VERSION=0.0.0` and a local patch identity.
- Docker archive/image config and filesystem inspection; no registry push.

## Requirement Traceability

| Requirement / Criterion | Implementation | Test or evidence | Status |
|---|---|---|---|
| REQ-001, AC-001 | `.github/workflows/release-tag.yml` | Actual workflow contract test; final-snapshot worker/API builds | Verified |
| REQ-002, REQ-003, AC-002 | inline validator in `verify-release` | Positive, missing/moved, and wrong-case temporary fixtures | Verified |
| REL-001, AC-003, AC-006 | both publication `needs` graphs; existing record gate | DAG contract and worker-failure negative simulation | Verified |
| NFR-001 | `.github/workflows/ci-master.yml`, Node tests | Reusable CI step; 13 local tests passed | Verified |
| AC-004 | unchanged `worker.dockerfile` packaging | Buildx archive/config/filesystem inspection | Verified |
| AC-005 | unchanged `dockerfile` packaging | Buildx API archive/config inspection | Verified |

## Progress and Resume State

- Decisions made: Keep the root worker file canonical; validate build inputs inline in
  `verify-release` so checkout of an older immutable source tag cannot remove the
  validator; keep separate archive/build/push jobs; require both archives before either
  push; and exercise the actual workflow contract from reusable CI.
- Implemented: Corrected the worker file path, added early exact-case/nonempty input
  checks, gated both pushes on both builds, added executable regression coverage, and
  wired that coverage into PR/master validation.
- Verification passed: RED reproduced four missing-contract failures; GREEN passed all
  13 Node tests. Both workflow files parsed as YAML and `git diff --check` passed. An
  isolated v0.2.18 build reproduced the historical missing-Dockerfile error. Linux
  Buildx produced both final-snapshot archives with identity
  `local-79c64ac157f4-patch-cf54f80d3185`; worker config/filesystem inspection confirmed
  its entry point, required runtime files, embedded identity, absence of the API DLL,
  and absence of source/test/database/Dockerfile payload. API config/runtime inspection
  confirmed its existing entry point, required files, and embedded identity.
- Current failure or blocker: None. `actionlint` was not installed locally; direct YAML
  parsing and executable workflow-contract tests were used instead.
- Remaining owner work: Review and commit the focused patch, let reusable CI pass, and
  create the normal next patch release. A no-CI semantic-release dry run predicts
  `0.2.19`, and neither the local nor remote `v0.2.19` tag exists. Do not rerun the
  original failed workflow runs, because those runs retain their historical workflow
  definition.
- Follow-ups out of scope: Projection/migration recovery, dependency upgrades, action
  warning cleanup, the existing API Dockerfile lint/label warnings, production
  deployment, and registry publication.

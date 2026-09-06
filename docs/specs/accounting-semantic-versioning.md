# Accounting semantic versioning

## Status

Implemented

## Problem

Accounting has semantic-release tooling and historical tags, but its configured
release rules are incomplete, PR squash metadata is not validated, and the
release contract is not documented or tested as one system.

## Goal

Create deterministic, traceable stable Accounting releases after successful
`master` verification without allowing a PR or a feature branch to create a
stable tag.

## Non-Goals

- Retag, rename, or delete historical releases.
- Introduce prerelease channels or mutable Docker aliases.
- Change accounting application behavior.

## Repository Findings

### Confirmed

- The SPA uses `semantic-release` on a `push` to `master`, full Git history,
  `v${version}` tags, a serialized release group, and `contents: write` only
  in its release job.
- SPA release rules are: breaking change → MAJOR; `feat` → MINOR; `fix`,
  `perf`, `revert`, `refactor`, `chore`, `build`, and `ci` → PATCH; `docs`,
  `test`, and `style` → no release.
- Accounting has annotated stable tags through `v0.0.701`; its repository also
  contains historical prerelease tags such as `v0.0.291-build1`.
- Accounting's `update_semver.yml` runs only on a `master` push, serializes
  runs, reuses the complete verification workflow, and is the only workflow
  that invokes `semantic-release`.
- The suspected missing-tag behavior is not confirmed by current remote state:
  GitHub Actions run `34023412731` is actively verifying the latest `master`
  SHA `95f3ad63835b2cd7acdf0cd15fa70cb0f3c75fb3` before it can publish a tag.
- Accounting PR verification has `contents: read`, checks out the PR head with
  `persist-credentials: false`, and has no tag or GitHub Release command.
- Accounting's prior `.releaserc.json` only explicitly described `fix`,
  `feat`, and breaking changes. It did not document or test the SPA's full
  deployable-application policy.

### Assumed

- Repository branch protection requires the PR quality gates and permits the
  `GITHUB_TOKEN` used by the `master` release workflow to create tags/releases.
  GitHub repository settings are not available from this checkout.

## Semantic Version Contract

- The stable release identity is an annotated, immutable `vMAJOR.MINOR.PATCH`
  tag reachable from `master`. Invalid, foreign, and prerelease tags are not
  release baselines.
- `semantic-release` is the single automatic stable-tag owner. It derives the
  next version from Conventional Commits reachable since the latest valid tag.
- A `!` marker or `BREAKING CHANGE:` / `BREAKING CHANGES:` footer is MAJOR;
  `feat` is MINOR; `fix`, `perf`, `revert`, `refactor`, `chore`, `build`, and
  `ci` are PATCH; `docs`, `test`, and `style` do not release by themselves.
- With no valid stable tag, the first release-worthy `master` revision uses
  semantic-release's deterministic `v1.0.0` bootstrap. No-release history does
  not create a bootstrap tag.
- PRs must have a Conventional Commit title matching their supported branch
  intent. This preserves the semantic signal when GitHub squash merging uses
  the PR title as the squash commit subject. Branch names validate intent only;
  they never select a version increment.
- Feature, development, hotfix, and PR builds never create a stable tag or
  GitHub Release. A merged `hotfix/*` PR is represented by `fix:` and normally
  releases PATCH; a breaking marker still releases MAJOR.
- The serialized `master` workflow recalculates after verification with full
  history and tags. A rerun at an already tagged commit creates no new tag and
  verifies the existing tag/release identity instead. Conflicting existing tag
  or release targets fail rather than being overwritten.
- Deployment accepts an existing stable tag only. API and worker images use the
  immutable version tag and SHA tag, OCI version/revision/source labels, and
  .NET `Version`/`InformationalVersion` build metadata derived from that
  identity.

## Release Decision Matrix

| Context | Version calculation | Stable tag | GitHub Release / deployment |
|---|---:|---:|---:|
| Feature/development/hotfix push | No | No | No |
| PR validation | Title validation only | No | No |
| Failed `master` verification | No publish step | No | No |
| Successful release-worthy `master` push | Yes | Yes | Yes |
| `docs`/`test`/`style`-only `master` push | Yes, no impact | No | No |
| Rerun at an already tagged commit | Existing identity checked | No duplicate | No duplicate release identity |
| Breaking `master` change | Yes, MAJOR | Yes | Yes |

## Requirements

- REQ-001: Stable version decisions follow the SPA's documented Conventional
  Commit policy and the `vMAJOR.MINOR.PATCH` tag format.
- REQ-002: PR verification validates squash-safe semantic metadata but cannot
  write a Git tag, GitHub Release, or Docker image.
- REL-001: Release calculation uses full history, valid stable tags only,
  serialized `master` execution, and immutable-tag conflict checks.
- REL-002: A successful release records one source SHA in the Git tag, GitHub
  Release, Docker labels/tags, and .NET artifact metadata.

## Acceptance Criteria

- AC-001: Focused tests prove PATCH, MINOR, MAJOR, mixed, breaking, no-release,
  bootstrap, invalid-tag, and rerun decision semantics.
- AC-002: PR policy validation has read-only permissions and does not invoke
  semantic-release.
- AC-003: The only automatic tag path is the post-verification `master` push
  workflow, with serialized execution and full checkout history.
- AC-004: Release Docker builds receive normalized SemVer and the release SHA
  as .NET assembly metadata inputs.

## Failure Scenarios and Edge Cases

- EDGE-001: Historical `*-buildN` tags are not stable release baselines.
- EDGE-002: A docs/test/style-only `master` change creates no tag.
- EDGE-003: A rerun observes an existing tag targeting the same SHA and does
  not calculate a second release; a tag/release targeting another SHA fails.
- EDGE-004: Concurrent `master` pushes are sequenced, so each run calculates
  after the preceding tag is visible.

## Test Strategy

Node's built-in test runner invokes semantic-release's commit analyzer with the
repository configuration. A small policy unit test covers PR title/branch
validation. YAML is parsed during workflow validation. .NET build proves the
metadata changes compile without changing application code.

## Implementation Plan

1. RED: add semantic-policy tests before the Accounting policy module exists.
2. GREEN: centralize semantic-release configuration and add the read-only PR
   metadata workflow.
3. Harden release idempotency/observability and pass normalized version/SHA to
   Docker builds.
4. Run focused release tests, YAML parsing, and the existing .NET build/tests.

## Requirement Traceability

| Requirement / Criterion | Implementation | Test or evidence | Status |
|---|---|---|---|
| REQ-001 / AC-001 | `release.config.mjs`, `tools/ci/*` | `npm run test:release-policy` (8/8) | PASS |
| REQ-002 / AC-002 | `pr-release-policy.yml` | Policy workflow test and YAML parser | PASS |
| REL-001 / AC-003 | `update_semver.yml` | Policy workflow test, YAML parser, diff review | PASS |
| REL-002 / AC-004 | Dockerfiles, `release-tag.yml` | Versioned API build and policy workflow test | PASS |

## Progress and Resume State

- Decisions made: reproduce the SPA's application-version policy, not its
  frontend runtime/deployment details; use PR title validation to preserve
  semantics for squash merges; use Docker version tags without the Git `v`
  prefix, matching the SPA image convention.
- Implemented: centralized release configuration, focused policy tests,
  read-only PR metadata validation, release rerun reporting, and .NET/Docker
  version + SHA propagation.
- TDD: `node --test tools/ci/release-policy.spec.mjs` initially failed because
  `release.config.mjs` did not exist; the final suite passes 8/8.
- Verification passed: all workflow YAML and release-policy JavaScript files
  parsed with Prettier; `git diff --check`; targeted API build with
  `/p:Version=0.0.702 /p:InformationalVersion=0.0.702+testsha`; and 38/38 API
  unit tests.
- Remaining verification: an unauthenticated local semantic-release dry run
  loaded both configured plugins but did not complete in the 30-second command
  window; the authenticated `master` GitHub Actions release and Docker/Testcontainers
  integration gates remain CI-only.

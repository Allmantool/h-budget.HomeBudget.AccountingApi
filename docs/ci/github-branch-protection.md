# Accounting Pull Request Merge Protection

## Policy

`master` is the protected branch. A pull request targeting it may merge only after
these required GitHub Actions contexts succeed on the current mergeable commit:

| Required context | Workflow/job | Purpose |
|---|---|---|
| `CI Master / PR Gate` | CI Master / PR Gate | Aggregates mandatory build, test, integration, and quality results. |
| `CodeQL / Analyze (csharp)` | CodeQL / Analyze (csharp) | C# security analysis. |

The individual CI job names are diagnostic only; do not add them as separately
required GitHub contexts. The aggregate gate fails if any mandatory input is
`failure`, `cancelled`, `skipped`, or absent.

Mandatory CI inputs are:

1. Validate Application Build: restore and deterministic Release solution build,
   including the repository's existing analyzer and code-style enforcement.
2. Run Unit And Component Tests: API, Categories, and Operations test projects.
3. Run API Integration Tests: Docker/Testcontainers-backed API integration suite.
4. Analyze Master Quality: existing coverage validation and optional Sonar publication.

Sonar publication is informational when `SONAR_TOKEN` is unavailable; the quality
job's local build and coverage work remains mandatory. `Publish CI Summary`, Renovate,
release, tag, and deployment workflows are not merge gates.

## Current Audit Result (2026-09-05)

The authenticated GitHub API showed:

- default branch: `master`;
- no branch protection (`404 Branch not protected`);
- no repository rulesets (`[]`);
- all merge methods allowed;
- repository workflow default token permissions set to write.

Consequently, pull request #823 was reported by GitHub as `MERGEABLE` while its
`Validate Application Build` and `Analyze (csharp)` checks had failed. A normal
Squash-and-merge action therefore remained visible. This was governance failure, not
the expected behavior of a required check.

The build failure was a real NuGet downgrade (`NU1605`) and CodeQL v2 reported a
configuration error. They failed loudly, but GitHub was not configured to require
them.

## Required Repository Ruleset

Create one **active repository branch ruleset** named `Accounting master PR
protection` targeting `refs/heads/master`. Do not create a parallel branch protection
rule; GitHub allows only one branch-protection rule to apply at a time, while a single
ruleset is the authoritative policy here.

The ruleset was created through the authenticated GitHub API during this hardening
work: ID `22327976`, enforcement `active`, no bypass actors, and
`current_user_can_bypass: never`. The workflow change still needs to be pushed and
run once for the new `CI Master / PR Gate` check to be emitted on a pull request.

Configure these rules:

- Require a pull request before merging.
  - at least 1 approval;
  - dismiss stale approvals on push;
  - require approval of the most recent reviewable push by someone else;
  - require conversation resolution.
- Require status checks to pass before merging and require the branch to be up to date.
  - `CI Master / PR Gate` from the GitHub Actions app (ID `15368`);
  - `CodeQL / Analyze (csharp)` from the GitHub Actions app (ID `15368`).
- Block force pushes.
- Block branch deletion.
- Do not add bypass actors. This deliberately applies the policy to administrators.
- Allow the existing GitHub merge methods (`merge`, `squash`, `rebase`) unless the
  repository owner later chooses a narrower merge-history policy.

There is no merge queue today. If one is enabled later, add `merge_group` to both
required workflows before turning the queue on, otherwise their required checks will
not be emitted for merge-group commits.

## Apply and Verify

If the ruleset ever needs to be recreated, use an authenticated `gh` session with
repository administration permission. Prefer first running the workflow change on a
pull request so the new `CI Master / PR Gate` context is visible. Replace
`OWNER/REPOSITORY` with the output of `gh repo view --json nameWithOwner --jq
.nameWithOwner`.

```bash
gh api --method POST repos/OWNER/REPOSITORY/rulesets \
  -H 'X-GitHub-Api-Version: 2026-03-10' \
  --input - <<'JSON'
{
  "name": "Accounting master PR protection",
  "target": "branch",
  "enforcement": "active",
  "bypass_actors": [],
  "conditions": {
    "ref_name": {
      "include": ["refs/heads/master"],
      "exclude": []
    }
  },
  "rules": [
    {
      "type": "pull_request",
      "parameters": {
        "dismiss_stale_reviews_on_push": true,
        "require_code_owner_review": false,
        "require_last_push_approval": true,
        "required_approving_review_count": 1,
        "required_review_thread_resolution": true,
        "allowed_merge_methods": ["merge", "squash", "rebase"]
      }
    },
    {
      "type": "required_status_checks",
      "parameters": {
        "do_not_enforce_on_create": false,
        "strict_required_status_checks_policy": true,
        "required_status_checks": [
          { "context": "CI Master / PR Gate", "integration_id": 15368 },
          { "context": "CodeQL / Analyze (csharp)", "integration_id": 15368 }
        ]
      }
    },
    { "type": "non_fast_forward" },
    { "type": "deletion" }
  ]
}
JSON
```

If the POST returns a validation error because the new CI context has not yet appeared,
run the changed workflow on a pull request first, then retry. Do not substitute a
similarly named context or omit the GitHub Actions integration ID.

Verify the active policy after creation and after any future GitHub settings change:

```bash
gh api repos/OWNER/REPOSITORY/rulesets
gh api repos/OWNER/REPOSITORY/rules/branches/master
gh pr view PR_NUMBER --repo OWNER/REPOSITORY --json mergeable,mergeStateStatus,statusCheckRollup
```

The ruleset response must show `enforcement: active`, no `bypass_actors`, strict
status checks, and exactly the two contexts above. A current commit with a failed,
cancelled, missing, or skipped mandatory CI job must show a failed `PR Gate` and must
not be mergeable through the normal UI. Successful checks from an older commit do not
satisfy strict protection for a newer head/mergeable commit.

## Operational Notes

- The CI and CodeQL workflows have no path filters, so the required checks are
  emitted for all pull requests to `master`; do not add workflow-level path filters.
- CI and CodeQL cancel obsolete runs per pull request. The strict ruleset still
  requires checks on the latest mergeable commit, so a cancelled stale run cannot
  satisfy the policy.
- Test-result artifacts use `if: always()` and remain diagnostic-only; their upload
  result cannot turn a failing test job green.
- `pull_request_target` is not used. PR workflows do not deploy or publish packages.
- Third-party action tags are mutable and repository Actions policy does not require
  SHA pinning. This is a separate supply-chain hardening follow-up, not a reason to
  weaken the merge gate.
- Renovate currently runs on pull requests and inherits the repository's default
  write-capable token setting. Its event/permission model should be reviewed
  separately before accepting untrusted fork contributions.

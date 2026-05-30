# Code Review Checklist

Use this checklist for human PR reviews and Codex-generated changes.

## Correctness

- The change satisfies the stated requirement.
- Edge cases, failure paths, and invalid inputs are handled.
- Public behavior and contracts are preserved unless intentionally changed.
- Idempotency, retries, timeouts, and cancellation behavior are preserved where relevant.

## Simplicity and Design

- The solution is the smallest clear change that solves the problem.
- SOLID and GRASP responsibilities are placed with the right owner.
- KISS and YAGNI are respected; no speculative layers, interfaces, or extension points were added.
- Duplication was removed only when the abstraction is meaningful.
- Law of Demeter is respected; deep object chains do not leak internals.

## Organization and Readability

- Files and classes are cohesive and reasonably small.
- One top-level type per file is preferred.
- File names match the main type names.
- Method size and nesting are reasonable.
- Method parameter count is preferably 4 or fewer and no more than 5 without justification.
- Constructor parameter count is preferably 5 or fewer and no more than 7 without justification.
- Names are domain-specific and clear.
- LINQ and fluent chains remain readable.

## C# and .NET Practices

- Async I/O accepts and forwards `CancellationToken`.
- No `.Result`, `.Wait()`, or sync-over-async.
- Nullable assumptions are explicit and safe.
- Static classes are limited to pure helpers, mappings, extensions, or constants.
- Extension methods do not hide expensive side effects.
- Logging is structured and follows existing patterns.
- Logs do not expose secrets, tokens, credentials, personal data, or sensitive business data.

## Security and Operations

- Authorization, validation, and error-handling patterns are preserved.
- Observability, tracing, and correlation are preserved.
- Configuration changes are safe for local, CI, and deployment environments.
- Resource disposal and connection lifetimes are correct.

## Tests and Quality Gates

- Tests were added or updated for changed behavior when practical.
- Regression tests cover bug fixes.
- Build passes for the affected project set.
- Relevant unit/component tests pass.
- Integration tests were run or explicitly deferred when the change affects cross-process/data behavior.
- Analyzer, formatting, and code-style results were reviewed.

## Review Output

For findings, include:

- File/symbol.
- Problem.
- Why it matters.
- Suggested fix.
- Whether the fix is safe or behavior-changing.
- Severity: Blocking, Should Fix, or Nice to Have.

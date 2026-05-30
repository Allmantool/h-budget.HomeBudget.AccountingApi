# Clean Code Review

## When To Use

Use this skill when reviewing or refactoring C#/.NET code for maintainability, readability, SOLID, GRASP, KISS, YAGNI, Law of Demeter, complexity, file size, method size, parameter count, dependency design, testability, and code-review readiness.

Use it for:

- Code reviews.
- Refactoring plans.
- Reviewing generated code before delivery.
- Checking whether a change is ready for a human pull request.

## Review Priorities

Prioritize findings by severity:

- Blocking: likely correctness, security, data-loss, build, test, deployment, or contract-breaking issue.
- Should Fix: maintainability, reliability, testability, complexity, or architecture issue that should be addressed before merge.
- Nice to Have: small clarity, style, naming, or simplification improvement that is useful but not required.

Lead with findings. Keep summaries short and secondary.

## Finding Format

Each finding should include:

- Severity.
- File/symbol.
- Problem.
- Why it matters.
- Suggested fix.
- Whether the fix is safe or behavior-changing.

Prefer precise file and line references. Avoid vague comments that cannot be acted on.

## Review Checklist

Check for:

- Correct behavior and edge cases.
- Simple design that fits the existing architecture.
- SOLID and GRASP responsibility placement.
- KISS/YAGNI violations and speculative abstractions.
- Law of Demeter violations and deep object chains.
- Harmful duplication without premature abstraction.
- One top-level type per file and file names matching main type names.
- Files/classes/methods that exceed guidance limits without a good reason.
- Method parameter count above 4 preferred or 5 soft maximum.
- Constructor parameter count above 5 preferred or 7 soft maximum.
- Static classes used only for pure helpers, extension methods, mappings, or constants.
- Extension methods that hide expensive side effects.
- Explicit dependencies instead of hidden global state.
- Async I/O accepting and forwarding `CancellationToken`.
- No `.Result`, `.Wait()`, or sync-over-async.
- Structured logging with no secrets or sensitive data.
- Testability and deterministic tests for changed behavior.
- Analyzer, formatter, build, and test results.

## Refactoring Guidance

Preserve behavior unless the user explicitly asks for behavior changes.

Prefer incremental changes:

- Extract private methods before introducing new services.
- Introduce request/options/context/value objects only when they represent a real concept.
- Avoid broad rewrites, mass formatting, or unrelated cleanup.
- Keep public contracts stable unless changing them is part of the task.

Call out any deliberate deviation from repository standards and explain the trade-off.

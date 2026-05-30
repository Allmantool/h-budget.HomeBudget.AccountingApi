# Coding Standards

These standards complement `AGENTS.md`, `.editorconfig`, `Directory.Build.props`, and `StyleCopConfig.ruleset`.

## Priorities

Use this order when trade-offs compete:

1. Correctness.
2. Simplicity.
3. Readability.
4. Maintainability.
5. Testability.
6. Performance only where needed and measured.

## Design

- Apply SOLID and GRASP pragmatically.
- Keep business/domain behavior separate from infrastructure, transport, persistence, and framework wiring where the architecture supports it.
- Prefer explicit dependencies and composition over inheritance.
- Keep abstractions justified by current complexity, coupling, or duplication.
- Avoid broad rewrites and unrelated formatting churn.
- Follow the Law of Demeter by avoiding deep object chains that expose internals.

## Organization

- Prefer one top-level class, record, interface, enum, or exception per file.
- Match the file name to the main type name.
- Use the existing feature/domain folder structure.
- Keep files and classes cohesive, with one clear reason to change.
- Avoid large "god files" and mixed responsibilities.

## Size and Complexity

These are guidance limits, not mechanical rules:

- Target file size: under 150 lines.
- Soft maximum file size: 250 lines.
- Target method size: under 30 lines.
- Soft maximum method size: 50 lines.
- Maximum nesting depth: 3.

If exceeding a limit is reasonable, explain the reason in the PR or final delivery notes.

## Parameters

- Preferred maximum method parameters: 4.
- Soft maximum method parameters: 5.
- Preferred maximum constructor parameters: 5.
- Soft maximum constructor parameters: 7.

When a signature grows beyond these limits, consider a request object, command object, options object, context object, value object, or domain-specific parameter object. Do not create meaningless parameter bags just to satisfy a number.

## Static Classes and Extensions

Use static classes for pure helpers, extension methods, mapping helpers, and constants grouped by domain concept.

Do not use static classes for business workflows with dependencies, I/O, logging, configuration-dependent behavior, or mutable shared state.

Extension methods should improve readability and must not hide expensive side effects.

## C# and .NET

- Respect the repository's current nullable and implicit using baseline.
- Do not introduce null-unsafe code.
- Async methods performing I/O should accept and forward `CancellationToken`.
- Do not use `.Result`, `.Wait()`, or sync-over-async.
- Use structured logging and existing logging patterns.
- Do not log secrets, credentials, tokens, personal data, or sensitive business data.
- Prefer sealed classes when inheritance is not intended.
- Prefer records for immutable data carriers.
- Prefer clear domain names over vague names like `Manager`, `Helper`, `Processor`, or `Util`.
- Avoid large LINQ expressions when they harm readability.

## Tests

- Add or update tests for changed behavior when practical.
- Add regression tests for bug fixes.
- Keep tests deterministic.
- Do not remove, skip, or weaken tests to make a build pass.
- Use integration tests for API-to-worker, Kafka/EventStoreDB/MongoDB/SQL, startup/configuration, or container/test infrastructure changes.

## Validation

Use the smallest meaningful validation set:

- `dotnet restore HomeBudgetAccountingApi.sln`
- `dotnet build HomeBudgetAccountingApi.sln --configuration Release --no-restore --no-incremental`
- The nearest affected `dotnet test` project.
- Integration tests when the change crosses process or data boundaries.

# AGENTS.md

## Project Overview

- This repository is a .NET 10 multi-project solution rooted at `HomeBudgetAccountingApi.sln`.
- Primary runtime hosts:
  - API: `HomeBudget.Accounting.Api`
  - Worker: `HomeBudget.Accounting.Workers.OperationsConsumer`
- Shared code is split across domain, infrastructure, core, notifications, and feature components (`Accounts`, `Categories`, `Contractors`, `Operations`).
- Tests use NUnit, FluentAssertions, Moq, Coverlet, and Testcontainers.
- Node is present only for release automation (`semantic-release`); application build/runtime is .NET-based.
- No checked-in `README`, `CONTRIBUTING`, `docs/`, or static OpenAPI file was found. Treat code, CI, and config as the source of truth.

## Repository Structure

- `HomeBudget.Accounting.Api`: ASP.NET Core API entrypoint, controllers, middleware, Swagger, app wiring.
- `HomeBudget.Accounting.Workers.OperationsConsumer`: background worker host for Kafka/EventStoreDB processing.
- `HomeBudget.Accounting.Infrastructure`: Kafka/EventStoreDB/Mongo/Dapper infrastructure, logging, OpenTelemetry setup, background service helpers.
- `HomeBudget.Accounting.Domain`: shared domain constants, enums, options, factories, builders.
- `HomeBudget.Core`: shared options, constants, observability helpers, base abstractions.
- `HomeBudget.Accounting.Notifications`: SignalR notification wiring.
- `HomeBudget.Components.Accounts`: account handlers, Kafka producer/consumer, Mongo document client.
- `HomeBudget.Components.Categories`: category component logic and Mongo document client.
- `HomeBudget.Components.Contractors`: contractor component logic.
- `HomeBudget.Components.Operations`: payment operations, EventStoreDB write/read, Kafka flows, MediatR tracing behavior.
- `HomeBudget.Accounting.Api.Tests`: API-focused unit tests.
- `HomeBudget.Components.Categories.Tests`: category component tests.
- `HomeBudget.Components.Operations.Tests`: operations component tests.
- `HomeBudget.Accounting.Api.IntegrationTests`: Docker/Testcontainers-based integration tests for API + worker + Kafka + EventStoreDB + MongoDB + SQL Server.
- `HomeBudget.Test.Core`: shared test helper/testcontainer utilities.

## App Entrypoints

- API entrypoint: `HomeBudget.Accounting.Api/Program.cs`
- Worker entrypoint: `HomeBudget.Accounting.Workers.OperationsConsumer/Program.cs`
- API launch settings: `HomeBudget.Accounting.Api/Properties/launchSettings.json`
- Worker launch settings: `HomeBudget.Accounting.Workers.OperationsConsumer/Properties/launchSettings.json`

## How to Run

- Restore solution:
  - `dotnet restore HomeBudgetAccountingApi.sln`
- Build solution:
  - `dotnet build HomeBudgetAccountingApi.sln --configuration Release --no-restore --no-incremental`
- Run API locally:
  - `dotnet run --project HomeBudget.Accounting.Api/HomeBudget.Accounting.Api.csproj`
- Run worker locally:
  - `dotnet run --project HomeBudget.Accounting.Workers.OperationsConsumer/HomeBudget.Accounting.Workers.OperationsConsumer.csproj`
- API local dev URLs are defined in launch settings and include `http://localhost:5307` and `https://localhost:5308`.
- Swagger is exposed by the API launch profile at `/swagger`.
- The worker binds to `http://127.0.0.1:0` and maps `/health` and `/metrics` only when tracing/metrics are enabled.

## How to Test

- Use `.github/workflows/ci-master.yml` as the source of truth.
- API unit tests:
  - `dotnet test HomeBudget.Accounting.Api.Tests/HomeBudget.Accounting.Api.Tests.csproj --configuration Release --no-build --no-restore --settings coverlet.runsettings --collect:"XPlat Code Coverage"`
- Categories component tests:
  - `dotnet test HomeBudget.Components.Categories.Tests/HomeBudget.Components.Categories.Tests.csproj --configuration Release --no-build --no-restore --settings coverlet.runsettings --collect:"XPlat Code Coverage"`
- Operations component tests:
  - `dotnet test HomeBudget.Components.Operations.Tests/HomeBudget.Components.Operations.Tests.csproj --configuration Release --no-build --no-restore --settings coverlet.runsettings --collect:"XPlat Code Coverage"`
- Integration tests:
  - `dotnet test HomeBudget.Accounting.Api.IntegrationTests/HomeBudget.Accounting.Api.IntegrationTests.csproj --configuration Release --no-build --no-restore --settings coverlet.runsettings --collect:"XPlat Code Coverage"`
- Integration tests require Docker/Testcontainers and start Kafka, EventStoreDB, MongoDB, and SQL Server. See `HomeBudget.Accounting.Api.IntegrationTests/TestContainersService.cs`.

## Lint / Typecheck / Format

- No dedicated repo script for `lint`, `typecheck`, or `dotnet format` was found.
- The practical lint gate is `dotnet build`, because analyzers are enabled globally in `Directory.Build.props` and StyleCop/Sonar analyzers are referenced centrally.
- Coverage/test behavior is configured in `coverlet.runsettings`.
- Do not invent a new repo-standard formatting workflow unless the task explicitly requires it.

## Development Workflow

- Start from the solution and the host that owns the change.
- API composition is wired in `HomeBudget.Accounting.Api/Configuration/DependencyRegistrations.cs`.
- Worker composition is wired in `HomeBudget.Accounting.Workers.OperationsConsumer/Configuration/DependencyRegistrations.cs`.
- Infrastructure composition is wired in `HomeBudget.Accounting.Infrastructure/Configuration/DependencyRegistrations.cs`.
- Component-level wiring lives under each component’s `Configuration/DependencyRegistrations.cs`.
- Release automation:
  - CI and tests: `.github/workflows/ci-master.yml`
  - Image/tag release: `.github/workflows/release-tag.yml`, `.github/workflows/update_semver.yml`
  - Sonar helper: `startsonar.sh`

## Coding Conventions

- Target framework is `net10.0`; implicit usings are disabled and nullable is disabled in `Directory.Build.props`.
- Match nearby code instead of introducing a new style.
- Observed conventions:
  - explicit `using` directives
  - block-scoped namespaces are common
  - async methods should end with `Async` per `.editorconfig`
  - central package management via `Directory.Packages.props`
- Tests rely on `InternalsVisibleTo` across multiple projects. Avoid changing assembly visibility casually.

## Safe Change Rules

- Prefer active host/project paths that are in the solution. Ignore residue directories unless the task explicitly mentions them:
  - `HomeBudget.Accounting.OperationsConsumer` appears to be leftover IDE/build residue only.
  - `HomeBudget.Components.Transfers.Tests` contains only `obj/` residue and is not in the solution or CI test list.
- Treat these files carefully:
  - `HomeBudget.Accounting.Api/appsettings.json` contains real-looking environment-specific endpoints and credentials. Do not casually rewrite or expose them.
  - `dockerfile` intentionally removes test projects from the solution before release build.
  - `HomeBudget.Accounting.Infrastructure/Clients/BaseEventStoreWriteClient.cs` and `HomeBudget.Accounting.Infrastructure/Clients/BaseEventStoreStreamReadClient.cs` are marked `[Obsolete]`; avoid broad refactors there unless that is the task.
- If you touch Kafka/EventStoreDB/MongoDB flow, inspect both the component code and the worker/API wiring. Behavior is split across hosts and shared components.
- If you touch DI, startup, middleware, or options binding, check both hosts. Shared components are reused by API and worker.
- If you touch observability/logging, inspect:
  - `HomeBudget.Accounting.Infrastructure/Extensions/OpenTelemetry/OpenTelemetryExtensions.cs`
  - `HomeBudget.Accounting.Infrastructure/Extensions/Logs/LoggerConfigurationExtensions.cs`
  - `HomeBudget.Core/Observability`
- Prefer minimal, targeted validation. Integration tests are expensive and Docker-backed.

## Generated Code / Migrations / Schemas

- No checked-in generated client/schema code was found.
- No committed OpenAPI/Swagger JSON file was found; Swagger is generated at runtime from the API.
- SQL migrations live under `HomeBudget.Accounting.Api.IntegrationTests/db/migrations` and are applied by integration test setup via Evolve in `HomeBudget.Accounting.Api.IntegrationTests/TestContainersService.cs`.
- Coverlet excludes `**/Migrations/*`, `**/Program.cs`, and `**/Startup.cs` from coverage per `coverlet.runsettings`.
- Treat `bin/`, `obj/`, `publish/`, `test-results/`, `node_modules/`, `.dotnet/`, `.vs/`, `.idea/`, and `.git/` as generated/vendor/output areas.

## Validation Checklist Before Finishing

- Build the smallest relevant project set. Use full solution build if you changed shared contracts, DI, or build config.
- Run the nearest affected test project.
- Run integration tests if you changed:
  - API-to-worker behavior
  - Kafka/EventStoreDB/MongoDB/SQL integration
  - startup/configuration paths
  - container/test infrastructure
- If you changed package versions, update them centrally in `Directory.Packages.props`.
- If you changed host behavior, inspect both corresponding `Program.cs` and `Configuration/DependencyRegistrations.cs`.
- If behavior seems ambiguous, prefer CI workflow commands over inferred local shortcuts.

## Codex Scanning Optimization

- Read these first for quick orientation:
  - `HomeBudgetAccountingApi.sln`
  - `Directory.Build.props`
  - `Directory.Packages.props`
  - `.github/workflows/ci-master.yml`
  - the relevant host `Program.cs`
  - the relevant `Configuration/DependencyRegistrations.cs`
- Primary directories for most feature work:
  - `HomeBudget.Accounting.Api`
  - `HomeBudget.Accounting.Workers.OperationsConsumer`
  - `HomeBudget.Accounting.Infrastructure`
  - `HomeBudget.Components.Operations`
  - the adjacent component directory for the feature area
- Secondary directories:
  - `HomeBudget.Core`
  - `HomeBudget.Accounting.Domain`
  - `HomeBudget.Accounting.Notifications`
  - test projects
- Usually irrelevant unless the task explicitly needs them:
  - `bin/`, `obj/`, `publish/`, `test-results/`, `node_modules/`, `.dotnet/`, `.git/`, `.vs/`, `.idea/`
  - `HomeBudget.Accounting.OperationsConsumer`
  - `HomeBudget.Components.Transfers.Tests`
- Avoid re-reading unless necessary:
  - `Directory.Packages.props` after dependency context is known
  - workflow files after command selection is clear
  - large generated output directories
  - the full integration test harness when a task is isolated to a unit-tested component
- Fast root/app/test boundary detection:
  - Root is the directory containing `HomeBudgetAccountingApi.sln`, `Directory.Build.props`, and `.github/workflows`.
  - App boundaries are the two host projects with `Program.cs`.
  - Test boundaries are the four active test projects named in CI.
- Minimal scan sequence for bug fixes:
  - solution file
  - owning host `Program.cs`
  - owning `Configuration/DependencyRegistrations.cs`
  - targeted feature/service/handler/controller file
  - nearest test project and existing tests for that feature
- Minimal scan sequence for feature work:
  - solution file
  - relevant host `Program.cs`
  - component `Configuration/DependencyRegistrations.cs`
  - contracts/models/options used by the feature
  - implementation files
  - nearest unit/component tests
  - integration tests only if the feature crosses process or data boundaries
- Minimal scan sequence for refactors:
  - solution file
  - root build props/targets
  - affected project `.csproj`
  - all dependency registration files touched by the change
  - shared abstractions in `Core`, `Domain`, or `Infrastructure`
  - test projects that consume internals
- Minimal scan sequence for test-only changes:
  - target test project `.csproj`
  - failing/target test files
  - only the directly referenced production files
  - integration harness only if editing integration tests


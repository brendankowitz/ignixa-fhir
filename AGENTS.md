# Ignixa FHIR Server Development Guide

This is the single source of repository guidance for coding agents and contributors.

## Project Overview

Ignixa is a high-performance, multi-tenant FHIR server built on modern .NET. It supports STU3, R4, R4B, and R5 through a clean architecture:

```text
API -> Application -> Domain <- DataLayer
```

- `src/Ignixa.Api`: ASP.NET Core Minimal APIs and HTTP concerns.
- `src/Ignixa.Application`: Medino CQRS handlers and business logic.
- `src/Ignixa.Domain`: Domain models and storage abstractions.
- `src/Ignixa.DataLayer.*`: File system, SQL, and blob implementations.
- `src/Ignixa.*`: Reusable Search, FhirPath, Validation, and Serialization libraries.
- `test/`: xUnit suites mirroring production projects.
- `codegen/`: FHIR structure-provider generators.
- `docs/adr/`: Architecture Decision Records.
- `docs/site/`: Docusaurus documentation source.
- `deploy/`: Azure deployment assets.

The published documentation is at https://brendankowitz.github.io/ignixa-fhir/.

## Build, Test, and Run

- Restore once with `dotnet restore All.sln`.
- Build with `dotnet build All.sln`; warnings are errors.
- Run focused tests with `dotnet test -k "FeatureName"`.
- Run the full suite with `dotnet test All.sln`.
- Start the API with `dotnet run --project src/Ignixa.Api/Ignixa.Api.csproj`.
- Run `./run-compat-tests.ps1` when changing cross-version behavior.
- Regenerate specification providers with `cd codegen && ./generate.ps1` on Windows or `./generate.sh` elsewhere.

Use the smallest existing validation command that covers a change, then escalate when results require it. Do not add new build or test tooling unless the task requires it.

## Architecture Rules

### Layer Boundaries

- API depends on Application; Application depends on Domain; DataLayer implements Domain interfaces.
- Keep business logic out of API endpoints.
- Do not reference `Hl7.Fhir.*` packages from Application or DataLayer; use Ignixa abstractions.
- Use Minimal APIs in `*Endpoints.cs`; do not add MVC controllers.
- Application features use immutable command/query records and separate `IRequestHandler` types.
- Register handlers and services through the established Autofac patterns.
- Every asynchronous API accepts a `CancellationToken` named `cancellationToken`.

### Multi-Tenancy

- Tenant partition `0` is reserved for system operations and must never be exposed through `/tenant/0`.
- In single-tenant mode, both unqualified and `/tenant/{id}` routes are supported.
- In multi-tenant mode, require `/tenant/{id}` routes.
- Validate tenant-aware routing and storage configuration at trust boundaries.

### FHIR Semantics

- Protect `id`, `meta.versionId`, and `meta.lastUpdated` from PATCH operations.
- Prefer FHIRPath for resource navigation and value extraction over direct `MutableNode` or `JsonNode` traversal.
- Use `Select`, `Scalar`, `IsTrue`, and `IsBoolean` from `Ignixa.FhirPath.Evaluation`.
- Research behavior against the supported FHIR versions and preserve version-specific compatibility.

### Resource Merge Transactions

`MergeResources` uses an application-level visibility transaction, not a SQL transaction boundary:

```text
MergeResources -> commit core data -> PostMergeExtensionUpdater
```

- Do not wrap `MergeResources` and `PostMergeExtensionUpdater` in a SQL transaction.
- Do not modify existing `MergeResources` stored procedures or TVP schemas.
- Keep extension columns nullable; failure leaves basic search functional while modifier searches may be incomplete.
- Batch extension updates and use parameterized `ExecuteSqlRawAsync`.
- Log extension-update failures for monitoring without failing the completed core resource write.

## Coding Conventions

- Use 4 spaces, no tabs, file-scoped namespaces, and one type per file.
- Put System usings first and outside namespaces.
- Use `_camelCase` for private/internal fields, static ones included, and PascalCase for constants and static readonly members. There is no `s_` prefix in this codebase.
- Prefer language keywords such as `int` over framework names such as `Int32`.
- Use explicit types unless the right-hand side makes the type obvious.
- Keep nullable reference types enabled and avoid unnecessary casts.
- Prefer primary constructors, collection expressions, target-typed `new`, pattern matching, raw string literals, and `ArgumentNullException.ThrowIfNull`.
- Do not use collection expressions or arrays for static values consumed by EF Core `.Contains()` expressions; use `List<T>` to avoid query interpreter issues.
- Do not use `FrozenSet` or `FrozenDictionary` inside EF Core queries.
- Do not use `#region`.

Write focused, self-documenting methods. Avoid deep nesting, high cyclomatic complexity, and nested loops. Add comments only for non-obvious invariants or business reasons.

## Error Handling and Security

- Never swallow exceptions or add success-shaped fallbacks.
- Handle expected failures at boundaries and let unexpected failures surface through established error handling.
- Fail fast for programmer errors.
- Keep execution deterministic and make degraded states observable.
- Never expose or log secrets. Prefer Managed Identity and untracked local `appsettings.*.json` overrides.

## Testing

- Use xUnit, Shouldly, and NSubstitute.
- Follow Arrange, Act, Assert.
- Name tests `GivenContext_WhenAction_ThenResult`.
- Test behavior through public contracts rather than implementation details.
- Add coverage for handlers, endpoints, edge cases, and version-specific behavior changed by the work.
- Place tests in the matching `test/Ignixa.*.Tests` project.
- Call a test a regression only when it fails at the base commit; an unchanged-behavior pin is a guard.
- Verify regressions at the base commit, normally in a temporary worktree with the new test copied in.
- Mutation-test guards: break the behavior they pin, observe the failure, then revert the mutation.
- Run `git submodule update --init --recursive` before suites that use vendored fixtures; `Ignixa.SqlOnFhir.Tests` requires `sql-on-fhir-tests`.
- `SystemValueElementDeclarationTests.Decisions` is a reflection census: every new production `IElement` producer needs a recorded classification and rationale.
- Keep `MustCompile` tripwires for corpus rows expected to compile, so an un-compilable row cannot pass merely by being skipped.

## Mutation Testing

Use the mutation-coverage skill (published as `mutation-coverage-next` while experimental) if it
is installed; otherwise follow this section by hand. Run `dotnet tool restore` once, then run
Stryker from the test project directory with
`dotnet stryker -p <Target> -r json -r html -r dots --disable-bail`.

| Test project | `-p` target | Notes |
|---|---|---|
| `test/Ignixa.Abstractions.Tests` | `Ignixa.Abstractions.csproj` | |
| `test/Ignixa.Analyzers.Tests` | `Ignixa.Analyzers.csproj` | |
| `test/Ignixa.Api.Tests` | `Ignixa.Api.csproj` | |
| `test/Ignixa.Application.Tests` | `Ignixa.Application.csproj` | large; always scope with `--since`/`-m` |
| `test/Ignixa.Application.Experimental.Tests` | `Ignixa.Application.csproj` | |
| `test/Ignixa.ConformanceMatrix.Cli.Tests` | `Ignixa.ConformanceMatrix.Cli.csproj` | |
| `test/Ignixa.DataLayer.FileSystem.Tests` | `Ignixa.DataLayer.FileSystem.csproj` | |
| `test/Ignixa.DataLayer.SqlServer.Tests` | `Ignixa.DataLayer.SqlServer.csproj` | |
| `test/Ignixa.DeId.Tests` | `Ignixa.DeId.csproj` | |
| `test/Ignixa.Extensions.Tests` | `Ignixa.Extensions.FirelySdk6.csproj` | |
| `test/Ignixa.FhirFakes.Tests` | `Ignixa.FhirFakes.csproj` | |
| `test/Ignixa.FhirFakes.Cli.Tests` | `Ignixa.FhirFakes.Cli.csproj` | |
| `test/Ignixa.FhirMappingLanguage.Tests` | `Ignixa.FhirMappingLanguage.csproj` | |
| `test/Ignixa.FhirPath.Tests` | `Ignixa.FhirPath.csproj` | |
| `test/Ignixa.FhirPath.Phase3.{Stu3,R4B,R5}.Tests` | `Ignixa.FhirPath.csproj` | version conformance corpora; slow |
| `test/Ignixa.Models.Tests` | `Ignixa.Models.R4.csproj` or `Ignixa.Models.R5.csproj` | references both; run once per target |
| `test/Ignixa.Models.R4.Tests` | `Ignixa.Models.R4.csproj` | |
| `test/Ignixa.NarrativeGenerator.Tests` | `Ignixa.NarrativeGenerator.csproj` | |
| `test/Ignixa.PackageManagement.Tests` | `Ignixa.PackageManagement.csproj` | |
| `test/Ignixa.SchemaUpgrade.Cli.Tests` | `Ignixa.SchemaUpgrade.Cli.csproj` | |
| `test/Ignixa.Search.Tests` | `Ignixa.Search.csproj` | |
| `test/Ignixa.Search.Sql.Tests` | `Ignixa.Search.Sql.csproj` | |
| `test/Ignixa.Search.Sql.Generators.Tests` | `Ignixa.Search.Sql.Generators.csproj` | |
| `test/Ignixa.Serialization.Tests` | `Ignixa.Serialization.csproj` | |
| `test/Ignixa.SqlOnFhir.Tests` | `Ignixa.SqlOnFhir.csproj` | needs `sql-on-fhir-tests` submodule |
| `test/Ignixa.SqlOnFhir.Cli.Tests` | `Ignixa.SqlOnFhir.Cli.csproj` | |
| `test/Ignixa.TestScript.Tests` | `Ignixa.TestScript.csproj` | |
| `test/Ignixa.Validation.Tests` | `Ignixa.Validation.csproj` | |
| `test/Ignixa.Validation.Cli.Tests` | `Ignixa.Validation.Cli.csproj` | |
| `test/Ignixa.Api.E2ETests` | excluded | needs a running server |
| `test/Ignixa.DataLayer.SqlServer.IntegrationTests` | excluded | needs SQL Server |
| `test/Ignixa.RepoGuards.Tests` | excluded | repository/architecture guards, not unit tests |
| `test/Ignixa.Tests.Compatibility.CLI.Tests` | excluded | contract fixtures only |

- Setup: `git submodule update --init --recursive` before suites using vendored fixtures.
- Test conventions: xUnit, Shouldly, NSubstitute; Arrange/Act/Assert; `GivenContext_WhenAction_ThenResult`.
- Never remove:
  - `SystemValueElementDeclarationTests.Decisions`: reflection census of `IElement` producers.
  - `MustCompile` corpus tripwires: stop un-compilable rows passing by being skipped.
  - All `Ignixa.RepoGuards.Tests`: architecture and layering guards.
  - Regression tests (names or comments citing an issue or a base-commit failure).
  - Version-specific STU3/R4/R4B/R5 variants and Phase3 conformance rows.
  - Multi-tenancy and security boundary tests (tenant partition 0, routing, sanitized error output).

## Documentation and Decisions

- Read related ADRs before changing architecture or established behavior.
- Update `docs/site/docs/` when a feature changes user-facing behavior or documented configuration.
- Keep specification research, architectural decisions, implementation, and tests traceable.
- Do not create planning documents unless requested or required by the repository workflow.

## Marketplace Agents and Skills

Reusable agents and workflows come from `brendankowitz/agent-marketplace`; do not copy them into this repository.

```powershell
# GitHub Copilot CLI
copilot plugin marketplace add brendankowitz/agent-marketplace
copilot plugin install discover@agent-marketplace
copilot plugin install decide@agent-marketplace
copilot plugin install build@agent-marketplace
copilot plugin install review@agent-marketplace
copilot plugin install document@agent-marketplace
copilot plugin install pr-review-toolkit@agent-marketplace

# Claude Code
claude plugin marketplace add brendankowitz/agent-marketplace
claude plugin install discover@agent-marketplace
claude plugin install decide@agent-marketplace
claude plugin install build@agent-marketplace
claude plugin install review@agent-marketplace
claude plugin install document@agent-marketplace
claude plugin install pr-review-toolkit@claude-plugins-official
```

GitHub Copilot CLI uses `pr-review-toolkit@agent-marketplace`. Claude Code uses the official upstream `pr-review-toolkit@claude-plugins-official`; the port is intentionally omitted from the Claude marketplace to avoid duplicate agents.

The only repository-local agents are:

- `fhir-agent`: FHIR specification research across supported versions.
- `fhir-coordinator`: Coordinates FHIR research with marketplace ADR and implementation agents.

## Git and Pull Requests

- Do not commit without explicit user approval.
- Keep commits focused with short imperative subjects.
- Before requesting a commit, show the diff and repository status.
- PR descriptions should state purpose, scope, risks, and tests run; link relevant issues and ADRs.
- Attach screenshots for user-interface changes.

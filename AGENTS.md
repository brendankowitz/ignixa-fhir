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
- Use `_camelCase` for private/internal fields, `s_camelCase` for static fields, and PascalCase for constants and static readonly members.
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

Use the official `pr-review-toolkit@claude-plugins-official` for both Claude Code and Copilot CLI — `agent-marketplace` does not currently publish a `pr-review-toolkit` plugin, so `copilot plugin install pr-review-toolkit@agent-marketplace` above will fail.

The only repository-local agents are:

- `fhir-agent`: FHIR specification research across supported versions.
- `fhir-coordinator`: Coordinates FHIR research with marketplace ADR and implementation agents.

## Git and Pull Requests

- Do not commit without explicit user approval.
- Keep commits focused with short imperative subjects.
- Before requesting a commit, show the diff and repository status.
- PR descriptions should state purpose, scope, risks, and tests run; link relevant issues and ADRs.
- Attach screenshots for user-interface changes.

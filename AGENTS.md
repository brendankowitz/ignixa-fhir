# Repository Guidelines

## Project Structure & Module Organization
- `src/`: Ignixa.Api (Minimal API), Ignixa.Application (CQRS handlers), Ignixa.Domain (models), Ignixa.DataLayer.* (file system, SQL, blob), plus support libraries (Search, FhirPath, Validation, Serialization).
- `test/`: xUnit suites mirroring the main projects (API, Application, FhirPath, Validation, Serialization).
- `codegen/`: FHIR structure provider generators; run when updating HL7 packages.
- `docs/`: ADRs and investigations; `deploy/`: Azure templates; `.github/`: CI workflows and issue templates.

## Build, Test, and Development Commands
- `dotnet restore All.sln` (first run) then `dotnet build All.sln` (treat warnings as errors).
- `dotnet test All.sln` for full coverage; `dotnet test -k "FeatureName"` for focused runs.
- `dotnet run --project src/Ignixa.Api/Ignixa.Api.csproj` to start the API locally (https://localhost:5001 by default).
- `cd codegen && ./generate.ps1` (or `./generate.sh`) to regenerate specification providers after package updates.
- `./run-compat-tests.ps1` to execute compatibility checks when touching cross-version behavior.

## Coding Style & Naming Conventions
- 4-space indentation, spaces only; file-scoped namespaces preferred.
- Private/internal fields `_camelCase`; static fields `s_camelCase`; const/static readonly members PascalCase.
- Use explicit types unless the right-hand side is obvious; favor language keywords (`int` over `Int32`).
- One type per file; nullable enabled; StyleCop and EditorConfig rules apply during build.

## Testing Guidelines
- Frameworks: xUnit + FluentAssertions + NSubstitute; follow AAA layout.
- Name tests `GivenContext_WhenAction_ThenResult`; place new cases in the matching `test/Ignixa.*.Tests` project.
- Add coverage for new handlers/endpoints and edge cases; run `dotnet test All.sln` before raising a PR.

## Commit & Pull Request Guidelines
- Commit messages: short, imperative subjects (~72 chars), e.g., `Add patient search paging`; keep commits focused.
- PRs: include purpose, scope of change, risks, and tests run; link issues/ADRs; attach screenshots for UI-facing tweaks.

## Security & Configuration Tips
- Do not commit secrets; prefer Managed Identity and local `appsettings.*.json` overrides.
- Respect tenant rules: avoid exposing `/tenant/0` and validate tenant-aware routes and storage configuration.

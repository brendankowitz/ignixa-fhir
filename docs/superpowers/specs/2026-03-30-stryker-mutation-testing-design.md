# Stryker Mutation Testing Integration

**Date:** 2026-03-30
**Status:** Approved

## Goal

Integrate [Stryker.NET](https://stryker-mutator.io/docs/stryker-net/introduction/) mutation testing into the development workflow to surface gaps in test suite effectiveness across Core and Application projects. Mutation scores appear as non-blocking quality signals on every PR and are available on demand locally.

## Scope

### Projects in Scope (Initial Set)

| Test Project | Mutates Source |
|---|---|
| `test/Ignixa.FhirPath.Tests` | `src/Core/Ignixa.FhirPath` |
| `test/Ignixa.Validation.Tests` | `src/Core/Ignixa.Validation` |
| `test/Ignixa.Serialization.Tests` | `src/Core/Ignixa.Serialization` |
| `test/Ignixa.FhirMappingLanguage.Tests` | `src/Core/Ignixa.FhirMappingLanguage` |
| `test/Ignixa.Anonymizer.Tests` | `src/Core/Ignixa.Anonymizer` |
| `test/Ignixa.SqlOnFhir.Tests` | `src/Core/Ignixa.SqlOnFhir` |
| `test/Ignixa.Application.Tests` | `src/Application/Ignixa.Application` |
| `test/Ignixa.Api.Tests` | `src/Application/Ignixa.Api` |

E2E tests (`Ignixa.Api.E2ETests`) are excluded — mutation testing against infrastructure-dependent tests is not cost-effective.

Additional projects (e.g., `Ignixa.NarrativeGenerator`, `Ignixa.PackageManagement`) can be added once the initial set is stable.

## Config Structure

Each test project listed above gets a `stryker-config.json` in its directory. The config scopes Stryker to only mutate the corresponding source project.

Example for `test/Ignixa.FhirPath.Tests/stryker-config.json`:

```json
{
  "stryker-config": {
    "project": "Ignixa.FhirPath.csproj",
    "reporters": ["html", "json", "progress"],
    "report-file-name": "mutation-report",
    "thresholds": {
      "high": 80,
      "low": 60,
      "break": 0
    },
    "mutation-level": "Standard"
  }
}
```

Key decisions:
- `break: 0` — Stryker never exits non-zero based on score. The CI job is always green; score is informational.
- `thresholds.high/low` — Used only for colorized HTML report output, not for CI gating.
- `mutation-level: Standard` — Balances thoroughness with run time. `Advanced` can be enabled per-project later if warranted.
- `reporters: ["html", "json", "progress"]` — HTML for human review, JSON for the summary aggregation step in CI.

## Tool Manifest

A `.NET local tool manifest` at `.config/dotnet-tools.json` pins the `dotnet-stryker` version. This ensures developers and CI use identical versions without a global install.

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "dotnet-stryker": {
      "version": "4.x",
      "commands": ["dotnet-stryker"]
    }
  }
}
```

Developers run `dotnet tool restore` once after cloning or pulling. No separate install step required.

## CI Integration

A new workflow `.github/workflows/mutation-tests.yml` runs on every PR targeting `main`.

### Trigger

```yaml
on:
  pull_request:
    branches: [ main ]
    paths:
      - 'src/Core/**'
      - 'src/Application/**'
      - 'test/**'
      - '.github/workflows/mutation-tests.yml'
```

Path filtering ensures the workflow only triggers when source or test code changes — doc-only PRs skip it.

### Structure

- **Matrix job** (`mutation-test`): One job per test project (8 parallel jobs). Each job:
  1. Checks out the repo with submodules
  2. Sets up .NET 9
  3. Runs `dotnet tool restore`
  4. Runs `dotnet stryker` from the test project directory
  5. Uploads the JSON report as a workflow artifact named `stryker-{project-name}`

- **Summary job** (`mutation-summary`): Runs after all matrix jobs complete (`needs: [mutation-test]`, `if: always()`). Downloads all JSON artifacts, parses the `mutation-report.json` files using a small inline `node` script (available on ubuntu-latest runners), and posts a Markdown table to `$GITHUB_STEP_SUMMARY` showing per-project mutation scores. This table is visible in the PR checks UI.

- No job calls `exit 1` based on mutation score. The workflow result is always success unless Stryker itself crashes (e.g., compilation error in the test project).

### Job naming

Matrix jobs are named `Mutation Tests (FhirPath)`, `Mutation Tests (Validation)`, etc. so the PR checks panel clearly identifies each module.

## Local Developer Workflow

Run mutation tests for a specific project:

```bash
cd test/Ignixa.FhirPath.Tests
dotnet stryker
```

Stryker opens the HTML report in the default browser after the run. No additional scripts or tasks are needed.

To target a subset of mutants during development (faster feedback):

```bash
dotnet stryker --since:main
```

This uses Stryker's diff-based mutation to only test mutants in files changed since `main`.

## What Mutation Testing Does Not Replace

- **Code coverage** (coverlet) measures which lines are executed. Mutation testing measures whether tests *detect* changes in behavior. Both are useful; they answer different questions.
- **E2E tests** validate integration correctness. Mutation testing targets unit-testable logic only.
- A high mutation score does not mean the tests are correct — it means the tests are sensitive to code changes. Good test names and assertions are still required.

## Adding More Projects

To add a new project to mutation testing:

1. Add a `stryker-config.json` to the test project directory with `"project"` pointing to the source `.csproj`
2. Add the test project to the matrix in `.github/workflows/mutation-tests.yml`

No other changes required.

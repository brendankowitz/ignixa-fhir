---
name: mutation-coverage
description: Use Stryker.NET mutation testing to curate an Ignixa test suite to "just enough" coverage - add tests for surviving mutants, document equivalent mutants, and remove tests that kill nothing or only duplicate other tests' kills. Use when asked to harden tests for code you just wrote, find missing test branches, prune redundant or do-nothing tests, or run Stryker / mutation testing on a project.
---

# Mutation Coverage

Mutation testing changes production code one small edit at a time (a "mutant") and runs the
tests. A mutant the tests catch is **killed**; one they miss **survived**. Survivors point at
missing assertions or branches. Tests that kill nothing, or whose kills another test already
covers, add runtime without adding protection.

The goal is **just enough** tests: every behaviorally meaningful mutant killed, with as few
overlapping tests as possible. Mutation score is a means, not the target.

## Prerequisites

```powershell
dotnet tool restore            # installs the pinned dotnet-stryker from .config/dotnet-tools.json
```

Stryker runs from a **test project directory** that has a `stryker-config.json`. Each config
names the single production project it mutates:

| Test project | Mutates |
|---|---|
| `test/Ignixa.Api.Tests` | `Ignixa.Api` |
| `test/Ignixa.Application.Tests` | `Ignixa.Application` |
| `test/Ignixa.DeId.Tests` | `Ignixa.DeId` |
| `test/Ignixa.FhirMappingLanguage.Tests` | `Ignixa.FhirMappingLanguage` |
| `test/Ignixa.FhirPath.Tests` | `Ignixa.FhirPath` |
| `test/Ignixa.Serialization.Tests` | `Ignixa.Serialization` |
| `test/Ignixa.SqlOnFhir.Tests` | `Ignixa.SqlOnFhir` (run `git submodule update --init --recursive` first) |
| `test/Ignixa.Validation.Tests` | `Ignixa.Validation` |

For another pair, copy one of these configs and change `project` to the production `.csproj`
file name the test project references. Do not run Stryker against E2E or SQL integration tests.

## Workflow

### 1. Choose the smallest scope

Full-project runs are slow (FhirPath takes roughly 40 minutes). Always scope unless the user
asked for a whole-project cleanup.

| Situation | Scope flags |
|---|---|
| Code you just changed on this branch | `--since:main` |
| Specific files | `-m "**/Path/File.cs"` (repeatable) |
| Specific lines | `-m "**/File.cs{start..end}"` (character span) |
| Whole-project curation | no filter |

### 2. Run Stryker with the full kill matrix

```powershell
cd test/Ignixa.<Name>.Tests
dotnet stryker --disable-bail <scope flags> -O ../../StrykerOutput/<label>-before
```

`--disable-bail` is required: without it Stryker records only the first test that kills each
mutant, which makes the redundancy analysis meaningless. `StrykerOutput/` is git-ignored.

### 3. Summarize the report

```powershell
pwsh .github/skills/mutation-coverage/scripts/Get-MutationSummary.ps1 `
    -ReportPath StrykerOutput/<label>-before/reports/mutation-report.json
```

The summary lists, per file, each **Survived** and **NoCoverage** mutant with its source line,
then **tests that killed no mutants**, then **tests whose kills are all covered by another
test**. It flags partial scopes and runs that look like they were made without `--disable-bail`.

### 4. Close the gaps

For each survivor or uncovered mutant, decide which it is:

- **Missing behavior check**: add or strengthen a test in the matching `test/Ignixa.*.Tests`
  project that would fail with the mutation. Prefer extending an existing test's
  `[Theory]` data or assertions over adding a new test. Follow repository test conventions
  (xUnit, Shouldly, NSubstitute, `GivenContext_WhenAction_ThenResult`, Arrange/Act/Assert),
  and assert on public behavior, not on the mutated line.
- **Equivalent mutant**: the mutation cannot change observable behavior (for example a
  boundary that is unreachable, a log message string, or a defensive branch already guaranteed
  by the type system). Do not write a test for it. Record it with an inline Stryker comment that
  states why, so later runs skip it:

  ```csharp
  // Stryker disable once Equality : roundTo is clamped to [0, 28] by the caller
  ```

- **Dead code**: if no caller can reach the branch, tell the user; removing production code is
  outside this skill unless they ask.

Never add a test whose only purpose is to execute a line without asserting a result.

### 5. Prune redundant tests

Removal candidates come from the summary's last two sections. Before removing anything:

- **Only trust whole-scope results.** A test that kills nothing in a `-m` or `--since` run may
  kill mutants elsewhere. Confirm with a run whose scope includes everything the test exercises,
  normally the whole project.
- **Check what the test is for.** Stryker mutates one production project. A test that targets a
  different project, a test helper, a fixture's integrity, or serialization round-trips against
  vendored data will show zero kills and may still be essential.
- **Theories count as one test.** Stryker reports a `[Theory]` as a single test. Redundancy
  between its data rows is invisible; prune rows only by reasoning about the inputs.
- **Prefer merging over deleting.** When two tests overlap, fold the unique assertions into one
  test (or into theory data) and remove the other.

Never remove these, regardless of kill data:

- Regression tests (named or commented as a regression, or linked to an issue).
- FHIR-version-specific tests (STU3, R4, R4B, R5 variants of the same behavior).
- `MustCompile` corpus tripwires.
- `SystemValueElementDeclarationTests.Decisions` and other reflection census tests.
- Security, tenancy, and trust-boundary tests (tenant `0` isolation, PATCH protection of
  `id`, `meta.versionId`, `meta.lastUpdated`).
- Tests in `Ignixa.RepoGuards.Tests`.

Unless the user explicitly asked for a cleanup pass, list removal candidates with your reasoning
and let them decide instead of deleting.

### 6. Prove nothing was lost

After pruning, re-run the **same scope** and compare against the before report:

```powershell
dotnet stryker --disable-bail <same scope flags> -O ../../StrykerOutput/<label>-after
pwsh .github/skills/mutation-coverage/scripts/Get-MutationSummary.ps1 `
    -ReportPath StrykerOutput/<label>-after/reports/mutation-report.json `
    -BaselinePath StrykerOutput/<label>-before/reports/mutation-report.json
```

The script exits with code 1 and lists every mutant the baseline killed that is no longer
killed. Restore or rewrite tests until that list is empty.

The comparison matches mutants by file, line, column, and mutation, so **production source must
be identical between the two runs**. Do the prune-and-verify pass with test-only changes, and
add Stryker disable comments or production edits in a separate pass with its own baseline.

### 7. Finish

- Run `dotnet test` for the touched test project; it must pass.
- Report to the user: scope, before/after mutation score, tests added, tests removed or merged
  (each with the covering test), equivalent mutants recorded, and anything left unresolved.
- Do not commit without the user's approval.

## Troubleshooting

- **Many `CompileError` mutants**: expected for some code shapes; Stryker removes them safely.
  They do not affect the score.
- **`Timeout` mutants**: usually mutated loop conditions; they count as killed.
- **Static initializers**: with the default `perTest` coverage, mutants in static constructors
  run against all tests. If kill data there looks wrong, add
  `"coverage-analysis": "perTestInIsolation"` for that run.
- **Slow runs**: narrow `-m`, or pass `-c <n>` to raise concurrency.

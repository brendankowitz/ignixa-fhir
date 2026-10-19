---
name: mutation-coverage
description: Use Stryker.NET mutation testing to curate a .NET test suite to "just enough" coverage - add tests for surviving mutants, document equivalent mutants, and remove tests that kill nothing or only duplicate other tests' kills. Use when asked to harden tests for code you just wrote, find missing test branches, prune redundant or do-nothing tests, or run Stryker / mutation testing on a .NET project.
---

# Mutation Coverage

Mutation testing changes production code one small edit at a time (a "mutant") and runs the
tests. A mutant the tests catch is **killed**; one they miss **survived**. Survivors point at
missing assertions or branches. Tests that kill nothing, or whose kills another test already
covers, add runtime without adding protection.

The goal is **just enough** tests: every behaviorally meaningful mutant killed, with as few
overlapping tests as possible. Mutation score is a means, not the target.

Paths below like `scripts/...` are relative to this skill's directory.

## 0. Load or create the repository profile

Repository-specific facts live in the repository's agent instructions, not in this skill. Look
for a `## Mutation Testing` section in `AGENTS.md` (or, if the repository has no `AGENTS.md`, in
`CLAUDE.md` or `.github/copilot-instructions.md`). If it exists, follow it and skip to step 1.

If it does not exist, create it before mutation testing:

1. **Find targets.** Run `pwsh scripts/Get-MutationTargets.ps1 -Root <repo root>`. It lists each
   test project with a suggested production project (`Target`) and all `Candidates`. Resolve
   empty or doubtful targets by reading the test code; mark projects that are E2E, integration,
   container, or network dependent as excluded.
2. **Find conventions.** Read existing agent instructions, `CONTRIBUTING.md`, and a few test
   files to learn the test framework, assertion and mocking libraries, naming and structure
   conventions, and setup steps (submodules, fixtures, services).
3. **Find protected tests.** Search for tests that must survive regardless of kill data:
   regression tests (names or comments citing issues/bugs), version- or platform-specific
   variants, tripwire or census tests (reflection scans, corpus "must compile" checks),
   architecture/guard tests, and security or multi-tenant boundary tests. Record them by name or
   pattern.
4. **Ensure the tool.** If `.config/dotnet-tools.json` does not pin `dotnet-stryker`, ask before
   running `dotnet new tool-manifest` (if needed) and `dotnet tool install dotnet-stryker`. Add
   `StrykerOutput/` to `.gitignore`.
5. **Write the section** using the template below, show it to the user, and continue once they
   accept it. Keep it short; it is read on every run.

```markdown
## Mutation Testing

Use the `mutation-coverage` skill. Run Stryker from the test project directory with
`dotnet stryker -p <Target> -r json -r html -r dots --disable-bail`.

| Test project | `-p` target | Notes |
|---|---|---|
| `tests/Foo.Tests` | `Foo.csproj` | |
| `tests/Foo.E2E.Tests` | excluded | needs a running server |

- Setup: <steps such as `git submodule update --init --recursive`, or "none">.
- Test conventions: <framework, assertion/mocking libraries, naming pattern>.
- Never remove: <protected tests by name or pattern, with one-line reasons>.
```

When later runs teach something durable (a new exclusion, a slow project, a protected test),
update this section rather than this skill.

## 1. Choose the smallest scope

Full-project runs can take tens of minutes. Always scope unless the user asked for a
whole-project cleanup.

| Situation | Scope flags |
|---|---|
| Code changed on this branch | `--since:<base branch>` |
| Specific files | `-m "**/Path/File.cs"` (repeatable) |
| Specific spans | `-m "**/File.cs{start..end}"` (character offsets) |
| Whole-project curation | no filter |

## 2. Run Stryker with the full kill matrix

```powershell
cd <test project directory>
dotnet stryker -p <Target> -r json -r html -r dots --disable-bail <scope flags> -O <repo root>/StrykerOutput/<label>-before
```

- `-p` is the production project **file name** (not a path) from the profile.
- `-r json` is required; the summary script reads `reports/mutation-report.json`.
- `--disable-bail` is required: without it Stryker records only the first test that kills each
  mutant, which makes redundancy analysis meaningless.

## 3. Summarize the report

```powershell
pwsh scripts/Get-MutationSummary.ps1 -ReportPath <output>/reports/mutation-report.json
```

The summary lists, per file, each **Survived** and **NoCoverage** mutant with its source line,
then **tests that killed no mutants**, then **tests whose kills are all covered by another
test**. It flags partial scopes and runs that look like they were made without `--disable-bail`.

## 4. Close the gaps

For each survivor or uncovered mutant, decide which it is:

- **Missing behavior check**: add or strengthen a test that would fail with the mutation.
  Prefer extending an existing test's data rows or assertions over adding a new test. Follow
  the profile's test conventions and assert on public behavior, not on the mutated line.
- **Equivalent mutant**: the mutation cannot change observable behavior (an unreachable
  boundary, a log or exception message string, a branch already guaranteed by types). Do not
  write a test for it. Record why with a Stryker comment so later runs skip it:

  ```csharp
  // Stryker disable once Equality : value is clamped to [0, 28] by the only caller
  ```

- **Dead code**: if no caller can reach the branch, tell the user; removing production code is
  outside this skill unless they ask.

Never add a test whose only purpose is to execute a line without asserting a result.

## 5. Prune redundant tests

Removal candidates come from the summary's last two sections. Before removing anything:

- **Only trust whole-scope results.** A test that kills nothing in a `-m` or `--since` run may
  kill mutants elsewhere. Confirm with a run whose scope covers everything the test exercises,
  normally the whole target project.
- **Check what the test is for.** Stryker mutates one production project. A test aimed at a
  different project, a test helper, fixture integrity, or round-trips against vendored data
  will show zero kills and may still be essential.
- **Parameterized tests count as one.** Stryker reports an xUnit `[Theory]` (or NUnit
  `[TestCase]` set) as a single test, so redundancy between data rows is invisible; prune rows
  only by reasoning about the inputs.
- **Prefer merging over deleting.** Fold a redundant test's unique assertions into the test that
  covers it, then remove it.
- **Respect the profile's never-remove list**, and by default never remove regression,
  version-specific, tripwire/census, architecture-guard, or security/tenancy tests.

Unless the user explicitly asked for a cleanup pass, list removal candidates with your reasoning
and let them decide instead of deleting.

## 6. Prove nothing was lost

After pruning, re-run the **same scope** and compare against the before report:

```powershell
dotnet stryker -p <Target> -r json -r html -r dots --disable-bail <same scope flags> -O <repo root>/StrykerOutput/<label>-after
pwsh scripts/Get-MutationSummary.ps1 `
    -ReportPath <repo root>/StrykerOutput/<label>-after/reports/mutation-report.json `
    -BaselinePath <repo root>/StrykerOutput/<label>-before/reports/mutation-report.json
```

The script exits with code 1 and lists every mutant the baseline killed that is no longer
killed. Restore or rewrite tests until that list is empty.

The comparison matches mutants by file, line, column, and mutation, so **production source must
be identical between the two runs**. Do the prune-and-verify pass with test-only changes, and
make Stryker disable comments or production edits in a separate pass with its own baseline.

## 7. Finish

- Run the touched test project's tests; they must pass.
- Report to the user: scope, before/after mutation score, tests added, tests removed or merged
  (each with the covering test), equivalent mutants recorded, and anything unresolved.
- Do not commit unless the user asked you to.

## Troubleshooting

- **Many `CompileError` mutants**: expected for some code shapes; Stryker removes them safely
  and they do not affect the score.
- **`Timeout` mutants**: usually mutated loop conditions; they count as killed. Timeout counts
  can vary slightly between runs.
- **Static initializers**: with the default `perTest` coverage, mutants in static constructors
  run against all tests. If kill data there looks wrong, re-run with a config file setting
  `"coverage-analysis": "perTestInIsolation"`.
- **Microsoft Testing Platform / xUnit v3**: pass `--test-runner mtp`; `--test-case-filter` is
  not supported by those runners.
- **Slow runs**: narrow `-m`, or raise concurrency with `-c <n>`.

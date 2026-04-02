# Stryker Mutation Testing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrate Stryker.NET 4.14.0 mutation testing across Core and Application projects, with per-project configs, a non-blocking CI quality gate on every PR, and a local `dotnet stryker` dev workflow.

**Architecture:** One `stryker-config.json` per test project, scoped to its corresponding source project. A `.config/dotnet-tools.json` pins the tool version repo-wide. A new GitHub Actions workflow (`mutation-tests.yml`) runs 8 parallel matrix jobs on every PR targeting `main`, uploads HTML/JSON reports as artifacts, and posts a mutation score table to the PR step summary.

**Tech Stack:** Stryker.NET 4.14.0, xUnit, .NET 9, GitHub Actions (ubuntu-latest), Node.js (for summary script — pre-installed on ubuntu-latest runners)

---

## File Map

| File | Action |
|---|---|
| `.config/dotnet-tools.json` | Create — pins `dotnet-stryker@4.14.0` as a local tool |
| `test/Ignixa.FhirPath.Tests/stryker-config.json` | Create — scoped to `Ignixa.FhirPath.csproj` |
| `test/Ignixa.Validation.Tests/stryker-config.json` | Create — scoped to `Ignixa.Validation.csproj` |
| `test/Ignixa.Serialization.Tests/stryker-config.json` | Create — scoped to `Ignixa.Serialization.csproj` |
| `test/Ignixa.FhirMappingLanguage.Tests/stryker-config.json` | Create — scoped to `Ignixa.FhirMappingLanguage.csproj` |
| `test/Ignixa.Anonymizer.Tests/stryker-config.json` | Create — scoped to `Ignixa.Anonymizer.csproj` |
| `test/Ignixa.SqlOnFhir.Tests/stryker-config.json` | Create — scoped to `Ignixa.SqlOnFhir.csproj` |
| `test/Ignixa.Application.Tests/stryker-config.json` | Create — scoped to `Ignixa.Application.csproj` |
| `test/Ignixa.Api.Tests/stryker-config.json` | Create — scoped to `Ignixa.Api.csproj` |
| `.github/workflows/mutation-tests.yml` | Create — parallel matrix CI job + summary |

---

## Task 1: Add dotnet-stryker to the local tool manifest

**Files:**
- Create: `.config/dotnet-tools.json`

- [ ] **Step 1: Create the tool manifest**

Create `.config/dotnet-tools.json` at the repo root:

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "dotnet-stryker": {
      "version": "4.14.0",
      "commands": [
        "dotnet-stryker"
      ]
    }
  }
}
```

- [ ] **Step 2: Restore tools to verify the manifest is valid**

Run from the repo root:
```bash
dotnet tool restore
```

Expected output:
```
Tool 'dotnet-stryker' (version '4.14.0') was restored. Available commands: dotnet-stryker
Restore was successful.
```

If it fails with "version not found", run `dotnet tool search dotnet-stryker` and use the latest non-prerelease version shown.

- [ ] **Step 3: Commit**

```bash
git add .config/dotnet-tools.json
git commit -m "tooling: add dotnet-stryker 4.14.0 as local tool"
```

---

## Task 2: Pilot — add stryker-config.json for FhirPath.Tests and run locally

**Files:**
- Create: `test/Ignixa.FhirPath.Tests/stryker-config.json`

- [ ] **Step 1: Create the stryker-config.json**

Create `test/Ignixa.FhirPath.Tests/stryker-config.json`:

```json
{
  "stryker-config": {
    "project": "Ignixa.FhirPath.csproj",
    "reporters": ["html", "json", "progress"],
    "thresholds": {
      "high": 80,
      "low": 60,
      "break": 0
    },
    "mutation-level": "Standard"
  }
}
```

**Why this config:**
- `project`: Tells Stryker which of the test project's references to mutate. FhirPath.Tests also references Ignixa.Serialization and Ignixa.Specification — this scopes it to FhirPath only.
- `break: 0`: Stryker exits 0 regardless of mutation score. CI job stays green.
- `mutation-level: Standard`: Applies the most commonly useful mutators without going into edge-case mutations that generate noise.
- `reporters`: `html` for local review, `json` for CI summary script, `progress` for live terminal output.

- [ ] **Step 2: Run Stryker locally to verify it works**

```bash
cd test/Ignixa.FhirPath.Tests
dotnet stryker
```

This will take several minutes (expect 5–20 minutes depending on machine). Stryker will:
1. Build the solution
2. Generate mutants in `Ignixa.FhirPath`
3. Run all FhirPath.Tests for each mutant
4. Write reports to `test/Ignixa.FhirPath.Tests/StrykerOutput/reports/`

Expected: Stryker finishes without error. The final line shows a mutation score, e.g.:
```
All mutants have been tested.
 75 mutants have been tested.
   55 mutants killed (Killed)
   10 mutants survived (Survived)
    5 mutants timed out (Timeout)
    5 mutants ignored (Ignored)
```

- [ ] **Step 3: Verify the HTML report opens**

```bash
# On Windows:
start StrykerOutput/reports/mutation-report.html

# On Linux/Mac:
open StrykerOutput/reports/mutation-report.html
```

Expected: Browser opens showing the Stryker mutation report with file-by-file breakdown.

- [ ] **Step 4: Verify the JSON report exists**

```bash
ls StrykerOutput/reports/
```

Expected output includes: `mutation-report.json` and `mutation-report.html`

- [ ] **Step 5: Add StrykerOutput to .gitignore**

Check if StrykerOutput is already in `.gitignore`:

```bash
cd ../..
grep -r "StrykerOutput" .gitignore
```

If not present, add it:

```bash
echo "StrykerOutput/" >> .gitignore
```

- [ ] **Step 6: Commit**

```bash
git add test/Ignixa.FhirPath.Tests/stryker-config.json .gitignore
git commit -m "test: add Stryker config for Ignixa.FhirPath (pilot)"
```

---

## Task 3: Add stryker-config.json for remaining Core test projects

**Files:**
- Create: `test/Ignixa.Validation.Tests/stryker-config.json`
- Create: `test/Ignixa.Serialization.Tests/stryker-config.json`
- Create: `test/Ignixa.FhirMappingLanguage.Tests/stryker-config.json`
- Create: `test/Ignixa.Anonymizer.Tests/stryker-config.json`
- Create: `test/Ignixa.SqlOnFhir.Tests/stryker-config.json`

- [ ] **Step 1: Create stryker-config.json for Ignixa.Validation.Tests**

Create `test/Ignixa.Validation.Tests/stryker-config.json`:

```json
{
  "stryker-config": {
    "project": "Ignixa.Validation.csproj",
    "reporters": ["html", "json", "progress"],
    "thresholds": {
      "high": 80,
      "low": 60,
      "break": 0
    },
    "mutation-level": "Standard"
  }
}
```

- [ ] **Step 2: Create stryker-config.json for Ignixa.Serialization.Tests**

Create `test/Ignixa.Serialization.Tests/stryker-config.json`:

```json
{
  "stryker-config": {
    "project": "Ignixa.Serialization.csproj",
    "reporters": ["html", "json", "progress"],
    "thresholds": {
      "high": 80,
      "low": 60,
      "break": 0
    },
    "mutation-level": "Standard"
  }
}
```

- [ ] **Step 3: Create stryker-config.json for Ignixa.FhirMappingLanguage.Tests**

Create `test/Ignixa.FhirMappingLanguage.Tests/stryker-config.json`:

```json
{
  "stryker-config": {
    "project": "Ignixa.FhirMappingLanguage.csproj",
    "reporters": ["html", "json", "progress"],
    "thresholds": {
      "high": 80,
      "low": 60,
      "break": 0
    },
    "mutation-level": "Standard"
  }
}
```

- [ ] **Step 4: Create stryker-config.json for Ignixa.Anonymizer.Tests**

Create `test/Ignixa.Anonymizer.Tests/stryker-config.json`:

```json
{
  "stryker-config": {
    "project": "Ignixa.Anonymizer.csproj",
    "reporters": ["html", "json", "progress"],
    "thresholds": {
      "high": 80,
      "low": 60,
      "break": 0
    },
    "mutation-level": "Standard"
  }
}
```

- [ ] **Step 5: Create stryker-config.json for Ignixa.SqlOnFhir.Tests**

Create `test/Ignixa.SqlOnFhir.Tests/stryker-config.json`:

```json
{
  "stryker-config": {
    "project": "Ignixa.SqlOnFhir.csproj",
    "reporters": ["html", "json", "progress"],
    "thresholds": {
      "high": 80,
      "low": 60,
      "break": 0
    },
    "mutation-level": "Standard"
  }
}
```

- [ ] **Step 6: Spot-check one config by running locally (optional — skip if CI time is preferred)**

```bash
cd test/Ignixa.Validation.Tests
dotnet stryker
```

Expected: Stryker runs and reports a mutation score without error.

- [ ] **Step 7: Commit**

```bash
cd ../..
git add test/Ignixa.Validation.Tests/stryker-config.json \
        test/Ignixa.Serialization.Tests/stryker-config.json \
        test/Ignixa.FhirMappingLanguage.Tests/stryker-config.json \
        test/Ignixa.Anonymizer.Tests/stryker-config.json \
        test/Ignixa.SqlOnFhir.Tests/stryker-config.json
git commit -m "test: add Stryker configs for Core test projects"
```

---

## Task 4: Add stryker-config.json for Application test projects

**Files:**
- Create: `test/Ignixa.Application.Tests/stryker-config.json`
- Create: `test/Ignixa.Api.Tests/stryker-config.json`

**Note:** `Ignixa.Application.Tests` references multiple source projects (`Ignixa.Application`, `Ignixa.Application.Operations`, `Ignixa.Search`, etc.). The `project` field in the config targets only `Ignixa.Application.csproj`. To add coverage of `Ignixa.Application.Operations` later, add a second config and matrix job.

- [ ] **Step 1: Create stryker-config.json for Ignixa.Application.Tests**

Create `test/Ignixa.Application.Tests/stryker-config.json`:

```json
{
  "stryker-config": {
    "project": "Ignixa.Application.csproj",
    "reporters": ["html", "json", "progress"],
    "thresholds": {
      "high": 80,
      "low": 60,
      "break": 0
    },
    "mutation-level": "Standard"
  }
}
```

- [ ] **Step 2: Create stryker-config.json for Ignixa.Api.Tests**

Create `test/Ignixa.Api.Tests/stryker-config.json`:

```json
{
  "stryker-config": {
    "project": "Ignixa.Api.csproj",
    "reporters": ["html", "json", "progress"],
    "thresholds": {
      "high": 80,
      "low": 60,
      "break": 0
    },
    "mutation-level": "Standard"
  }
}
```

- [ ] **Step 3: Commit**

```bash
git add test/Ignixa.Application.Tests/stryker-config.json \
        test/Ignixa.Api.Tests/stryker-config.json
git commit -m "test: add Stryker configs for Application test projects"
```

---

## Task 5: Create the GitHub Actions mutation-tests.yml workflow

**Files:**
- Create: `.github/workflows/mutation-tests.yml`

- [ ] **Step 1: Create the workflow file**

Create `.github/workflows/mutation-tests.yml`:

```yaml
name: Mutation Tests

on:
  pull_request:
    branches: [ main ]
    paths:
      - 'src/Core/**'
      - 'src/Application/**'
      - 'test/**'
      - '.github/workflows/mutation-tests.yml'

concurrency:
  group: mutation-${{ github.event.pull_request.number }}
  cancel-in-progress: true

jobs:
  mutation-test:
    name: Mutation Tests (${{ matrix.project-name }})
    runs-on: ubuntu-latest
    timeout-minutes: 60

    strategy:
      fail-fast: false
      matrix:
        include:
          - test-project: test/Ignixa.FhirPath.Tests
            project-name: FhirPath
          - test-project: test/Ignixa.Validation.Tests
            project-name: Validation
          - test-project: test/Ignixa.Serialization.Tests
            project-name: Serialization
          - test-project: test/Ignixa.FhirMappingLanguage.Tests
            project-name: FhirMappingLanguage
          - test-project: test/Ignixa.Anonymizer.Tests
            project-name: Anonymizer
          - test-project: test/Ignixa.SqlOnFhir.Tests
            project-name: SqlOnFhir
          - test-project: test/Ignixa.Application.Tests
            project-name: Application
          - test-project: test/Ignixa.Api.Tests
            project-name: Api

    steps:
      - name: Checkout code
        uses: actions/checkout@v6
        with:
          fetch-depth: 0
          submodules: recursive

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Restore tools
        run: dotnet tool restore

      - name: Restore packages
        run: dotnet restore ${{ matrix.test-project }}

      - name: Run Stryker
        working-directory: ${{ matrix.test-project }}
        run: dotnet stryker

      - name: Upload mutation report
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: stryker-${{ matrix.project-name }}
          path: ${{ matrix.test-project }}/StrykerOutput/reports/
          retention-days: 30
          if-no-files-found: warn

  mutation-summary:
    name: Mutation Score Summary
    runs-on: ubuntu-latest
    needs: [ mutation-test ]
    if: always()

    steps:
      - name: Download all mutation reports
        uses: actions/download-artifact@v4
        with:
          pattern: stryker-*
          path: stryker-reports
          merge-multiple: false

      - name: Post mutation score summary
        run: |
          node - <<'EOF'
          const fs = require('fs');
          const path = require('path');

          const reportsDir = 'stryker-reports';
          const rows = [];

          if (!fs.existsSync(reportsDir)) {
            fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, '## Mutation Testing Results\n\nNo reports found.\n');
            process.exit(0);
          }

          const dirs = fs.readdirSync(reportsDir).sort();
          for (const dir of dirs) {
            const reportPath = path.join(reportsDir, dir, 'mutation-report.json');
            if (!fs.existsSync(reportPath)) {
              rows.push(`| ${dir.replace('stryker-', '')} | — | — | — | — | ⚠️ No report |`);
              continue;
            }

            let report;
            try {
              report = JSON.parse(fs.readFileSync(reportPath, 'utf8'));
            } catch (e) {
              rows.push(`| ${dir.replace('stryker-', '')} | — | — | — | — | ❌ Parse error |`);
              continue;
            }

            let killed = 0, survived = 0, timeout = 0, noCoverage = 0, ignored = 0;
            for (const file of Object.values(report.files || {})) {
              for (const mutant of file.mutants || []) {
                switch (mutant.status) {
                  case 'Killed': killed++; break;
                  case 'Survived': survived++; break;
                  case 'Timeout': timeout++; break;
                  case 'NoCoverage': noCoverage++; break;
                  case 'Ignored': ignored++; break;
                }
              }
            }

            const detected = killed + timeout;
            const total = killed + survived + timeout + noCoverage;
            const score = total > 0 ? Math.round(detected / total * 100) : 0;
            const emoji = score >= 80 ? '🟢' : score >= 60 ? '🟡' : '🔴';
            const projectName = dir.replace('stryker-', '');

            rows.push(`| ${projectName} | ${killed} | ${survived} | ${noCoverage} | ${total} | ${emoji} **${score}%** |`);
          }

          let summary = '## Mutation Testing Results\n\n';
          summary += '> Scores are informational — no threshold enforced yet.\n\n';
          summary += '| Project | Killed | Survived | No Coverage | Total Mutants | Score |\n';
          summary += '|---|---|---|---|---|---|\n';
          summary += rows.join('\n') + '\n\n';
          summary += '🟢 ≥ 80%  🟡 ≥ 60%  🔴 < 60%\n';

          fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, summary);
          console.log('Summary written to step summary.');
          EOF
```

**What this workflow does:**
- Triggers on PRs to `main` when `src/Core/**`, `src/Application/**`, or `test/**` files change
- `fail-fast: false`: All 8 matrix jobs run even if one fails (so you get all scores, not just the first failure)
- `timeout-minutes: 60`: Kills a job if Stryker hangs (can happen with infinite-loop mutants)
- Each job: checks out, restores tools, restores packages, runs Stryker, uploads `StrykerOutput/reports/` as an artifact
- Summary job: runs after all matrix jobs (`if: always()` ensures it runs even if some jobs fail), downloads all JSON reports, posts a score table to the GitHub step summary
- Score formula: `(killed + timeout) / (killed + survived + timeout + noCoverage) * 100`

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/mutation-tests.yml
git commit -m "ci: add mutation testing workflow with per-project matrix and PR summary"
```

---

## Task 6: Push, create a PR, and verify CI behavior

- [ ] **Step 1: Push the branch**

```bash
git push -u origin dev/stryker
```

- [ ] **Step 2: Create a PR targeting main**

```bash
gh pr create --title "feat: integrate Stryker.NET mutation testing" \
  --body "Adds per-project Stryker configs, local tool manifest, and CI mutation testing workflow. Scores are non-blocking (warn only)." \
  --base main
```

- [ ] **Step 3: Watch the Actions UI**

Go to the PR on GitHub. The `Mutation Tests` workflow will appear alongside `PR Build`. Click into it:
- You should see 8 parallel jobs: `Mutation Tests (FhirPath)`, `Mutation Tests (Validation)`, etc.
- Each job should complete within its 60-minute timeout
- After all jobs finish, the `Mutation Score Summary` job runs and posts a table to the step summary

- [ ] **Step 4: Check the step summary**

In the `Mutation Score Summary` job, click "Summary" in the left sidebar. You should see a table like:

```
## Mutation Testing Results

| Project | Killed | Survived | No Coverage | Total Mutants | Score |
|---|---|---|---|---|---|
| Api | 45 | 12 | 8 | 65 | 🟡 69% |
| Application | 102 | 18 | 5 | 125 | 🟢 82% |
| Anonymizer | 34 | 6 | 2 | 42 | 🟢 86% |
...
```

- [ ] **Step 5: Download and inspect HTML reports**

In any matrix job, click "Artifacts" to download the `stryker-{ProjectName}` artifact. Open `mutation-report.html` in a browser to see the file-by-file survived mutant breakdown — this is what tells you *where* the test gaps are.

- [ ] **Step 6: Interpret results and walk through with the team**

Key things to look for in the HTML report:
- **Survived mutants** on arithmetic operators (`+`→`-`, `*`→`/`) in business logic — these mean tests don't assert the computed value
- **Survived mutants** on boolean conditions — tests reach the code but don't verify both branches
- **No Coverage mutants** — code that no test executes at all

If any project has a notably low score (< 50%), flag it and open a separate task to improve test coverage for that area.

---

## Troubleshooting

**Stryker fails with "No project found" error**

The `project` field in `stryker-config.json` must match the filename (not full path) of exactly one `<ProjectReference>` in the test `.csproj`. Run:
```bash
grep ProjectReference test/Ignixa.FhirPath.Tests/Ignixa.FhirPath.Tests.csproj
```
and confirm the filename matches `"project"` in the config.

**Stryker times out on FhirPath.Tests in CI**

The FhirPath test suite downloads FHIR test cases (≈40MB zip) from GitHub on first build. In CI this should complete in <30 seconds, but if the download fails:
1. Check GitHub Actions logs for the `Restore packages` step — the download is triggered by MSBuild during restore/build
2. The marker file `test/Ignixa.FhirPath.Tests/TestData/fhir-test-cases/.downloaded` prevents re-download; it's not committed to git so CI always downloads fresh

**Summary job shows "No report" for a project**

The matrix job likely failed before Stryker completed. Check that job's logs. Common causes:
- Compilation error in the source project
- Test project references a project that Stryker can't build (e.g., missing global tool)
- The `project` field in `stryker-config.json` doesn't match any project reference

**All jobs show 0% mutation score**

Stryker ran but found no mutants. This usually means the `project` field matched a wrong project (e.g., a test utility project instead of the source). Double-check the `project` field against the csproj references.

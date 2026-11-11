# GitHub Workflows

GitHub Actions supplies build and test evidence. Native Copilot reviews provide advisory analysis; maintainers adjudicate findings and decide whether to merge. Instructions are not a merge gate or a guarantee that every defect will be found.

## Copilot Review and Merge Procedure

[AGENTS.md](../AGENTS.md) is the authoritative development guide. [REVIEW.md](../REVIEW.md) defines review focus, and [FHIR security guidelines](instructions/fhir-security.instructions.md) add concrete boundary checks for matching paths. The security file's `excludeAgent: "cloud-agent"` scopes it away from GitHub's cloud agent; this is not a universal control over every local client.

### Repository settings

As verified on 2026-11-06, the active Main ruleset requests Copilot reviews for PRs targeting the default branch and for new pushes, with draft review off. It requires these GitHub Actions checks:

- `Build and Unit Tests (11.0.x)`
- `Validate Docker Build`
- `E2E Tests (SQL Server)`

These are a dated settings snapshot, not configuration managed by this document. Inspect the live ruleset before relying on it. SQL integration and documentation jobs also run in PR Build but were not required checks in that snapshot.

Recommended settings are repository instructions enabled, Balanced review effort routinely, and Max requested for security-sensitive or complex changes. Select effort in GitHub; instruction text cannot select models or guarantee named specialist passes. Keep counted Copilot approvals off for this initial rollout. Effort and approval settings are separate from automatic-review rules and must be checked separately.

### Renovate automerge

[Renovate configuration](../renovate.json) enables PR automerge for selected dependency updates. If enabled and GitHub's applicable protections are satisfied, those PRs may merge without the manual procedure below; a requested Copilot review or non-required check may not finish first. This change does not alter automerge settings. Inspect the live settings and decide separately whether human review needs an enforced gate.

### Before merging manually

1. Inspect the individual applicable CI jobs and evidence for the current revision, including SQL integration and documentation results. Missing, unexpectedly skipped, cancelled, or failed work is not a pass. A green rollup comment is not a substitute for these results.
2. Read Copilot's review of the latest changes. If no review arrived, it failed, or it predates the latest push, request/re-request it from the PR's Reviewers section. If Copilot is unavailable, perform and record a manual review against the same guidelines rather than silently treating absence as approval.
3. Confirm the PR description states intent and acceptance criteria per the [pull request template](pull_request_template.md), with evidence for each criterion; a criterion marked "none" is an open gap, and a manual check is limited evidence. Verify material findings against the code and tests. Fix real defects and rerun affected checks; record a concrete reason when dismissing a false positive. Resolving a thread alone is not evidence that a defect was fixed.
4. Personally inspect changes to authorization, tenancy, query expansion/continuation, sensitive data, schema upgrades, and CI/review/test controls, including shared helpers. Copilot reads instructions from the PR head, so a PR editing `AGENTS.md`, `REVIEW.md`, or `.github/instructions/**` is reviewed under its own changes; compare those files against the target branch yourself. Do not let a small diff or reassuring AI review replace that judgment.
5. Confirm required checks and any repository approval requirements are satisfied, then merge manually. If the base changed materially, update/retest and reassess the affected paths; the snapshot above does not require the branch to be up to date.

Automatic review requests do not themselves block merging until a review completes. The latest-review and finding-adjudication steps above are maintainer responsibilities, not an implemented policy check.

### Improve the guidance incrementally

After the first 10 reviewed PRs, inspect material misses, useful findings, false positives, and review cost. This is a tuning checkpoint, not a safety certification. For a confirmed miss, add the smallest effective regression test or guard and update guidance only when the lesson generalizes. Use public references and synthetic examples; do not publish confidential incident details or patient data. Review policy changes explicitly rather than learning automatically from dismissed comments.

## Reading CI Evidence

The current workflows have limits that reviewers must account for:

- PR Build runs for every changed path on PRs targeting `main` or `fhir-v2`, including documentation and instructions. This intentionally runs full CI for documentation-only changes so required checks are not left pending by path filtering.
- `PR Validation Complete` currently checks that no prerequisite reported `failure`, rather than proving every prerequisite succeeded. Inspect the five underlying jobs until the rollup is hardened.
- The FHIR TestScript conformance report can record behavioral failures while its harness test passes. Read per-case outcomes; green E2E is not proof of complete FHIR conformance.
- The PR schema DeployReport targets an empty database. It proves report generation, not safe upgrade of existing data.
- Missing reports are not clean results. Read what each job actually asserted and which tests/configurations ran.

## Overview

### Workflow Architecture

```
.github/
├── actions/
│   └── dotnet-build-and-test/
│       └── action.yml              ← Reusable composite action
├── instructions/
│   └── fhir-security.instructions.md
├── workflows/
│   ├── pr-build.yml                ← Pull request workflow
│   ├── ci.yml                       ← Main branch workflow
│   └── ...                          ← Docs, release, and other workflows
└── WORKFLOWS.md                     ← This file
```

## Reusable Composite Action

**File**: `.github/actions/dotnet-build-and-test/action.yml`

Encapsulates common build, test, and reporting steps used by multiple workflows.

### Inputs

- `dotnet-version` (default: `11.0.x`) - declared SDK input; the current setup step installs .NET 10 and 11 explicitly alongside `global.json`
- `solution-file` (default: `All.sln`) - Path to solution file
- `build-configuration` (default: `Release`) - Build configuration
- `test-filter` (default: empty) - test selection passed by the caller

### Steps

1. Setup .NET SDK
2. Restore NuGet dependencies
3. Build solution
4. Run unit tests with xUnit
5. Upload test results artifacts
6. Publish test results summary

### Usage

```yaml
- name: Build and test solution
  uses: ./.github/actions/dotnet-build-and-test
  with:
    dotnet-version: '11.0.x'
    solution-file: 'All.sln'
    build-configuration: 'Release'
```

## PR Workflow

**File**: `.github/workflows/pr-build.yml`

Triggered on pull requests targeting `main` or `fhir-v2` branches.

### Triggers

- Pull request created/updated
- All changed paths, including documentation, instructions, and build configuration (no workflow-level path filter)

### Features

- ✅ Builds solution in Release mode
- ✅ Runs unit tests, with SQL integration and SQL E2E in separate jobs
- ✅ Verifies package creation, Docker builds, and the documentation site
- ✅ Uploads test artifacts (30-day retention)
- ✅ Posts success/failure comment on PR
- ✅ Cancels in-progress runs for same PR

### Concurrency

```yaml
group: pr-build-${{ github.event.pull_request.number }}
cancel-in-progress: true
```

Each PR has its own build queue; new commits cancel previous builds.

## CI Workflow

**File**: `.github/workflows/ci.yml`

Triggered on pushes to `main` and manual dispatch.

### Triggers

- Push to `main` (no path filter)
- Manual dispatch, with options to skip Docker publication or NuGet packaging

### Jobs

#### 1. Build, Test, and Package
- Builds solution in Release mode
- Runs unit tests, SQL integration tests, and SQL E2E tests in separate jobs
- Produces package and test artifacts; separate jobs publish the container and deploy documentation
- Generates conformance reports; report generation is not equivalent to all cases passing

#### 2. Code Quality (optional)
- Static analysis with warnings-as-errors
- Code formatting check with `dotnet format`
- Does not block workflow on failure

#### 3. Notifications
- Success notification
- Failure notification with error status

### Concurrency

```yaml
group: ci-${{ github.ref }}
cancel-in-progress: true
```

Each branch has its own CI queue; new pushes cancel previous CI runs.

## SDK Configuration

The build jobs use this matrix:

```yaml
strategy:
  matrix:
    dotnet-version: [ '11.0.x' ]
```

The composite action installs .NET 10 and 11 explicitly (multi-targeted test projects need both runtimes). Changing its input or the job matrix alone does not change those installations. `global.json` pins SDK 11.0.x. Test target frameworks come from the project files; a job name is not proof that every supported FHIR version or storage provider ran.

## Test Results

### Artifact Upload
- **Location**: Actions > Workflow Run > Artifacts
- **Name**: `test-results-{os}` (e.g., `test-results-Linux`)
- **Format**: TRX (Test Results XML)
- **Retention**: 30 days

### Test Results Summary
- Posted inline in workflow summary
- Available via `EnricoMi/publish-unit-test-result-action@v2`
- Includes pass/fail counts and duration

## Skipping Workflows

Commit directives such as `[skip ci]` or `[ci skip]` can prevent PR checks from running. Do not use them to bypass merge evidence; required checks may remain pending. PR Build has no path filter, but commit skip directives still apply.

## Modifying Workflows

### Adding a shared build/test step

Edit `.github/actions/dotnet-build-and-test/action.yml`:

```yaml
- name: My custom step
  run: |
    echo "This runs in workflows that use the composite action"
  shell: bash
```

### Adding a workflow-specific step

Edit `.github/workflows/pr-build.yml` or `.github/workflows/ci.yml`:

```yaml
- name: Build and test solution
  uses: ./.github/actions/dotnet-build-and-test
  # ...

- name: My PR-specific step
  run: |
    echo "This only runs for PRs"
  shell: bash
```

## Troubleshooting

### Workflow Not Triggering

1. Check branch protection rules (may require specific status checks)
2. Verify the PR targets `main` or `fhir-v2`; PR Build runs regardless of changed paths
3. Check if commit message includes `[skip ci]`

### Build Failure

1. Click workflow run in GitHub Actions tab
2. Expand failed job to see logs
3. Check test results artifacts for details

### Test Results Not Showing

1. Ensure `EnricoMi/publish-unit-test-result-action` is enabled
2. Check that test runner outputs `.trx` files
3. Verify artifacts are being uploaded

## Performance Tips

### Faster Builds

- Use matrix strategy to test in parallel
- Configure dependency caching explicitly when appropriate; do not assume SDK setup enables it
- Use `--no-restore` and `--no-build` flags where appropriate

### Reduced Workflow Time

- PR workflows cancel on new commits (concurrency)
- CI workflows cancel on new pushes (concurrency)
- Code quality job is non-blocking (continues on error)

## Security

### Permissions

Permissions are job-specific. PR build/test jobs use `contents: read` and, where needed, `checks: write`; PR comment jobs request `pull-requests: write` and skip commenting on fork PRs. Publishing workflows need additional permissions. Review permission changes and keep credentials out of untrusted PR execution.

### Secrets

SQL test jobs use disposable-container credentials; publication and optional agent workflows have their own authentication requirements. See [SECRETS.md](SECRETS.md) and the relevant workflow. Never use production credentials or patient data in tests or review examples.

## References

- [Copilot custom instruction support](https://docs.github.com/en/copilot/reference/custom-instructions-support)
- [Customizing Copilot reviews and its limitations](https://docs.github.com/en/copilot/tutorials/customize-code-review)
- [Configuring automatic Copilot reviews](https://docs.github.com/en/copilot/how-tos/copilot-on-github/set-up-copilot/configure-code-review)
- [Required status check semantics](https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/troubleshooting-required-status-checks)
- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [Composite Actions](https://docs.github.com/en/actions/creating-actions/creating-a-composite-action)
- [Workflow Syntax](https://docs.github.com/en/actions/using-workflows/workflow-syntax-for-github-actions)
- [EnricoMi Test Result Publisher](https://github.com/EnricoMi/publish-unit-test-result-action)

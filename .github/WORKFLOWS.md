# GitHub Workflows

This repository uses GitHub Actions for automated CI/CD. The workflows are designed to run on pull requests and main branch pushes.

## Overview

### Workflow Architecture

```
.github/
├── actions/
│   └── dotnet-build-and-test/
│       └── action.yml              ← Reusable composite action
├── workflows/
│   ├── pr-build.yml                ← Pull request workflow
│   └── ci.yml                       ← Main branch workflow
└── WORKFLOWS.md                     ← This file
```

## Reusable Composite Action

**File**: `.github/actions/dotnet-build-and-test/action.yml`

Encapsulates common build, test, and reporting steps used by multiple workflows.

### Inputs

- `dotnet-version` (default: `9.0.x`) - .NET SDK version
- `solution-file` (default: `All.sln`) - Path to solution file
- `build-configuration` (default: `Release`) - Build configuration

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
    dotnet-version: '9.0.x'
    solution-file: 'All.sln'
    build-configuration: 'Release'
```

## PR Workflow

**File**: `.github/workflows/pr-build.yml`

Triggered on pull requests targeting `main` or `fhir-v2` branches.

### Triggers

- Pull request created/updated
- Paths: `src/**`, `test/**`, `bench/**`, `All.sln`, workflow files

### Features

- ✅ Builds solution in Release mode
- ✅ Runs all unit tests
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

Triggered on pushes to main branch (merge commits from PRs).

### Triggers

- Push to `main` or `fhir-v2`
- Paths: `src/**`, `test/**`, `bench/**`, `All.sln`, workflow files

### Jobs

#### 1. Build and Test (required)
- Builds solution in Release mode
- Runs all unit tests
- Uploads test artifacts

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

## Matrix Testing

Both workflows support matrix testing:

```yaml
strategy:
  matrix:
    dotnet-version: [ '9.0.x' ]
```

Extend to test multiple .NET versions:

```yaml
dotnet-version: [ '8.0.x', '9.0.x' ]
```

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

To skip CI workflows for a commit, add `[skip ci]` or `[ci skip]` to commit message:

```bash
git commit -m "Update documentation [skip ci]"
```

## Modifying Workflows

### Adding a new step to all workflows

Edit `.github/actions/dotnet-build-and-test/action.yml`:

```yaml
- name: My custom step
  run: |
    echo "This runs in all workflows"
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
2. Verify path filters match your changes
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
- Cache NuGet packages: `actions/setup-dotnet@v4` does this automatically
- Use `--no-restore` and `--no-build` flags where appropriate

### Reduced Workflow Time

- PR workflows cancel on new commits (concurrency)
- CI workflows cancel on new pushes (concurrency)
- Code quality job is non-blocking (continues on error)

## Security

### Permissions

Workflows use minimal required permissions:
- `contents: read` - Check out code
- `pull-requests: write` - Comment on PRs (PR workflow only)

### Secrets

No secrets currently required. Add secrets to repository settings if needed:

```yaml
env:
  MY_SECRET: ${{ secrets.MY_SECRET }}
```

## References

- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [Composite Actions](https://docs.github.com/en/actions/creating-actions/creating-a-composite-action)
- [Workflow Syntax](https://docs.github.com/en/actions/using-workflows/workflow-syntax-for-github-actions)
- [EnricoMi Test Result Publisher](https://github.com/EnricoMi/publish-unit-test-result-action)

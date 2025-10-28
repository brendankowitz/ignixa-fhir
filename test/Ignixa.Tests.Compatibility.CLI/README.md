# FHIR Compatibility Test CLI

This project provides a command-line tool for running FHIR compatibility tests against the Ignixa server using Microsoft's FHIR Server E2E test suite.

## Why is this project excluded from All.sln?

The `Ignixa.Tests.Compatibility.CLI` project depends on `Microsoft.Health.Fhir.R4.Tests.E2E` package which:
- Is hosted on Microsoft's **public** Azure DevOps feed: `https://microsofthealthoss.pkgs.visualstudio.com/FhirServer/_packaging/CI/nuget/v3/index.json`
- Requires a project-specific `NuGet.Config` with custom PackageSourceMapping
- Causes issues in CI/CD pipelines (GitHub Actions) when included in solution-level restore

To keep CI builds simple and fast, this project is **excluded from `All.sln`** but can be built locally when needed.

## How to Build Locally

### Option 1: Build with project-specific NuGet.Config

```bash
cd test/Ignixa.Tests.Compatibility.CLI
dotnet restore --configfile NuGet.Config
dotnet build
```

### Option 2: Run directly

```bash
cd test/Ignixa.Tests.Compatibility.CLI
dotnet run -- --help
```

### Option 3: Create standalone executable

```bash
cd test/Ignixa.Tests.Compatibility.CLI
dotnet publish -c Release -o ./publish
./publish/fhir-compat --help
```

## Usage

The CLI tool runs Microsoft's FHIR E2E tests against the Ignixa server to verify compatibility.

```bash
# Run all tests
fhir-compat run --server https://localhost:5001

# Run specific test categories
fhir-compat run --server https://localhost:5001 --filter "CreateTests"
fhir-compat run --server https://localhost:5001 --filter "SearchTests"

# Generate HTML report
fhir-compat run --server https://localhost:5001 --output compatibility-report.json
fhir-compat report compatibility-report.json --output report.html
```

## Dependencies

This project depends on:
- `Microsoft.Health.Fhir.R4.Tests.E2E` (from MicrosoftHealthOSS feed)
- `System.CommandLine` (for CLI parsing)
- `xunit.runner.utility` (for running xUnit tests programmatically)

The `NuGet.Config` file in this directory configures:
- `nuget.org` for standard packages
- `MicrosoftHealthOSS` feed for `Microsoft.Health.Fhir.*` packages only

## Troubleshooting

### Error: Unable to find package Microsoft.Health.*

**Solution**: Make sure you're using the project-specific NuGet.Config:

```bash
dotnet restore --configfile NuGet.Config
```

### Error: PackageSourceMapping is blocking restore

**Cause**: Running `dotnet restore` from the repository root without the `--configfile` parameter.

**Solution**: Navigate to the project directory first:
```bash
cd test/Ignixa.Tests.Compatibility.CLI
dotnet restore
```

## Development

To add this project back to the solution temporarily for development:

1. Edit `All.sln` and add the project reference
2. Build the solution
3. **Before committing**: Remove it from the solution again to avoid CI issues

Alternatively, use the PowerShell script in the repository root:

```powershell
.\run-compat-tests.ps1 -Filter "CreateTests"
```

This script handles the build and execution automatically.

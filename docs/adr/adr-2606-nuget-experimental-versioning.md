# ADR 2606: NuGet Package Experimental/Pre-release Versioning

## Status

Accepted

## Context

Ignixa publishes Core SDK packages to NuGet.org for public consumption. Some packages are production-ready (FhirPath, Specification, Serialization), while others are experimental with evolving APIs (FhirMappingLanguage). Users need clear signals about package stability to make informed decisions about adopting dependencies.

**Current State:**
- All packages share the same version (e.g., `1.0.0`), applied uniformly via `-p:PackageVersion` in CI
- No differentiation between stable and experimental packages
- Packages listed explicitly in CI workflow (64+ lines of `dotnet pack` commands)
- Some projects hardcode their own `<Version>` (FhirFakes `0.1.0-preview`, Sidecar.Contracts `1.0.0`, all four CLI tools `0.1.0-preview`), silently overridden by CI
- `NU5104` (stable package depends on pre-release package) is suppressed globally in `Directory.Build.props`, so no enforcement exists today
- No standardized graduation path from experimental to stable

**Problem:**
How do we signal that a package is "experimental" vs "release-ready" when publishing to NuGet.org, without maintaining manual project lists in CI, and with the stable→pre-release dependency rule actually enforced?

## Decision

Use **SemVer 2.0 pre-release identifiers** with the suffix computed **inside MSBuild** (not in CI scripts) so that every evaluation of a project — including when it is evaluated as a `ProjectReference` of another package being packed — agrees on its version.

### 1. Stability Levels

| Level | Version Format | Meaning |
|-------|---------------|---------|
| **stable** | `1.0.0` | Production-ready, stable API |
| **beta** | `1.0.0-beta` | Feature-complete, API stabilizing |
| **alpha** | `1.0.0-alpha` | Experimental, breaking changes expected |

### 2. MSBuild Property Convention

Each packable `.csproj` declares its stability. **The default is `alpha`, not `stable`** — a forgotten property must never ship an experimental package with a production label. Stability is an explicit claim:

```xml
<!-- Stable package: explicit opt-in -->
<PropertyGroup>
  <IsPackable>true</IsPackable>
  <PackageStability>stable</PackageStability>
</PropertyGroup>

<!-- Pre-release package -->
<PropertyGroup>
  <IsPackable>true</IsPackable>
  <PackageStability>beta</PackageStability>
  <PackageReleaseNotes>
⚠️ PRE-RELEASE: Breaking changes may occur between versions.
See https://brendankowitz.github.io/ignixa-fhir/core-sdk/stability
  </PackageReleaseNotes>
</PropertyGroup>
```

The suffix mapping lives in **`Directory.Build.targets`** (new file). It cannot go in `Directory.Build.props`: `.props` is imported *before* the project body, so per-project `PackageStability` values would not be visible yet. `.targets` is imported after.

```xml
<!-- Directory.Build.targets -->
<Project>
  <!-- BasePackageVersion is supplied by CI; local builds keep GitVersion behavior -->
  <PropertyGroup Condition="'$(BasePackageVersion)' != '' AND '$(IsPackable)' == 'true'">
    <PackageStability Condition="'$(PackageStability)' == ''">alpha</PackageStability>
    <PackageVersion Condition="'$(PackageStability)' == 'stable'">$(BasePackageVersion)</PackageVersion>
    <PackageVersion Condition="'$(PackageStability)' != 'stable'">$(BasePackageVersion)-$(PackageStability)</PackageVersion>
  </PropertyGroup>
</Project>
```

CI passes `-p:BasePackageVersion=$VERSION` (a custom property) rather than `-p:PackageVersion`. This is load-bearing: a global property passed on the command line cannot be modified by project files, so `-p:PackageVersion` would make per-project suffixes impossible.

**Why CI-side version computation is broken (the rejected mechanism):** when `dotnet pack` runs on project A, NuGet computes the dependency version for each `ProjectReference` B by evaluating B *with A's global properties*. If CI computes versions per-package and passes `-p:PackageVersion=1.0.0-beta` while packing A, the nuspec records a dependency on `B >= 1.0.0-beta` — a version of B that is never published. Today's uniform versioning only works by accident because every package gets the same version. With MSBuild-side computation, B applies its own stability suffix during A's pack, so dependency ranges come out correct (e.g., `Ignixa.Validation 1.0.0-beta` depends on `Ignixa.FhirPath >= 1.0.0`).

### 3. Auto-Discovery in CI Workflow

`dotnet pack` already skips projects with `IsPackable=false` — that *is* the auto-discovery. No MSBuild property queries are needed. The only remaining CI logic is the public/internal feed split, which follows directory layout:

```bash
# Public packages (NuGet.org)
find src/Core tools src/Application/Ignixa.Sidecar.Contracts -name "*.csproj" -type f | sort | while read -r PROJECT; do
  dotnet pack "$PROJECT" --configuration Release --no-build \
    --output ./core-packages \
    -p:BasePackageVersion="$BASE_VERSION" -p:UpdateVersionProperties=false
done

# Internal packages (GitHub feed)
find src/Application src/DataLayer -name "*.csproj" -type f \
  ! -path "*Ignixa.Sidecar.Contracts*" | sort | while read -r PROJECT; do
  dotnet pack "$PROJECT" --configuration Release --no-build \
    --output ./internal-packages \
    -p:BasePackageVersion="$BASE_VERSION" -p:UpdateVersionProperties=false
done
```

`-p:UpdateVersionProperties=false` is required: GitVersion.MsBuild otherwise overwrites the
computed `PackageVersion` at target-execution time, after `Directory.Build.targets` evaluation
(verified empirically — without it, packages emerge with GitVersion's branch semVer).

**`IsPackable` semantics are opt-out, not opt-in.** SDK-style projects default to `IsPackable=true`. Auto-discovery therefore publishes anything not explicitly opted out — which changes the published set relative to today's explicit lists: `Ignixa.Api.OpenIddict` and `Ignixa.DeId.Cli` are packable but not currently packed. Each must either be explicitly marked `IsPackable=false` or deliberately added to the published set (see checklist).

**Benefits:**
- No manual project list maintenance, no per-project `dotnet msbuild -getProperty` invocations (~60 evaluations per CI run avoided)
- Correct inter-package dependency versions in nuspecs
- Stability controlled per-package via one MSBuild property
- Misconfiguration fails toward `alpha` (under-claiming), never toward `stable`

### 4. Dependency Stability Rule and Enforcement

**Policy: a package's stability must not exceed the stability of any of its package dependencies.** Stable must not depend on beta/alpha; beta must not depend on alpha.

NuGet enforces only the stable→pre-release case, via warning `NU5104` at pack time — and `Directory.Build.props` currently suppresses it globally (`<NoWarn>...NU5104...</NoWarn>`, "allow pre-release dependencies"). **NU5104 must be removed from the global `NoWarn`** or this ADR has no enforcement mechanism. With `TreatWarningsAsErrors=true` already set repo-wide, a stable package referencing a pre-release package then fails the pack. If a specific internal project legitimately needs a pre-release NuGet dependency, scope the suppression to that project.

The beta→alpha case is not enforced by NuGet; it is enforced by the classification table below (every edge was checked against the actual `ProjectReference` graph) and re-checked at review time when dependencies change.

### 5. Package Classification

Classification of **all** packable projects. "Forced" means the dependency rule dictates the value; others are maturity judgments.

**Public packages (NuGet.org):**

| Package | Stability | Rationale |
|---------|-----------|-----------|
| **Ignixa.Abstractions** | stable | Forced: every stable package depends on it |
| **Ignixa.Analyzers** | stable | Analyzer dependency of Serialization (stable); small, frozen API |
| **Ignixa.FhirPath** | stable | Production-ready, extensive test coverage |
| **Ignixa.Specification** | stable | Core FHIR schema provider, stable API |
| **Ignixa.Serialization** | stable | JSON serialization, stable API |
| **Ignixa.Validation** | stable | Production-ready validation engine |
| **Ignixa.Search** | stable | Feature-complete search parameter engine |
| **Ignixa.SqlOnFhir** | stable | SQL-on-FHIR view runner, stable API |
| **Ignixa.SqlOnFhir.Writers** | stable | Depends on SqlOnFhir (stable) |
| **Ignixa.PackageManagement** | stable | Depends only on Abstractions; stable API |
| **Ignixa.Extensions.FirelySdk5** | stable | Interop shim; deps all stable |
| **Ignixa.Extensions.FirelySdk6** | stable | Interop shim; deps all stable |
| **Ignixa.FhirFakes** | stable | Test-data generation; hardcoded `<Version>0.1.0-preview</Version>` must be removed |
| **Ignixa.Sidecar.Contracts** | stable | Contracts package, no project dependencies; hardcoded `<Version>1.0.0</Version>` must be removed |
| **Ignixa.DeId** | beta | API still maturing |
| **Ignixa.NarrativeGenerator** | beta | Newer component, API not yet exercised broadly |
| **Ignixa.FhirMappingLanguage** | beta | Evolving API |
| **Ignixa.TestScript** | beta | Just merged (#255), brand new |
| **Ignixa.TestScript.XUnit** | beta | Capped by TestScript (beta) |
| **Ignixa.TestScript.FhirFakes** | beta | Capped by TestScript (beta); FhirFakes dep is stable |

**CLI tools (match the library they wrap):**

| Tool | Stability | Wraps |
|------|-----------|-------|
| **Ignixa.Validation.Cli** | stable | Validation (stable) |
| **Ignixa.SqlOnFhir.Cli** | stable | SqlOnFhir + Writers (stable) |
| **Ignixa.FhirFakes.Cli** | stable | FhirFakes (stable); also depends on Validation (stable) |
| **Ignixa.DeId.Cli** | beta | DeId (beta) — newly published; was packable but absent from the old CI list |
| **Ignixa.ConformanceMatrix.Cli** | beta | TestScript + TestScript.FhirFakes (beta) — added with #255 |

All four tools hardcode `<Version>0.1.0-preview</Version>`; these must be removed in favor of `PackageStability`.

**Previously unpublished projects:** `Ignixa.Api.OpenIddict` joins the internal feed via auto-discovery (defaults to alpha until classified), and `Ignixa.DeId.Cli` joins the public feed (packable but absent from the old CI list). `tools/FhirPathPerfCheck` (local perf harness) and `Ignixa.FhirPath.Generators` (build-time source generator whose output is compiled into FhirPath) are not products — both get `IsPackable=false`.

The alpha tier is currently empty. It remains the **default** for any packable project that does not declare `PackageStability`, so new packages enter the catalog as alpha until explicitly classified.

**Internal packages (Application/DataLayer):** unmarked projects default to `alpha`. Mark individual packages `beta`/`stable` deliberately if internal consumers need the signal. Note: flipping internal packages from `1.0.0` to `1.0.0-alpha` changes what internal consumers resolve — review pinned references before rollout.

### 6. Graduation Criteria

**alpha → beta:**
- Feature-complete for documented use cases
- Public API frozen (no breaking changes planned)
- Integration tests covering major scenarios
- Used in at least one internal project

**beta → stable:**
- 2+ months without breaking API changes
- Production usage (internal or external)
- Documentation complete (README, samples, API docs)
- No known critical bugs
- All package dependencies are themselves stable (dependency rule, §4)

### 7. Documentation Site

Create `docs/site/docs/core-sdk/stability.md` with the stability matrix. **Generate it in CI from the `PackageStability` properties** rather than maintaining it by hand — a hand-edited matrix is a second source of truth that will rot.

### 8. Rejected Alternatives

**Alternative 1: Separate Package Names**
- `Ignixa.FhirMappingLanguage` (stable) vs `Ignixa.FhirMappingLanguage.Experimental`
- **Rejected**: Requires package name change on graduation (breaking change), confusing to users

**Alternative 2: NuGet Tags Only**
- `<PackageTags>experimental</PackageTags>` without version suffix
- **Rejected**: Not enforced by dependency resolution, easy to miss, no semantic versioning signal

**Alternative 3: Manual CI List (Current Approach)**
- Maintain explicit list of projects in `.github/workflows/ci.yml`
- **Rejected**: 64+ lines to maintain, error-prone when adding new packages

**Alternative 4: GitVersion Branch-Based Tagging**
- Build from `experimental/*` branch → all packages get `-alpha`
- **Rejected**: All-or-nothing approach, can't mix stable and experimental in same build

**Alternative 5: CI-Side Version Computation (this ADR's original proposal)**
- Bash script queries `PackageStability` via `dotnet msbuild -getProperty` per project, computes the suffixed version, passes it as `-p:PackageVersion` per pack invocation
- **Rejected**: Produces wrong `ProjectReference` dependency versions in nuspecs (see §2) — the global `PackageVersion` of the *referencing* project leaks into the recorded version of every *referenced* project. Also: ~60 extra MSBuild evaluations per CI run, and the `|| echo "stable"` failure fallback misclassifies packages in the most-trusted direction when tooling breaks.

## Consequences

### Positive

- **Clear User Signal**: SemVer-compliant pre-release versions signal stability
- **No List Maintenance**: `IsPackable` is the discovery mechanism; CI only encodes the feed split
- **Correct Dependency Graphs**: Suffixes computed in MSBuild keep nuspec dependency versions consistent
- **Enforced Dependency Safety**: With NU5104 un-suppressed and warnings-as-errors, a stable package referencing a pre-release package fails the pack
- **Safe Failure Direction**: Unclassified or misconfigured packages ship as `alpha`, never as `stable`
- **Gradual Rollout**: Packages graduate independently as they stabilize

### Negative

- **New Build Infrastructure**: Introduces `Directory.Build.targets` and a `BasePackageVersion` contract between CI and MSBuild
- **GitVersion Interplay**: `GitVersion.MsBuild` overwrites version properties at target-execution time — verified during implementation; mitigated by passing `-p:UpdateVersionProperties=false` at pack. Note GitVersion's semVer already carries pre-release tags on non-main branches (e.g., `1.0.0-PullRequest261.1`); naive suffix concatenation would be malformed — CI must pass a clean base version (as it does today via `nuget-version`)
- **Version Churn for Internal Consumers**: Internal packages flip from `1.0.0` to `1.0.0-alpha` unless explicitly marked
- **Documentation Overhead**: Stability matrix generation step in CI

### Trade-offs

| Concern | Mitigation |
|---------|------------|
| **Forgotten `PackageStability`** | Defaults to `alpha` — under-claims rather than over-claims |
| **Users install pre-release by accident** | NuGet.org marks pre-release prominently; requires `--prerelease` flag in CLI |
| **Auto-discovery publishes something unintended** | `IsPackable=false` is explicit per project; CI logs all packed projects for verification |
| **Bare `-alpha`/`-beta` suffix collisions** | GitVersion bumps the base version per release; if re-releases within a base version are ever needed, extend to dot-numbered suffixes (`-beta.1`) which NuGet sorts correctly |

## Implementation Checklist

**Phase 1: Infrastructure**
- [x] Create `Directory.Build.targets` with the `PackageStability` → `PackageVersion` mapping (§2)
- [x] Remove `NU5104` from the global `NoWarn` in `Directory.Build.props` (verified: the only pre-release NuGet dependency, `ModelContextProtocol.AspNetCore`, is consumed by internal alpha packages, so no scoped suppression needed)
- [x] Add `<PackageStability>` to every packable project per the §5 tables
- [x] Remove hardcoded `<Version>` from FhirFakes, Sidecar.Contracts, and all five CLI tools
- [x] Set `IsPackable=false` on `tools/FhirPathPerfCheck` and `Ignixa.FhirPath.Generators`
- [x] Guard tests in `Ignixa.RepoGuards.Tests` enforce the dependency-stability rule and explicit classification of public packages

**Phase 2: CI Workflow**
- [x] Replace pack steps with the find-based loops (§3), passing `-p:BasePackageVersion` and `-p:UpdateVersionProperties=false`
- [x] Verify GitVersion.MsBuild does not override the computed `PackageVersion` (it does without `UpdateVersionProperties=false`; verified both ways)
- [x] Diff the produced `.nupkg` set against today's published set — additions are exactly `Ignixa.DeId.Cli` (public) and `Ignixa.Api.OpenIddict` (internal); no removals
- [x] Inspect nuspec dependency versions across beta→beta and beta→stable edges (`TestScript.XUnit → TestScript 1.2.3-beta`; `DeId → FhirPath 1.2.3`)

**Phase 3: Documentation**
- [x] `eng/Generate-StabilityMatrix.ps1` generates `docs/site/docs/core-sdk/stability.md`; docs workflow regenerates it on every build and now also triggers on csproj changes
- [x] Update main README.md with link to stability docs
- [x] Graduation criteria documented (§6)

**Phase 4: Validation**
- [ ] Verify NuGet.org displays pre-release correctly (after first publish)
- [x] Verify NU5104 fires: temporarily promoted `TestScript.XUnit` to stable against beta `TestScript` — pack fails with `error NU5104` (warnings-as-errors)
- [ ] Notify internal consumers of Application/DataLayer version changes (`1.0.0` → `-alpha` suffixes)
- [ ] Mention newly published packages and the versioning policy in the next release notes (repo has no CHANGELOG file; release notes are generated by `publish-release.yml`)

## References

- **SemVer 2.0 Spec**: https://semver.org/#spec-item-9
- **NuGet Pre-release Versions**: https://learn.microsoft.com/en-us/nuget/create-packages/prerelease-packages
- **NU5104**: https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu5104
- **Customize your build (Directory.Build.targets import order)**: https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-by-directory
- **Related**: `docs/features/experimental-library/investigations/library-proposal.md` (runtime feature toggles, orthogonal to package versioning)

# ADR 2606: NuGet Package Experimental/Pre-release Versioning

## Status

Proposed

## Context

Ignixa publishes Core SDK packages to NuGet.org for public consumption. Some packages are production-ready (FhirPath, Specification, Serialization), while others are experimental with evolving APIs (FhirMappingLanguage). Users need clear signals about package stability to make informed decisions about adopting dependencies.

**Current State:**
- All packages share the same version (e.g., `1.0.0`)
- No differentiation between stable and experimental packages
- Packages listed explicitly in CI workflow (64+ lines of `dotnet pack` commands)
- No standardized graduation path from experimental to stable

**Problem:**
How do we signal that a package is "experimental" vs "release-ready" when publishing to NuGet.org, without maintaining manual project lists in CI?

## Decision

Use **SemVer 2.0 pre-release identifiers** with **auto-discovery** to classify package stability:

### 1. Stability Levels

| Level | Version Format | Meaning |
|-------|---------------|---------|
| **stable** | `1.0.0` | Production-ready, stable API |
| **beta** | `1.0.0-beta` | Feature-complete, API stabilizing |
| **alpha** | `1.0.0-alpha` | Experimental, breaking changes expected |

### 2. MSBuild Property Convention

Add `PackageStability` property to each `.csproj`:

```xml
<!-- Stable package (default) -->
<PropertyGroup>
  <IsPackable>true</IsPackable>
  <!-- PackageStability defaults to "stable" if not specified -->
</PropertyGroup>

<!-- Experimental package -->
<PropertyGroup>
  <IsPackable>true</IsPackable>
  <PackageStability>alpha</PackageStability>
  <Description>FHIR Mapping Language parser (EXPERIMENTAL)</Description>
  <PackageReleaseNotes>
⚠️ EXPERIMENTAL: Breaking changes may occur between versions.
See https://brendankowitz.github.io/ignixa-fhir/core-sdk/stability
  </PackageReleaseNotes>
</PropertyGroup>
```

**Default in `Directory.Build.props`:**
```xml
<PropertyGroup>
  <PackageStability Condition="'$(PackageStability)' == ''">stable</PackageStability>
</PropertyGroup>
```

### 3. Auto-Discovery in CI Workflow

Replace explicit project lists with auto-discovery script:

```bash
# Function to compute version with stability suffix
get_package_version() {
  local PROJECT_PATH=$1
  local BASE_VERSION=$2

  # Query PackageStability from project file
  STABILITY=$(dotnet msbuild "$PROJECT_PATH" \
    -getProperty:PackageStability \
    -nologo \
    -p:Configuration=Release 2>/dev/null || echo "stable")

  case "$STABILITY" in
    alpha) echo "${BASE_VERSION}-alpha" ;;
    beta)  echo "${BASE_VERSION}-beta" ;;
    *)     echo "$BASE_VERSION" ;;
  esac
}

# Auto-discover and pack Core packages
find src/Core -name "*.csproj" -type f | while read -r PROJECT; do
  IS_PACKABLE=$(dotnet msbuild "$PROJECT" \
    -getProperty:IsPackable \
    -nologo \
    -p:Configuration=Release 2>/dev/null || echo "false")

  if [ "$IS_PACKABLE" = "true" ]; then
    PKG_VERSION=$(get_package_version "$PROJECT" "$BASE_VERSION")
    echo "Packing: $PROJECT (version: $PKG_VERSION)"

    dotnet pack "$PROJECT" \
      --configuration Release \
      --no-build \
      --output ./core-packages \
      -p:PackageVersion="$PKG_VERSION"
  fi
done
```

**Benefits:**
- No manual project list maintenance
- Add new package → auto-detected if `IsPackable=true`
- Stability controlled per-package via MSBuild property
- Standard SemVer pre-release semantics

### 4. Initial Package Classification

| Package | Stability | Rationale |
|---------|-----------|-----------|
| **Ignixa.FhirPath** | stable | Production-ready, extensive test coverage |
| **Ignixa.Specification** | stable | Core FHIR schema provider, stable API |
| **Ignixa.Serialization** | stable | JSON serialization, stable API |
| **Ignixa.Validation** | beta | Feature-complete, minor API refinements |
| **Ignixa.Search** | beta | Feature-complete, stabilizing |
| **Ignixa.FhirMappingLanguage** | alpha | Experimental, evolving API |
| **Ignixa.SqlOnFhir** | beta | Working implementation, API stabilizing |

### 5. Graduation Criteria

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

### 6. Documentation Site

Create `docs/site/docs/core-sdk/stability.md` with stability matrix (updated per release).

### 7. Rejected Alternatives

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

## Consequences

### Positive

- **Clear User Signal**: SemVer-compliant pre-release versions signal stability
- **No List Maintenance**: Auto-discovery eliminates manual project lists
- **Flexible**: Per-package stability control via MSBuild property
- **Standards-Compliant**: Uses standard NuGet pre-release semantics
- **Dependency Safety**: NuGet prevents stable packages from depending on pre-release by default
- **Gradual Rollout**: Packages graduate independently as they stabilize

### Negative

- **CI Complexity**: Bash script more complex than simple `dotnet pack` list
- **MSBuild Dependency**: Requires MSBuild property query (may fail if tool breaks)
- **Documentation Overhead**: Must maintain stability matrix in docs
- **Version String Length**: Pre-release suffixes make version strings longer (e.g., `1.0.0-alpha` vs `1.0.0`)

### Trade-offs

| Concern | Mitigation |
|---------|------------|
| **Script complexity** | Script is self-contained, well-commented, and tested in CI |
| **MSBuild query failure** | Fallback to `stable` if query fails (safe default) |
| **Users install pre-release by accident** | NuGet.org marks pre-release prominently; requires `--prerelease` flag in CLI |
| **Package discovery errors** | CI logs all discovered packages for verification |

## Implementation Checklist

**Phase 1: Infrastructure**
- [ ] Add `PackageStability` default to `Directory.Build.props`
- [ ] Mark experimental packages with `<PackageStability>alpha</PackageStability>` in `.csproj`
- [ ] Update descriptions/release notes for experimental packages

**Phase 2: CI Workflow**
- [ ] Replace "Pack Core projects" step with auto-discovery script
- [ ] Replace "Pack Application projects" step with auto-discovery script (internal packages)
- [ ] Test on branch to verify correct `.nupkg` versions

**Phase 3: Documentation**
- [ ] Create `docs/site/docs/core-sdk/stability.md` with matrix
- [ ] Update main README.md with link to stability docs
- [ ] Document graduation criteria

**Phase 4: Validation**
- [ ] Verify NuGet.org displays pre-release correctly
- [ ] Test dependency resolution (stable can't depend on alpha)
- [ ] Update CHANGELOG with versioning policy

## References

- **SemVer 2.0 Spec**: https://semver.org/#spec-item-9
- **NuGet Pre-release Versions**: https://learn.microsoft.com/en-us/nuget/create-packages/prerelease-packages
- **MSBuild Property Query**: `dotnet msbuild -getProperty:PropertyName`
- **Related**: `docs/features/experimental-library/investigations/library-proposal.md` (runtime feature toggles, orthogonal to package versioning)

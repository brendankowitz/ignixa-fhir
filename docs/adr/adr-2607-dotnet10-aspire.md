# ADR 2607: .NET 10 Support and ASP.NET Aspire Adoption

## Status

Accepted; TFM policy superseded by [ADR 2611](adr-2611-dotnet11.md)

> Phases 1–2 landed in PR #264. Phases 3–4 remain future work. Phase 5 (.NET 9 deprecation) was completed by [ADR 2611](adr-2611-dotnet11.md), which also supersedes the multi-targeting, Application-layer, Docker, CI and package-version decisions below (sections 1, 2, 4, 5, 7, 8). The text below is retained as the historical record.

## Context

.NET 10 (LTS) ships November 2025. The project currently targets `net9.0` exclusively — a single TFM across all layers with no multi-targeting infrastructure. The Core SDK packages are consumed externally via NuGet and must support users on both .NET 9 and .NET 10. The Application layer (server runtime) has no external consumption constraint and can move to .NET 10 directly.

Separately, ASP.NET Aspire provides orchestration, service discovery, and observability as a first-class .NET hosting model. Adopting Aspire replaces manual Docker Compose / Bicep-managed service wiring for local development and simplifies cloud deployment via the Aspire deployment tooling (azd, Azure Container Apps).

**Current state:**
- All projects: `net9.0` single TFM
- Docker: `mcr.microsoft.com/dotnet/sdk:9.0-azurelinux3.0` (multi-platform via `$BUILDPLATFORM`)
- Deployment: Azure Bicep templates (App Service, SQL Server, Blob Storage)
- No Aspire usage anywhere in the solution
- CI: single SDK version in build matrix (`9.0.x`)

## Decision

### 1. Multi-Targeting Strategy (Core Layer)

Core SDK packages (`src/Core/**`) adopt `net9.0;net10.0` multi-targeting to support consumers on either runtime. Each Core csproj sets `<TargetFrameworks>` directly:

```xml
<!-- Per-project change — no shared Directory.Build.props in src/Core/ -->
<TargetFrameworks>net9.0;net10.0</TargetFrameworks>
```

All packable Core projects are required to carry both TFMs. A RepoGuards test (added in the same PR) enforces this invariant: it fails the build if any Core project with `<IsPackable>true</IsPackable>` targets only a single framework.

- Core packages ship with both TFMs in the nupkg — NuGet resolves the best match for the consumer
- `#if NET10_0_OR_GREATER` conditionals used sparingly for .NET 10–only APIs (e.g., new BCL methods, performance improvements)
- `LangVersion` remains `latest` (C# 14 features available when targeting net10.0)

### 2. Application Layer: .NET 10 Only

Application, DataLayer, and Api projects (`src/Application/**`, `src/DataLayer/**`) upgrade directly to `net10.0`:

```xml
<!-- These projects change from net9.0 → net10.0 -->
<TargetFramework>net10.0</TargetFramework>
```

Rationale: these are not consumed as NuGet packages by external users. The server runtime deploys as a container image — consumers don't link against it. Single TFM keeps the build matrix simple and avoids carrying two runtime configurations in production.

### 3. ASP.NET Aspire Adoption

Introduce an Aspire AppHost project for orchestration:

- **New project:** `src/Application/Ignixa.AppHost/Ignixa.AppHost.csproj` (Aspire 9.x initially, upgrade to Aspire 10 when stable)
- **New project:** `src/Application/Ignixa.ServiceDefaults/Ignixa.ServiceDefaults.csproj` (shared telemetry, health checks, resilience defaults)
- `Ignixa.Web` references `Ignixa.ServiceDefaults` for OpenTelemetry, health endpoints, and service discovery
- AppHost orchestrates: SQL Server (container or connection string), Blob Storage (Azurite or Azure), the FHIR server itself
- **F5 experience preserved:** `dotnet run --project src/Application/Ignixa.AppHost` starts everything — no external Docker Compose or manual infrastructure

**Aspire does NOT replace:**
- Production Bicep/ARM deployments (Aspire manifest complements these)
- The standalone Dockerfile (still needed for non-Aspire container deployments)

### 4. Docker & Container Updates

| Component | Before | After |
|-----------|--------|-------|
| Build SDK | `sdk:9.0-azurelinux3.0` | `sdk:10.0-azurelinux3.0` |
| Runtime | `aspnet:9.0-azurelinux3.0` | `aspnet:10.0-azurelinux3.0` |
| Platform | `$BUILDPLATFORM` (x64 + ARM64) | Unchanged |
| Base OS | Azure Linux 3.0 | Unchanged (or Azure Linux 4.0 if available) |

The Dockerfile remains a single-stage publish targeting `net10.0`. No multi-targeting in the container image.

### 5. CI/CD Pipeline Changes

The team chose a single-SDK / single-matrix-leg approach rather than a two-SDK matrix:

- **Build matrix:** `dotnet-version: [ '10.0.x' ]` — one leg only; the 10.0 SDK compiles both TFMs (`net9.0;net10.0`) in a single build pass
- **`global.json`:** Added with `rollForward: latestFeature` pinned to `10.0.100` to prevent accidental SDK mismatches
- **net9.0 test execution:** Core test projects multi-target `net9.0;net10.0`, so `dotnet test` runs both TFMs in a single invocation. The `actions/setup-dotnet` step installs both `9.0.x` and `10.0.x` (multi-line `dotnet-version`) so the .NET 9 runtime is present for the net9.0 test pass while the 10.0 SDK (pinned by `global.json`) drives the build
- **NuGet pack:** Core packages produce multi-TFM nupkgs; Application/DataLayer packages remain single-TFM

There is no second SDK build leg — adding a `9.0.x` matrix entry is explicitly rejected as unnecessary cost.

### 6. Azure Deployment (Bicep)

Bicep templates in `deploy/azure/` require no runtime-version changes:

- `modules/app-service.bicep` already uses `linuxFxVersion: 'DOCKER|${dockerImageFull}'` — the runtime version is determined by the container image tag, not a hard-coded `DOTNETCORE|x.y` string. The Dockerfile bump to .NET 10 images is sufficient; no Bicep edit is needed
- **Optional (future):** Add Aspire manifest-based deployment via `azd` as an alternative to raw Bicep. The Aspire manifest (`aspire-manifest.json`) can generate equivalent infrastructure — evaluate once Aspire deployment tooling matures for production use

### 7. Package Version Management

Conditional per-TFM version ranges were considered and deliberately rejected. `Directory.Packages.props` uses a single unconditional floor of `10.0.9` for all `Microsoft.Extensions.*`, `Microsoft.EntityFrameworkCore.*`, and `Microsoft.AspNetCore.*` packages. The 10.0.x releases carry net8/netstandard-compatible assets and run correctly on the net9.0 TFM, so no per-TFM split is required. This avoids version-matrix maintenance overhead and keeps the file readable.

Security posture: all initial `10.0.0` pins were bumped to the serviced `10.0.9` release. Two vulnerable transitive packages (`Snappier`, `System.Security.Cryptography.Xml`) are pinned via central transitive pinning. The `NU1903` audit warning is intentionally left unsuppressed so the build surfaces new advisories automatically.

### 8. .NET 9 Deprecation Path

- .NET 9 reaches end-of-support May 2026 (STS)
- Core packages retain `net9.0` TFM for one release cycle after .NET 10 GA to allow consumers time to migrate
- After the grace period: drop `net9.0` from Core, remove multi-targeting, simplify to `net10.0` only
- CI matrix requires no change (already single-leg `10.0.x`); the `net9.0` conditional package groups in `Directory.Packages.props` are removed at the same time

## Consequences

**Positive:**
- Core SDK consumers can adopt .NET 10 incrementally without forced upgrades
- Application layer benefits from .NET 10 performance improvements (AOT, GC, networking) immediately
- Aspire provides a unified F5 experience — SQL, storage, and the server start together
- Aspire telemetry (OpenTelemetry, structured logging, distributed tracing) replaces ad-hoc configuration
- ARM64 container support maintained throughout
- Single CI matrix leg keeps pipeline cost flat — no doubled build time from a second SDK leg

**Negative:**
- Multi-targeting in Core increases per-project build time (both TFMs compiled and packed)
- Aspire AppHost adds 2 new projects to the solution
- Aspire has its own release cadence — version management overhead
- Teams unfamiliar with Aspire face a learning curve for orchestration model
- `Directory.Packages.props` imposes a 10.0.x dependency floor on net9.0 consumers of Core packages — accepted simplification; avoided by conditional version ranges but the maintenance cost was judged not worth it

**Trade-offs:**

| Concern | Mitigation |
|---------|------------|
| Multi-TFM build time | Single CI leg; 10.0 SDK compiles both TFMs in one pass |
| Aspire lock-in | ServiceDefaults is opt-in; server still runs standalone without AppHost |
| .NET 9 EoL consumers | Grace period + clear deprecation notice in package release notes |
| Bicep vs Aspire deployment | Both coexist — Bicep for production, Aspire for dev/staging |

## Implementation Checklist

**Phase 1: .NET 10 Upgrade (Application Layer)**
- [x] Add `global.json` with .NET 10 SDK pin (`10.0.100`, `rollForward: latestFeature`)
- [x] Update `Directory.Build.props`: `RuntimePackageVersion` → `10.0`, `AspNetPackageVersion` → `10.0.0`
- [x] Update `Directory.Packages.props`: bump `Microsoft.Extensions.*`, `Microsoft.EntityFrameworkCore.*`, `Microsoft.AspNetCore.*` to 10.0.x
- [x] Change Application/DataLayer/Api csproj files from `net9.0` → `net10.0`
- [x] Update Dockerfile SDK/runtime images to `10.0-azurelinux3.0`
- [x] Update CI matrix to `[ '10.0.x' ]` (single-leg, 10.0 SDK only)
- [x] Verify build and tests pass on .NET 10

**Phase 2: Core Multi-Targeting**
- [x] Update each Core csproj individually: `<TargetFramework>` → `<TargetFrameworks>net9.0;net10.0</TargetFrameworks>`
- [x] Add RepoGuards test enforcing all packable Core projects multi-target `net9.0;net10.0`
- [x] Resolve any conditional compilation needs (`#if NET10_0_OR_GREATER`)
- [x] Verify NuGet pack produces multi-TFM packages
- [x] Test Core packages from a `net9.0` consumer project (via multi-targeted test execution)

**Phase 3: Aspire Integration**
- [ ] Add `Ignixa.AppHost` project with Aspire SDK references
- [ ] Add `Ignixa.ServiceDefaults` project (OpenTelemetry, health checks, resilience)
- [ ] Wire `Ignixa.Web` to use ServiceDefaults
- [ ] Configure AppHost to orchestrate SQL Server + Azurite + FHIR server
- [ ] Verify F5 experience: `dotnet run --project Ignixa.AppHost` starts full stack

**Phase 4: Deployment Updates**
- [ ] Update container image references in deployment scripts
- [ ] Document Aspire manifest as alternative deployment path
- [ ] Update README with new development setup instructions

**Phase 5: .NET 9 Deprecation (future)**
- [ ] Announce deprecation timeline in release notes (superseded: tracked in [ADR 2611](adr-2611-dotnet11.md))
- [x] Remove `net9.0` from Core TFMs (done by [ADR 2611](adr-2611-dotnet11.md); Core now targets `net10.0;net11.0`)
- [ ] Remove `net9.0` conditional compilation blocks (`#if NET10_0_OR_GREATER`) where the guard is no longer needed (unchanged; with `net9.0` gone, any remaining guard is a cleanup candidate)

## References

- **.NET 10 Release**: https://dotnet.microsoft.com/en-us/platform/dotnet-10
- **ASP.NET Aspire**: https://learn.microsoft.com/en-us/dotnet/aspire/
- **Multi-targeting**: https://learn.microsoft.com/en-us/nuget/create-packages/multiple-target-frameworks-project-file
- **Azure Linux containers**: https://learn.microsoft.com/en-us/azure/azure-linux/
- **.NET Support Policy**: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core

# ADR 2611: .NET 11 Support and net8/net9 Removal

## Status

Accepted

Supersedes the target-framework policy in [ADR 2607](adr-2607-dotnet10-aspire.md) (sections 1, 2, 4, 5, 7 and 8). The Aspire portion of ADR 2607 is unaffected.

## Context

.NET 11 (STS, SDK 11.0.100, C# 15) reached general availability on 2026-11-10. On the same date .NET 8 and .NET 9 reached end of support. .NET 10 (LTS) and .NET 11 are both supported until 2028-11-14 ([support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)).

Before this change the repository built with the .NET 10 SDK; packable Core libraries targeted `net9.0;net10.0`, the server and tools targeted `net10.0`, and code generation targeted `net8.0`. Carrying two unsupported runtimes would add build and test cost for no supported consumer, while the supported runtimes (10 and 11) diverge enough in ASP.NET Core and OpenAPI that tracking 11 is worthwhile.

## Decision

| Component | Target framework(s) |
|-----------|---------------------|
| Packable Core libraries (`src/Core/**`, `Ignixa.Sidecar.Contracts`) | `net10.0;net11.0` |
| Public dotnet tools (`Ignixa.DeId.Cli`, `Ignixa.FhirFakes.Cli`, `Ignixa.SqlOnFhir.Cli`, `Ignixa.Validation.Cli`, `Ignixa.ConformanceMatrix.Cli`) | `net10.0;net11.0` |
| Server and runtime (`src/Application/**`, `src/DataLayer/**`, `Ignixa.Web`), `Ignixa.SchemaUpgrade.Cli`, perf tools, benchmarks | `net11.0` |
| Code generation (`Ignixa.Specification.Generators`) | `net10.0` (was `net8.0`) |
| Analyzers and source generators | `netstandard2.0` (unchanged) |

- `global.json` pins SDK `11.0.100` with `rollForward: latestFeature`.
- Container images move to `mcr.microsoft.com/dotnet/sdk:11.0-azurelinux3.0` and `aspnet:11.0-azurelinux3.0`.
- CI installs the 10.0.x and 11.0.x SDKs/runtimes; multi-targeted test projects run on both runtimes.
- `Microsoft.Extensions.*`, `System.Collections.Immutable` and `System.Security.Cryptography.Xml` use target-framework-conditional versions in `Directory.Packages.props`: **11.0.0** for `net11.0`, unchanged (10.0.10) for every other target, so `net10.0` consumers of the multi-targeted libraries are not forced onto 11.x packages. The ASP.NET Core packages (`Microsoft.AspNetCore.OpenApi`, `Authentication.JwtBearer`, `Mvc.Testing`) are referenced only by `net11.0` projects and are set to 11.0.0 unconditionally. `Microsoft.AspNetCore.OpenApi` 11.0.0 depends on `Microsoft.OpenApi` 3.11.0 or later, which clears the advisory behind the former 2.7.6 pin, so that pin is removed. ASP.NET Core 11 emits OpenAPI 3.2 by default; Ignixa pins its generated document to **3.1** (`OpenApiSpecVersion.OpenApi3_1`) so existing client generators keep working, and a test asserts the served document reports `3.1`.
- **EF Core stays on 10.x and `Microsoft.Data.SqlClient` on 6.x.** EF Core 11 brings SqlClient 7, which moves Microsoft Entra ID authentication out of the core package. That migration is deferred to a separate change.
- Out of scope: Aspire adoption (ADR 2607 phase 3) and a change of container base OS.

## Consequences

**Positive:**
- Every shipped target framework is in support; the repository tracks the current runtime and SDK.
- Dropping `net9.0` and `net8.0` shortens build and pack time and removes the `net9.0` conditional package groups.
- Tools install and run on either supported runtime, so users are not forced to install .NET 11 to use them.
- Server images take .NET 11 runtime improvements immediately.

**Negative:**
- **Breaking change for NuGet consumers on `net9.0` (or `net8.0`):** new releases of the Core packages no longer ship a `net9.0` asset. Consumers must move to .NET 10 or later; those who cannot should stay on the previous package version.
- CI must install two SDK/runtime lines and run multi-targeted tests twice.
- .NET 11 is STS and shares its end-of-support date (2028-11-14) with .NET 10, so the supported-runtime window must be revisited before then.
- EF Core and SqlClient lag the runtime until the SqlClient 7 / Entra ID change lands.

**Risks to verify (tracked separately, not conclusions):**
- .NET 11 on x64 requires x86-64-v2 capable CPUs; older hosts and emulators may fail to start.
- Kestrel is stricter about HTTP/2 and HTTP/3 headers and about requests that combine `Content-Length` with `Transfer-Encoding`; clients and proxies may be affected.
- `TarReader` validates entry checksums, so previously tolerated malformed archives may be rejected.
- `Math.Round(double, int)` behaviour changed; numeric outputs that depend on rounding need checking.

## Implementation Checklist

- [x] `global.json` pins SDK `11.0.100`
- [x] Retarget Core libraries and public tools to `net10.0;net11.0`; server, data layer, perf tools to `net11.0`
- [x] Retarget code generation to `net10.0`
- [x] Update Dockerfile, CI and package versions (conditional 11.0.0 Extensions/System packages for `net11.0`; ASP.NET Core packages 11.0.0; `Microsoft.OpenApi` pin removed in favour of the 3.11.0 floor from `Microsoft.AspNetCore.OpenApi`)
- [x] Pin generated OpenAPI document to 3.1 (with a test)
- [ ] Evaluate EF Core 11 / SqlClient 7 and Entra ID authentication (separate change)
- [ ] Announce the `net9.0` removal in release notes

## References

- **.NET Support Policy**: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- **.NET 11 Release**: https://dotnet.microsoft.com/en-us/platform/dotnet-11
- [ADR 2607: .NET 10 Support and ASP.NET Aspire Adoption](adr-2607-dotnet10-aspire.md)

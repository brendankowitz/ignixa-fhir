# Hostname / Subdomain Tenant Resolution — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolve the tenant from the request `Host` header (e.g. `fhir1.example.org` → tenant 1) so a tenant is reachable without a `/tenant/{id}/` path prefix, while keeping the existing numeric path form working.

**Architecture:** Accept many inbound forms, emit exactly one. A tenant's numeric `TenantId` stays the internal partition key; a config-supplied list of `Hostnames` becomes an additional inbound selector resolved through an in-memory host→tenant index. `FhirServiceBaseUriResolver` gains a tenant-addressing overload that produces a recognition set (canonical first) covering every hostname plus the `/tenant/{id}/` path form. The request path and the background/import path build the identical set, so a self-reference classifies the same either way — the invariant PR #357 established. Host is authoritative only after it matches the config allowlist; an unknown host never resolves a tenant.

**Tech Stack:** .NET 10 / C# 13, ASP.NET Core minimal APIs + middleware, Autofac DI, xUnit + Shouldly, appsettings-based tenant config.

## Global Constraints

- Target framework `net10.0`; nullable enabled; `LangVersion` latest. Build must be **0 warnings, 0 errors** (warnings are errors; CI additionally enforces CA rules the local build may not — match existing conventions, e.g. object-initializer `TheoryData` not collection expressions).
- One public type per file. System `using`s first, outside the namespace. File-scoped namespaces.
- Async methods take a `CancellationToken` named `cancellationToken` (not `ct`) for **new** signatures; existing methods in touched files already use `ct` — do not rename them, match the local file.
- Tests: AAA, Shouldly, xUnit, naming `GivenContext_WhenAction_ThenResult`. No `#region`.
- Numeric `TenantId` is the internal partition identity and MUST NOT change meaning. Hostnames are a routing/addressing concern only.
- `TenantId 0` is the reserved system partition (`SystemConstants.SystemPartitionId`) — never resolvable from a hostname, never a valid API tenant.
- Security invariant: a `Host` value resolves a tenant **only** if it is present in the configured host index. An unrecognized host must not select a tenant. The canonical base for a tenant is its configured canonical hostname (or the path form), never the raw request `Host`.
- Drift invariant (from PR #357): the request path and the background/import path must build the **identical** recognition set for a given tenant. Any change to set construction lives in one method both call.

## Out of scope (explicit — do NOT build here)

- **Path vanity slugs** (`/tenant/{slug}/` or `/{slug}/`). Routes today pin `{tenantId:int}` (e.g. `CompartmentEndpoints.cs:50` `MapGroup("/tenant/{tenantId:int}")`); accepting a slug means relaxing that constraint across every endpoint group and disambiguating a slug segment from a resource type. That is a separate plan. This plan leaves `TenantConfiguration.Slug` unbuilt (YAGNI) — hostnames deliver the requested `fhir1.example.org` form without touching routing.
- **Custom apex vanity domains** (`acme.com`). Mechanically these are just more entries in the host index and need no code change beyond config, but their TLS/DNS provisioning and the decision to allow them is deployment policy — capture in the ADR, not here.
- **Per-tenant TLS/DNS/wildcard-cert provisioning.** Infra, not code.

---

## File Structure

- `src/Application/Ignixa.Domain/Models/TenantConfiguration.cs` — **modify.** Add `Hostnames` to the `TenantConfiguration` record.
- `src/Application/Ignixa.Application/Infrastructure/TenantAddressing.cs` — **create.** Immutable value passed to the resolver: `TenantId`, `Hostnames`, `IncludeDeploymentRoot`.
- `src/Application/Ignixa.Domain/Abstractions/ITenantConfigurationStore.cs` — **modify.** Add `ResolveByHostAsync`.
- `src/Application/Ignixa.Application/Infrastructure/AppSettingsTenantConfigurationStore.cs` — **modify.** Build a host→config index (lazy, `OrdinalIgnoreCase`), reject duplicate hosts, implement `ResolveByHostAsync`.
- `src/Application/Ignixa.Application/Infrastructure/FhirServiceBaseUriResolver.cs` — **modify.** Add `Resolve(Uri?, TenantAddressing)` producing the recognition set (canonical first).
- `src/Application/Ignixa.Api/Middleware/TenantResolutionMiddleware.cs` — **modify.** Resolve from `Host` before the route branch; reject a host/path tenant disagreement with 400.
- `src/Application/Ignixa.Api/Middleware/FhirRequestContextMiddleware.cs` — **modify.** Build `TenantAddressing` from the resolved tenant and pass it to the resolver.
- `src/Application/Ignixa.Application/Infrastructure/FhirRequestContextFactory.cs` — **modify.** Background contexts resolve their canonical base from tenant config (they already carry `TenantId`).
- `src/Application/Ignixa.Api/Registrations/SearchServicesRegistration.cs` — **modify.** Startup validation of hostname format + cross-tenant uniqueness.
- `docs/site/docs/server/configuration.md` — **modify.** Document hostname config, precedence, conflict behavior.
- Tests: `test/Ignixa.Application.Tests/Infrastructure/*` and `test/Ignixa.Api.Tests/Middleware/*`.

---

### Task 1: Add `Hostnames` to `TenantConfiguration`

**Files:**
- Modify: `src/Application/Ignixa.Domain/Models/TenantConfiguration.cs` (the `TenantConfiguration` record, after the `Packages` property ~line 79)
- Test: `test/Ignixa.Application.Tests/Infrastructure/TenantConfigurationHostnameBindingTests.cs` (create)

**Interfaces:**
- Produces: `TenantConfiguration.Hostnames` → `IReadOnlyList<string>`, default empty, bound from `Tenants:Configurations:{n}:Hostnames`.

- [ ] **Step 1: Write the failing test**

```csharp
// test/Ignixa.Application.Tests/Infrastructure/TenantConfigurationHostnameBindingTests.cs
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure;

public class TenantConfigurationHostnameBindingTests
{
    [Fact]
    public async Task GivenHostnamesInConfig_WhenTenantLoaded_ThenHostnamesAreBound()
    {
        // Arrange
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenants:Configurations:0:TenantId"] = "1",
            ["Tenants:Configurations:0:DisplayName"] = "Acme",
            ["Tenants:Configurations:0:FhirVersion"] = "4.0",
            ["Tenants:Configurations:0:Hostnames:0"] = "fhir1.example.org",
            ["Tenants:Configurations:0:Hostnames:1"] = "acme.example.org",
        }).Build();
        var store = new AppSettingsTenantConfigurationStore(config, NullLogger<AppSettingsTenantConfigurationStore>.Instance);

        // Act
        var tenant = await store.GetTenantConfigurationAsync(1);

        // Assert
        tenant.ShouldNotBeNull();
        tenant.Hostnames.ShouldBe(["fhir1.example.org", "acme.example.org"]);
    }

    [Fact]
    public async Task GivenNoHostnamesInConfig_WhenTenantLoaded_ThenHostnamesIsEmpty()
    {
        // Arrange
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenants:Configurations:0:TenantId"] = "1",
            ["Tenants:Configurations:0:DisplayName"] = "Acme",
            ["Tenants:Configurations:0:FhirVersion"] = "4.0",
        }).Build();
        var store = new AppSettingsTenantConfigurationStore(config, NullLogger<AppSettingsTenantConfigurationStore>.Instance);

        // Act
        var tenant = await store.GetTenantConfigurationAsync(1);

        // Assert
        tenant.ShouldNotBeNull();
        tenant.Hostnames.ShouldBeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -c Release --filter "FullyQualifiedName~TenantConfigurationHostnameBindingTests"`
Expected: FAIL — `TenantConfiguration` has no `Hostnames` member (compile error).

- [ ] **Step 3: Add the property**

In `src/Application/Ignixa.Domain/Models/TenantConfiguration.cs`, inside the `TenantConfiguration` record, after the `Packages` property:

```csharp
    /// <summary>
    /// Hostnames this tenant answers on (e.g. "fhir1.example.org"). The first entry is the canonical base
    /// this tenant's absolute references are stored and emitted under; the rest are additional recognized
    /// inbound forms. Empty means the tenant is reached only via the /tenant/{id}/ path form. Hostnames are
    /// unique across all tenants; a host resolves exactly one tenant.
    /// </summary>
    public IReadOnlyList<string> Hostnames { get; init; } = Array.Empty<string>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -c Release --filter "FullyQualifiedName~TenantConfigurationHostnameBindingTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Application/Ignixa.Domain/Models/TenantConfiguration.cs test/Ignixa.Application.Tests/Infrastructure/TenantConfigurationHostnameBindingTests.cs
git commit -m "feat(tenancy): bind per-tenant Hostnames from config"
```

---

### Task 2: `TenantAddressing` value + `ResolveByHostAsync` host index

**Files:**
- Create: `src/Application/Ignixa.Application/Infrastructure/TenantAddressing.cs`
- Modify: `src/Application/Ignixa.Domain/Abstractions/ITenantConfigurationStore.cs` (add method after `GetAllTenantsAsync`)
- Modify: `src/Application/Ignixa.Application/Infrastructure/AppSettingsTenantConfigurationStore.cs` (add lazy host index + method + duplicate rejection)
- Test: `test/Ignixa.Application.Tests/Infrastructure/TenantHostIndexTests.cs` (create)

**Interfaces:**
- Produces: `TenantAddressing(int TenantId, IReadOnlyList<string> Hostnames, bool IncludeDeploymentRoot)` — a `sealed record`.
- Produces: `ITenantConfigurationStore.ResolveByHostAsync(string host, CancellationToken) → ValueTask<TenantConfiguration?>` — returns the active tenant registered for `host` (case-insensitive), or null.
- Consumes: `TenantConfiguration.Hostnames` (Task 1).

- [ ] **Step 1: Write the failing test**

```csharp
// test/Ignixa.Application.Tests/Infrastructure/TenantHostIndexTests.cs
using Ignixa.Application.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure;

public class TenantHostIndexTests
{
    private static AppSettingsTenantConfigurationStore Store(Dictionary<string, string?> values) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            NullLogger<AppSettingsTenantConfigurationStore>.Instance);

    [Fact]
    public async Task GivenAKnownHost_WhenResolvingByHost_ThenReturnsTheOwningTenant()
    {
        var store = Store(new()
        {
            ["Tenants:Configurations:0:TenantId"] = "1",
            ["Tenants:Configurations:0:DisplayName"] = "Acme",
            ["Tenants:Configurations:0:FhirVersion"] = "4.0",
            ["Tenants:Configurations:0:Hostnames:0"] = "fhir1.example.org",
        });

        var tenant = await store.ResolveByHostAsync("FHIR1.EXAMPLE.ORG");

        tenant.ShouldNotBeNull();
        tenant.TenantId.ShouldBe(1);
    }

    [Fact]
    public async Task GivenAnUnknownHost_WhenResolvingByHost_ThenReturnsNull()
    {
        var store = Store(new()
        {
            ["Tenants:Configurations:0:TenantId"] = "1",
            ["Tenants:Configurations:0:DisplayName"] = "Acme",
            ["Tenants:Configurations:0:FhirVersion"] = "4.0",
            ["Tenants:Configurations:0:Hostnames:0"] = "fhir1.example.org",
        });

        (await store.ResolveByHostAsync("evil.attacker.test")).ShouldBeNull();
    }

    [Fact]
    public async Task GivenTheSameHostOnTwoTenants_WhenResolvingByHost_ThenThrowsAtLoad()
    {
        var store = Store(new()
        {
            ["Tenants:Configurations:0:TenantId"] = "1",
            ["Tenants:Configurations:0:DisplayName"] = "Acme",
            ["Tenants:Configurations:0:FhirVersion"] = "4.0",
            ["Tenants:Configurations:0:Hostnames:0"] = "shared.example.org",
            ["Tenants:Configurations:1:TenantId"] = "2",
            ["Tenants:Configurations:1:DisplayName"] = "Beta",
            ["Tenants:Configurations:1:FhirVersion"] = "4.0",
            ["Tenants:Configurations:1:Hostnames:0"] = "shared.example.org",
        });

        // A host that maps to two tenants is a cross-tenant confusion hazard; fail loudly, not silently.
        await Should.ThrowAsync<InvalidOperationException>(async () => await store.ResolveByHostAsync("shared.example.org"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -c Release --filter "FullyQualifiedName~TenantHostIndexTests"`
Expected: FAIL — `ResolveByHostAsync` does not exist (compile error).

- [ ] **Step 3: Create `TenantAddressing`**

```csharp
// src/Application/Ignixa.Application/Infrastructure/TenantAddressing.cs
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// The addressing facts <see cref="FhirServiceBaseUriResolver"/> needs to build a tenant's recognition set,
/// independent of how the tenant was resolved (host header, path, or auto-detect).
/// </summary>
/// <param name="TenantId">Internal partition identity.</param>
/// <param name="Hostnames">Hostnames the tenant answers on; the first is canonical. May be empty.</param>
/// <param name="IncludeDeploymentRoot">
/// Whether the bare deployment root is a recognized base for this tenant. True only for the sole tenant of a
/// single-tenant deployment, where <c>example.org/Patient</c> is a valid self-reference. Including it for one
/// of several tenants would conflate <c>example.org/Patient/1</c> across tenants.
/// </param>
public sealed record TenantAddressing(
    int TenantId,
    IReadOnlyList<string> Hostnames,
    bool IncludeDeploymentRoot);
```

- [ ] **Step 4: Add the interface method**

In `src/Application/Ignixa.Domain/Abstractions/ITenantConfigurationStore.cs`, after `GetAllTenantsAsync`:

```csharp
    /// <summary>
    /// Resolves the active tenant registered for <paramref name="host"/> (case-insensitive), or null if no
    /// tenant claims it. Hostnames are unique across tenants; a host claimed by more than one is a
    /// configuration error and throws.
    /// </summary>
    ValueTask<TenantConfiguration?> ResolveByHostAsync(
        string host,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 5: Implement the host index in the store**

In `src/Application/Ignixa.Application/Infrastructure/AppSettingsTenantConfigurationStore.cs`:

Add a field beside `_tenants` (~line 22):

```csharp
    private readonly Lazy<IReadOnlyDictionary<string, TenantConfiguration>> _hostIndex;
```

Initialise it in the constructor, after `_tenants` is assigned:

```csharp
        _hostIndex = new Lazy<IReadOnlyDictionary<string, TenantConfiguration>>(BuildHostIndex);
```

Add the builder and the resolve method (place near `GetTenantConfigurationAsync`):

```csharp
    private IReadOnlyDictionary<string, TenantConfiguration> BuildHostIndex()
    {
        var index = new Dictionary<string, TenantConfiguration>(StringComparer.OrdinalIgnoreCase);

        foreach (var tenant in _tenants.Value)
        {
            if (tenant.IsSystemPartition || !tenant.IsActive)
            {
                continue;
            }

            foreach (var host in tenant.Hostnames)
            {
                var normalized = host.Trim();
                if (normalized.Length == 0)
                {
                    continue;
                }

                if (index.TryGetValue(normalized, out var existing))
                {
                    throw new InvalidOperationException(
                        $"Hostname '{normalized}' is claimed by both tenant {existing.TenantId} and tenant {tenant.TenantId}. A hostname must resolve exactly one tenant.");
                }

                index[normalized] = tenant;
            }
        }

        return index;
    }

    public ValueTask<TenantConfiguration?> ResolveByHostAsync(
        string host,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        return _hostIndex.Value.TryGetValue(host.Trim(), out var tenant)
            ? ValueTask.FromResult<TenantConfiguration?>(tenant)
            : ValueTask.FromResult<TenantConfiguration?>(null);
    }
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -c Release --filter "FullyQualifiedName~TenantHostIndexTests"`
Expected: PASS (3 tests). The duplicate-host test throws from the lazy index build on first `ResolveByHostAsync`.

- [ ] **Step 7: Commit**

```bash
git add src/Application/Ignixa.Application/Infrastructure/TenantAddressing.cs src/Application/Ignixa.Domain/Abstractions/ITenantConfigurationStore.cs src/Application/Ignixa.Application/Infrastructure/AppSettingsTenantConfigurationStore.cs test/Ignixa.Application.Tests/Infrastructure/TenantHostIndexTests.cs
git commit -m "feat(tenancy): host->tenant index with uniqueness enforcement"
```

---

### Task 3: Resolver overload building the recognition set from `TenantAddressing`

**Files:**
- Modify: `src/Application/Ignixa.Application/Infrastructure/FhirServiceBaseUriResolver.cs` (add an overload; keep the existing `Resolve(Uri?, int?, FhirServiceBaseUriForm)` untouched for back-compat)
- Test: `test/Ignixa.Application.Tests/Infrastructure/FhirServiceBaseUriResolverTenantAddressingTests.cs` (create)

**Interfaces:**
- Consumes: `TenantAddressing` (Task 2), `FhirServiceBaseUri.Normalize` (existing, `Ignixa.Abstractions`).
- Produces: `FhirServiceBaseUriResolver.Resolve(Uri? requestOrigin, TenantAddressing tenant) → IReadOnlyList<Uri>` — canonical base at index 0, followed by every other recognized base, de-duplicated, order-preserving. Empty when no root is available.

- [ ] **Step 1: Write the failing test**

```csharp
// test/Ignixa.Application.Tests/Infrastructure/FhirServiceBaseUriResolverTenantAddressingTests.cs
using Ignixa.Application.Infrastructure;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure;

public class FhirServiceBaseUriResolverTenantAddressingTests
{
    private static readonly Uri Root = new("https://example.org/");

    [Fact]
    public void GivenAConfiguredHostname_WhenResolving_ThenTheHostIsCanonicalAndThePathFormIsAlsoRecognized()
    {
        // Arrange
        var resolver = new FhirServiceBaseUriResolver(Root);
        var tenant = new TenantAddressing(1, ["fhir1.example.org"], IncludeDeploymentRoot: false);

        // Act
        var bases = resolver.Resolve(requestOrigin: null, tenant);

        // Assert
        bases[0].ShouldBe(new Uri("https://fhir1.example.org/"));
        bases.ShouldContain(new Uri("https://example.org/tenant/1/"));
        bases.ShouldNotContain(Root);
    }

    [Fact]
    public void GivenNoHostname_WhenResolving_ThenThePathFormIsCanonical()
    {
        // Arrange
        var resolver = new FhirServiceBaseUriResolver(Root);
        var tenant = new TenantAddressing(2, [], IncludeDeploymentRoot: false);

        // Act
        var bases = resolver.Resolve(requestOrigin: null, tenant);

        // Assert
        bases[0].ShouldBe(new Uri("https://example.org/tenant/2/"));
    }

    [Fact]
    public void GivenTheSoleTenant_WhenResolving_ThenTheDeploymentRootIsRecognized()
    {
        // Arrange
        var resolver = new FhirServiceBaseUriResolver(Root);
        var tenant = new TenantAddressing(1, [], IncludeDeploymentRoot: true);

        // Act
        var bases = resolver.Resolve(requestOrigin: null, tenant);

        // Assert
        bases.ShouldContain(Root);
    }

    [Fact]
    public void GivenNoConfiguredRootAndNoRequestOrigin_WhenResolving_ThenTheSetIsEmpty()
    {
        // Arrange
        var resolver = new FhirServiceBaseUriResolver(configuredServiceRoot: null);
        var tenant = new TenantAddressing(1, ["fhir1.example.org"], IncludeDeploymentRoot: false);

        // Act
        var bases = resolver.Resolve(requestOrigin: null, tenant);

        // Assert
        bases.ShouldBeEmpty();
    }

    [Fact]
    public void GivenMultipleHostnames_WhenResolving_ThenAllAreRecognizedCanonicalFirst()
    {
        // Arrange
        var resolver = new FhirServiceBaseUriResolver(Root);
        var tenant = new TenantAddressing(1, ["fhir1.example.org", "acme.example.org"], IncludeDeploymentRoot: false);

        // Act
        var bases = resolver.Resolve(requestOrigin: null, tenant);

        // Assert
        bases[0].ShouldBe(new Uri("https://fhir1.example.org/"));
        bases.ShouldContain(new Uri("https://acme.example.org/"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -c Release --filter "FullyQualifiedName~FhirServiceBaseUriResolverTenantAddressingTests"`
Expected: FAIL — the `Resolve(Uri?, TenantAddressing)` overload does not exist (compile error).

- [ ] **Step 3: Add the overload**

In `src/Application/Ignixa.Application/Infrastructure/FhirServiceBaseUriResolver.cs`, add after the existing `Resolve` method:

```csharp
    /// <summary>
    /// Resolves every base URI that identifies this server for a tenant, canonical first. The canonical base
    /// is the tenant's first configured hostname, or the <c>tenant/{id}/</c> path form when it has none. The
    /// remaining hostnames and the path form are additional recognized inbound bases; the deployment root is
    /// recognized only when <see cref="TenantAddressing.IncludeDeploymentRoot"/> is set. Both the request
    /// path and the background path call this method, so a self-reference classifies identically either way.
    /// </summary>
    public IReadOnlyList<Uri> Resolve(Uri? requestOrigin, TenantAddressing tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var root = _configuredServiceRoot ?? FhirServiceBaseUri.Normalize(requestOrigin);

        if (root is null || !root.IsAbsoluteUri)
        {
            return [];
        }

        var bases = new List<Uri>();

        foreach (var host in tenant.Hostnames)
        {
            var hostBase = FhirServiceBaseUri.Normalize(new Uri($"{root.Scheme}://{host.Trim()}/"));
            if (hostBase is not null)
            {
                bases.Add(hostBase);
            }
        }

        // Numeric path form is always recognized (and canonical when no hostname is configured), so a
        // reference stored via /tenant/{id}/ still classifies as internal after the switch to hostnames.
        bases.Add(new Uri(root, $"tenant/{tenant.TenantId}/"));

        if (tenant.IncludeDeploymentRoot)
        {
            bases.Add(root);
        }

        return bases.Distinct().ToArray();
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -c Release --filter "FullyQualifiedName~FhirServiceBaseUriResolverTenantAddressingTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Application/Ignixa.Application/Infrastructure/FhirServiceBaseUriResolver.cs test/Ignixa.Application.Tests/Infrastructure/FhirServiceBaseUriResolverTenantAddressingTests.cs
git commit -m "feat(tenancy): recognition set from tenant hostnames, canonical first"
```

---

### Task 4: Resolve tenant from `Host` in the middleware, reject host/path conflict

**Files:**
- Modify: `src/Application/Ignixa.Api/Middleware/TenantResolutionMiddleware.cs`
- Test: `test/Ignixa.Api.Tests/Middleware/TenantResolutionHostnameTests.cs` (create)

**Interfaces:**
- Consumes: `ITenantConfigurationStore.ResolveByHostAsync` (Task 2). Middleware already consumes `_configStore`, `HttpContext.Items["TenantId"]`, `HttpContext.Items["TenantConfiguration"]`.
- Produces: on a host match, `HttpContext.Items["TenantId"]` and `["TenantConfiguration"]` are set exactly as the route branch sets them. On host/path disagreement, a 400 `OperationOutcome` and the request short-circuits.

**Design (precedence, implemented at the top of `InvokeAsync`):**
1. Resolve `hostTenant` from `context.Request.Host.Host` via `ResolveByHostAsync`.
2. If the route carries a numeric `tenantId` **and** `hostTenant` is non-null **and** they disagree → 400 (tenant confusion).
3. If the route carries `tenantId` → existing route branch (unchanged) wins; a matching `hostTenant` is redundant and fine.
4. Else if `hostTenant` is non-null → set items from `hostTenant`, skip auto-detect.
5. Else → existing auto-detect / multi-tenant-400 branch (unchanged).

- [ ] **Step 1: Write the failing test**

```csharp
// test/Ignixa.Api.Tests/Middleware/TenantResolutionHostnameTests.cs
using Ignixa.Api.Middleware;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Middleware;

public class TenantResolutionHostnameTests
{
    private static TenantConfiguration Tenant(int id, params string[] hosts) => new()
    {
        TenantId = id,
        DisplayName = $"T{id}",
        FhirVersion = "4.0",
        Hostnames = hosts,
    };

    [Fact]
    public async Task GivenARequestOnATenantHost_WhenResolved_ThenTenantIsSetFromTheHost()
    {
        // Arrange
        var store = Substitute.For<ITenantConfigurationStore>();
        store.ResolveByHostAsync("fhir2.example.org", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration?>(Tenant(2, "fhir2.example.org")));

        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString("fhir2.example.org");
        ctx.Request.Path = "/Patient";
        ctx.Request.Method = "GET";
        var mw = new TenantResolutionMiddleware(_ => Task.CompletedTask, store, NullLogger<TenantResolutionMiddleware>.Instance);

        // Act
        await mw.InvokeAsync(ctx);

        // Assert
        ctx.Items["TenantId"].ShouldBe(2);
    }

    [Fact]
    public async Task GivenAHostAndPathThatDisagree_WhenResolved_ThenReturns400()
    {
        // Arrange — host says tenant 1, path says tenant 2.
        var store = Substitute.For<ITenantConfigurationStore>();
        store.ResolveByHostAsync("fhir1.example.org", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration?>(Tenant(1, "fhir1.example.org")));

        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Host = new HostString("fhir1.example.org");
        ctx.Request.Path = "/tenant/2/Patient";
        ctx.Request.RouteValues["tenantId"] = "2";
        ctx.Request.Method = "GET";
        var nextCalled = false;
        var mw = new TenantResolutionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, store, NullLogger<TenantResolutionMiddleware>.Instance);

        // Act
        await mw.InvokeAsync(ctx);

        // Assert
        ctx.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        nextCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenAnUnknownHostAndNoRoute_WhenResolved_ThenFallsThroughToAutoDetect()
    {
        // Arrange — unknown host must not resolve a tenant; single active tenant auto-detects.
        var store = Substitute.For<ITenantConfigurationStore>();
        store.ResolveByHostAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration?>((TenantConfiguration?)null));
        store.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<TenantConfiguration>>(new[] { Tenant(1) }));
        store.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration?>(Tenant(1)));

        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString("nothing.example.org");
        ctx.Request.Path = "/Patient";
        ctx.Request.Method = "GET";
        var mw = new TenantResolutionMiddleware(_ => Task.CompletedTask, store, NullLogger<TenantResolutionMiddleware>.Instance);

        // Act
        await mw.InvokeAsync(ctx);

        // Assert
        ctx.Items["TenantId"].ShouldBe(1);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.Api.Tests/Ignixa.Api.Tests.csproj -c Release --filter "FullyQualifiedName~TenantResolutionHostnameTests"`
Expected: FAIL — host resolution not wired; `GivenARequestOnATenantHost` leaves `TenantId` unset, conflict test does not 400.

- [ ] **Step 3: Wire host resolution into `InvokeAsync`**

In `src/Application/Ignixa.Api/Middleware/TenantResolutionMiddleware.cs`, at the very start of `InvokeAsync` (before the existing `if (context.Request.RouteValues.TryGetValue("tenantId", ...))`), add:

```csharp
        // Host-based tenant selection. A host resolves a tenant only if it is in the configured index;
        // an unknown host resolves nothing and the request falls through to the route/auto-detect branches.
        TenantConfiguration? hostTenant = null;
        if (context.Request.Host.HasValue)
        {
            hostTenant = await _configStore.ResolveByHostAsync(context.Request.Host.Host, context.RequestAborted);
        }
```

Then, inside the existing route branch, immediately after `int.TryParse(...)` yields `tenantId` and before the system-partition check, add the conflict guard:

```csharp
            // Host and path must not name different tenants; a silent pick would be a cross-tenant leak.
            if (hostTenant is not null && hostTenant.TenantId != tenantId)
            {
                _logger.LogWarning(
                    "Host/path tenant conflict: host {HostTenant} vs path {PathTenant} for {Method} {Path}",
                    hostTenant.TenantId,
                    tenantId,
                    context.Request.Method.SanitizeForLog(),
                    context.Request.Path.Value.SanitizeForLog());

                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = KnownContentTypes.ApplicationFhirJson;

                var conflict = new OperationOutcome();
                conflict.Issue.Add(new OperationOutcomeIssue
                {
                    SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Error,
                    IssueTypeCode = OperationOutcomeIssue.IssueTypeCommon.BusinessRule,
                    Diagnostics = "The request host and the /tenant/{id}/ path resolve to different tenants."
                });

                await context.Response.Body.WriteAsync(conflict.SerializeToBytes(), context.RequestAborted);
                return;
            }
```

Then, change the `else if (IsResourceEndpoint(context))` branch so a host match is used before auto-detect. Replace the opening of that branch:

```csharp
        else if (hostTenant is not null)
        {
            context.Items["TenantId"] = hostTenant.TenantId;
            context.Items["TenantConfiguration"] = hostTenant;

            _logger.LogDebug(
                "Resolved tenant {TenantId} ({DisplayName}) from host {Host} for {Method} {Path}",
                hostTenant.TenantId,
                hostTenant.DisplayName,
                context.Request.Host.Host.SanitizeForLog(),
                context.Request.Method.SanitizeForLog(),
                context.Request.Path.Value.SanitizeForLog());
        }
        else if (IsResourceEndpoint(context))
        {
            // ... existing auto-detect body unchanged ...
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.Api.Tests/Ignixa.Api.Tests.csproj -c Release --filter "FullyQualifiedName~TenantResolutionHostnameTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Run the full middleware suite to confirm no regression**

Run: `dotnet test test/Ignixa.Api.Tests/Ignixa.Api.Tests.csproj -c Release`
Expected: PASS (all existing middleware tests still green; numeric `/tenant/{id}/` path unchanged).

- [ ] **Step 6: Commit**

```bash
git add src/Application/Ignixa.Api/Middleware/TenantResolutionMiddleware.cs test/Ignixa.Api.Tests/Middleware/TenantResolutionHostnameTests.cs
git commit -m "feat(tenancy): resolve tenant from Host header, reject host/path conflict"
```

---

### Task 5: Build `TenantAddressing` in the request-context middleware and pass it to the resolver

**Files:**
- Modify: `src/Application/Ignixa.Api/Middleware/FhirRequestContextMiddleware.cs` (the `ServiceBaseUris` assignment ~line 71)
- Test: `test/Ignixa.Api.Tests/Middleware/FhirRequestContextServiceBaseUriTests.cs` (extend — this file exists from PR #357)

**Interfaces:**
- Consumes: `FhirServiceBaseUriResolver.Resolve(Uri?, TenantAddressing)` (Task 3), `HttpContext.Items["TenantConfiguration"]` (Task 4 / existing), `ITenantConfigurationStore.Mode` and `GetAllTenantsAsync` for the sole-tenant flag.
- Produces: `fhirContext.ServiceBaseUris` = the tenant-addressing recognition set when a tenant is resolved; `fhirContext.BaseUri` = its canonical (index 0).

**Design:** `IncludeDeploymentRoot` is true only when the deployment has exactly one active tenant (the existing auto-detect condition). The middleware already resolves `TenantConfiguration`; derive `TenantAddressing` from it. When no tenant is resolved (e.g. `/metadata`), keep the current fallback to the existing `Resolve(Uri?, int?, FhirServiceBaseUriForm)` path.

- [ ] **Step 1: Write the failing test**

```csharp
// Add to test/Ignixa.Api.Tests/Middleware/FhirRequestContextServiceBaseUriTests.cs
[Fact]
public async Task GivenARequestOnATenantHost_WhenContextBuilt_ThenBaseUriIsTheCanonicalHost()
{
    // Arrange: tenant 1 with canonical host fhir1.example.org, request arriving on that host.
    // (Use the fixture pattern already in this file: resolved TenantConfiguration in HttpContext.Items,
    //  Fhir:BaseUri = https://example.org/, FhirServiceBaseUriResolver constructed from it.)
    var context = BuildHttpContext(
        host: "fhir1.example.org",
        path: "/Patient",
        resolvedTenant: new TenantConfiguration
        {
            TenantId = 1, DisplayName = "Acme", FhirVersion = "4.0",
            Hostnames = new[] { "fhir1.example.org" },
        });

    // Act
    await InvokeMiddleware(context);

    // Assert
    var fhirContext = ResolveFhirContext(context);
    fhirContext.BaseUri.ShouldBe(new Uri("https://fhir1.example.org/"));
    fhirContext.ServiceBaseUris.ShouldContain(new Uri("https://example.org/tenant/1/"));
}
```

Note: reuse the existing helpers in this file (`BuildHttpContext`, `InvokeMiddleware`, `ResolveFhirContext` or their local equivalents). If the file names them differently, match the existing ones — do not introduce a parallel harness.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Api.Tests/Ignixa.Api.Tests.csproj -c Release --filter "FullyQualifiedName~FhirRequestContextServiceBaseUriTests.GivenARequestOnATenantHost"`
Expected: FAIL — base URI is the `/tenant/1/` path form, not the host.

- [ ] **Step 3: Build `TenantAddressing` and call the overload**

In `src/Application/Ignixa.Api/Middleware/FhirRequestContextMiddleware.cs`, replace the `ServiceBaseUris` assignment block:

```csharp
        if (fhirContext.TenantConfiguration is { } resolvedTenant)
        {
            var soleTenant = (await _configStore.GetAllTenantsAsync(httpContext.RequestAborted)).Count == 1;

            fhirContext.ServiceBaseUris = serviceBaseUriResolver.Resolve(
                BuildRequestOrigin(httpContext),
                new TenantAddressing(resolvedTenant.TenantId, resolvedTenant.Hostnames, IncludeDeploymentRoot: soleTenant));
        }
        else
        {
            fhirContext.ServiceBaseUris = serviceBaseUriResolver.Resolve(
                BuildRequestOrigin(httpContext),
                resolvedTenantId,
                CanonicalFormFor(httpContext));
        }

        fhirContext.BaseUri = fhirContext.ServiceBaseUris is [var canonical, ..] ? canonical : null;
```

Add `ITenantConfigurationStore` to the middleware's injected dependencies if it is not already present (constructor and the `InvokeAsync` parameter list follow the existing DI pattern in this file — middleware here resolve services as `InvokeAsync` parameters). Add `using Ignixa.Application.Infrastructure;` for `TenantAddressing` and `using Ignixa.Domain.Abstractions;` for the store if missing.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.Api.Tests/Ignixa.Api.Tests.csproj -c Release --filter "FullyQualifiedName~FhirRequestContextServiceBaseUriTests"`
Expected: PASS (new test plus the file's existing tests).

- [ ] **Step 5: Commit**

```bash
git add src/Application/Ignixa.Api/Middleware/FhirRequestContextMiddleware.cs test/Ignixa.Api.Tests/Middleware/FhirRequestContextServiceBaseUriTests.cs
git commit -m "feat(tenancy): request context base URIs from tenant hostnames"
```

---

### Task 6: Background/import contexts resolve the canonical host base

**Files:**
- Modify: `src/Application/Ignixa.Application/Infrastructure/FhirRequestContextFactory.cs` (`CreateBackgroundContext`)
- Modify: `src/Application/Ignixa.Application/Infrastructure/FhirRequestContextBaseUriProvider.cs` (the background fallback) OR the import activities' addressing — see design note
- Test: `test/Ignixa.Application.Tests/Infrastructure/BackgroundContextHostBaseUriTests.cs` (create)

**Interfaces:**
- Consumes: `FhirServiceBaseUriResolver.Resolve(Uri?, TenantAddressing)` (Task 3), `ITenantConfigurationStore.GetTenantConfigurationAsync` (existing).
- Produces: a background context whose `ServiceBaseUris`/`BaseUri` equal what a request on the tenant's canonical host would produce.

**Design note (pick the seam, do not do both):** PR #357 wired `CreateBackgroundContext(tenantId)` into the import activities and left the base to `FhirRequestContextBaseUriProvider.GetServiceBaseUris()`'s fallback, which currently calls `resolver.Resolve(null, context?.TenantId, TenantScoped)` — the **path-form** overload. Change that fallback to load the tenant's `Hostnames` and call the `TenantAddressing` overload, so background indexing reaches the same canonical host as the request path. The provider already has access to the ambient context (TenantId); inject `ITenantConfigurationStore` to fetch `Hostnames`. This keeps the single-code-path drift invariant: both paths end in the same `Resolve(Uri?, TenantAddressing)`.

- [ ] **Step 1: Write the failing test**

```csharp
// test/Ignixa.Application.Tests/Infrastructure/BackgroundContextHostBaseUriTests.cs
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure;

public class BackgroundContextHostBaseUriTests
{
    [Fact]
    public async Task GivenABackgroundContextForATenantWithAHost_WhenResolvingBaseUris_ThenItMatchesTheRequestPath()
    {
        // Arrange
        var store = Substitute.For<ITenantConfigurationStore>();
        store.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration?>(new TenantConfiguration
            {
                TenantId = 1, DisplayName = "Acme", FhirVersion = "4.0",
                Hostnames = new[] { "fhir1.example.org" },
            }));
        store.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<TenantConfiguration>>(new[]
            {
                new TenantConfiguration { TenantId = 1, DisplayName = "Acme", FhirVersion = "4.0" },
                new TenantConfiguration { TenantId = 2, DisplayName = "Beta", FhirVersion = "4.0" },
            }));

        var accessor = new FhirRequestContextAccessor
        {
            RequestContext = FhirRequestContextFactory.CreateBackgroundContext(1),
        };
        var resolver = new FhirServiceBaseUriResolver(new Uri("https://example.org/"));
        var provider = new FhirRequestContextBaseUriProvider(accessor, resolver, store);

        // Act
        var bases = provider.GetServiceBaseUris();

        // Assert
        bases[0].ShouldBe(new Uri("https://fhir1.example.org/"));
    }
}
```

Note: this assumes `FhirRequestContextBaseUriProvider` gains an `ITenantConfigurationStore` constructor parameter. If the provider must stay synchronous (`GetServiceBaseUris()` is not async), fetch the tenant config synchronously via a cached snapshot the store already exposes (`GetAllTenantsAsync` is backed by a `Lazy` array — add a synchronous `TryGetTenant(int, out TenantConfiguration)` helper to the store, or block on the `ValueTask` which completes synchronously for the appsettings store). Prefer adding the synchronous accessor over blocking.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -c Release --filter "FullyQualifiedName~BackgroundContextHostBaseUriTests"`
Expected: FAIL — provider does not take a store; still emits the `/tenant/1/` path form.

- [ ] **Step 3: Change the provider fallback to use hostnames**

In `src/Application/Ignixa.Application/Infrastructure/FhirRequestContextBaseUriProvider.cs`, add the store dependency and rewrite the fallback branch:

```csharp
public sealed class FhirRequestContextBaseUriProvider(
    IFhirRequestContextAccessor requestContextAccessor,
    FhirServiceBaseUriResolver resolver,
    ITenantConfigurationStore configStore) : IFhirBaseUriProvider
{
    public Uri? GetBaseUri() => GetServiceBaseUris() is [var canonical, ..] ? canonical : null;

    public IReadOnlyList<Uri> GetServiceBaseUris()
    {
        var context = requestContextAccessor.RequestContext;

        if (context?.ServiceBaseUris is { Count: > 0 } fromRequest)
        {
            return fromRequest;
        }

        if (context?.TenantId is { } tenantId and > 0)
        {
            var tenant = configStore.GetTenantConfigurationAsync(tenantId).GetAwaiter().GetResult();
            if (tenant is not null)
            {
                var soleTenant = configStore.GetAllTenantsAsync().GetAwaiter().GetResult().Count == 1;
                return resolver.Resolve(
                    requestOrigin: null,
                    new TenantAddressing(tenantId, tenant.Hostnames, IncludeDeploymentRoot: soleTenant));
            }
        }

        return resolver.Resolve(requestOrigin: null, context?.TenantId, FhirServiceBaseUriForm.TenantScoped);
    }
}
```

The `GetAwaiter().GetResult()` is safe here: `AppSettingsTenantConfigurationStore` returns already-completed `ValueTask`s (backed by a `Lazy` array), so there is no blocking wait. Add `using Ignixa.Domain.Abstractions;` if missing. Update the Autofac registration for `FhirRequestContextBaseUriProvider` to supply the store (it is already container-registered).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -c Release --filter "FullyQualifiedName~BackgroundContextHostBaseUriTests"`
Expected: PASS.

- [ ] **Step 5: Run the PR #357 base-URI wiring tests to confirm the invariant holds**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -c Release --filter "FullyQualifiedName~FhirRequestContextBaseUriProvider|FullyQualifiedName~ImportBatchActivityBaseUriWiring"`
Expected: PASS — request and background paths still agree.

- [ ] **Step 6: Commit**

```bash
git add src/Application/Ignixa.Application/Infrastructure/FhirRequestContextBaseUriProvider.cs test/Ignixa.Application.Tests/Infrastructure/BackgroundContextHostBaseUriTests.cs
git commit -m "feat(tenancy): background base URIs resolve tenant hostnames"
```

---

### Task 7: Validate hostname format and cross-tenant uniqueness at startup

**Files:**
- Modify: `src/Application/Ignixa.Api/Registrations/SearchServicesRegistration.cs` (the existing `Fhir:BaseUri` startup-validation hook ~line 129) OR add a sibling validator invoked from the same startup path
- Test: `test/Ignixa.Application.Tests/Infrastructure/TenantHostnameValidationTests.cs` (create)

**Interfaces:**
- Consumes: `ITenantConfigurationStore.GetAllTenantsAsync` (existing), `TenantConfiguration.Hostnames` (Task 1).
- Produces: `TenantHostnameValidator.Validate(IReadOnlyList<TenantConfiguration>) → IReadOnlyList<string>` returning human-readable problems (empty = valid). A non-empty result is logged as a startup error and, for a duplicate host, is fatal (throws) — a duplicate host is the cross-tenant-confusion case and must not boot.

**Rules:**
- Each hostname is a valid DNS hostname: one or more labels of `[a-z0-9]` / internal `-`, each label 1–63 chars, lowercased, no scheme, no port, no path. Reject otherwise (error, non-fatal — the host simply won't route).
- A hostname claimed by more than one tenant → fatal error (throw at startup). Mirrors the index build in Task 2 but surfaced at boot rather than first request.

- [ ] **Step 1: Write the failing test**

```csharp
// test/Ignixa.Application.Tests/Infrastructure/TenantHostnameValidationTests.cs
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Models;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure;

public class TenantHostnameValidationTests
{
    private static TenantConfiguration T(int id, params string[] hosts) =>
        new() { TenantId = id, DisplayName = $"T{id}", FhirVersion = "4.0", Hostnames = hosts };

    [Fact]
    public void GivenValidUniqueHostnames_WhenValidated_ThenNoProblems()
    {
        var problems = TenantHostnameValidator.Validate([T(1, "fhir1.example.org"), T(2, "fhir2.example.org")]);
        problems.ShouldBeEmpty();
    }

    [Fact]
    public void GivenAHostnameWithSchemeOrPort_WhenValidated_ThenReportsIt()
    {
        var problems = TenantHostnameValidator.Validate([T(1, "https://fhir1.example.org"), T(2, "fhir2.example.org:8080")]);
        problems.Count.ShouldBe(2);
    }

    [Fact]
    public void GivenADuplicateHostnameAcrossTenants_WhenValidated_ThenReportsIt()
    {
        var problems = TenantHostnameValidator.Validate([T(1, "shared.example.org"), T(2, "shared.example.org")]);
        problems.ShouldContain(p => p.Contains("shared.example.org"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -c Release --filter "FullyQualifiedName~TenantHostnameValidationTests"`
Expected: FAIL — `TenantHostnameValidator` does not exist.

- [ ] **Step 3: Create the validator**

```csharp
// src/Application/Ignixa.Application/Infrastructure/TenantHostnameValidator.cs
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.Domain.Models;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Validates tenant hostname configuration: each hostname is a bare DNS host, and no hostname is claimed by
/// more than one tenant. Returns human-readable problems; an empty list means valid.
/// </summary>
public static partial class TenantHostnameValidator
{
    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9](-?[a-z0-9])*)(\.[a-z0-9](-?[a-z0-9])*)*$")]
    private static partial Regex HostnameShape();

    public static IReadOnlyList<string> Validate(IReadOnlyList<TenantConfiguration> tenants)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        var problems = new List<string>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var tenant in tenants)
        {
            foreach (var host in tenant.Hostnames)
            {
                var value = host.Trim();

                if (!HostnameShape().IsMatch(value))
                {
                    problems.Add($"Tenant {tenant.TenantId}: '{host}' is not a bare lowercase DNS hostname (no scheme, port, or path).");
                    continue;
                }

                if (seen.TryGetValue(value, out var otherTenant))
                {
                    problems.Add($"Hostname '{value}' is claimed by tenant {otherTenant} and tenant {tenant.TenantId}; a hostname must resolve exactly one tenant.");
                    continue;
                }

                seen[value] = tenant.TenantId;
            }
        }

        return problems;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -c Release --filter "FullyQualifiedName~TenantHostnameValidationTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Invoke the validator at startup**

In `src/Application/Ignixa.Api/Registrations/SearchServicesRegistration.cs`, in the startup hook that already reads and reports `Fhir:BaseUri` (~line 129), after resolving the tenant store, add:

```csharp
        var tenants = configStore.GetAllTenantsAsync().GetAwaiter().GetResult();
        var hostnameProblems = TenantHostnameValidator.Validate(tenants);

        foreach (var problem in hostnameProblems)
        {
            logger.LogError("Tenant hostname configuration problem: {Problem}", problem);
        }

        if (hostnameProblems.Any(p => p.Contains("claimed by tenant")))
        {
            throw new InvalidOperationException(
                "Duplicate tenant hostname configuration; refusing to start. See preceding log entries.");
        }
```

Match the surrounding pattern for obtaining `logger` and `configStore` (both are already available in that startup path or resolvable from the container).

- [ ] **Step 6: Run to verify build + tests**

Run: `dotnet build All.sln -c Release` then `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -c Release --filter "FullyQualifiedName~TenantHostnameValidationTests"`
Expected: build 0/0; tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Application/Ignixa.Application/Infrastructure/TenantHostnameValidator.cs src/Application/Ignixa.Api/Registrations/SearchServicesRegistration.cs test/Ignixa.Application.Tests/Infrastructure/TenantHostnameValidationTests.cs
git commit -m "feat(tenancy): validate tenant hostnames at startup"
```

---

### Task 8: Documentation

**Files:**
- Modify: `docs/site/docs/server/configuration.md`
- Modify: `src/Application/Ignixa.Web/appsettings.json` (add a commented `Hostnames` example under a tenant, matching the file's existing comment style)

**Interfaces:** none (docs only).

- [ ] **Step 1: Document hostname config and precedence**

Add a "Hostname-based tenant resolution" section to `docs/site/docs/server/configuration.md` stating:

- Each tenant may declare `Hostnames: []`; the first is the canonical base for that tenant's absolute references, the rest are additional recognized inbound hosts.
- Resolution precedence: a request `Host` in the configured index selects that tenant; `/tenant/{id}/` in the path selects by id; if both are present and disagree the server returns **400**; an unrecognized host selects no tenant and falls through to `/tenant/{id}/` or single-tenant auto-detect.
- Hostnames are unique across tenants; a duplicate is a fatal startup error.
- Numeric `/tenant/{id}/` continues to work unchanged.
- TLS: subdomains under one zone are covered by a single wildcard certificate; wildcards are single-level; apex/vanity domains need their own certificate.
- Note explicitly that **path vanity slugs (`/tenant/{slug}/`) are not yet supported** and link to the follow-up plan.

- [ ] **Step 2: Validate docs build**

Run: the repo's docs validation (the CI step "Validate Documentation"; locally follow `docs/site/README.md`).
Expected: docs build succeeds.

- [ ] **Step 3: Commit**

```bash
git add docs/site/docs/server/configuration.md src/Application/Ignixa.Web/appsettings.json
git commit -m "docs(tenancy): hostname-based tenant resolution"
```

---

## Final verification (run after all tasks)

- [ ] `dotnet build All.sln -c Release` → 0 warnings, 0 errors.
- [ ] `dotnet test All.sln -c Release --filter "FullyQualifiedName!~E2ETests"` → all green (SqlOnFhir submodule + the known `Validation` net9 timing test are environmental; ignore only those).
- [ ] Manual smoke: configure two tenants with `fhir1.localhost` / `fhir2.localhost`, start the server, and confirm `GET http://fhir1.localhost:PORT/metadata` and `GET http://fhir2.localhost:PORT/metadata` resolve to different tenants, while `GET http://localhost:PORT/tenant/1/metadata` still works and `Host: fhir1.localhost` + `/tenant/2/` returns 400.

## Deferred to a follow-up plan (path vanity slugs)

`/tenant/{slug}/` and bare `/{slug}/` require: relaxing `{tenantId:int}` route constraints across every endpoint group, an int-or-slug route constraint, disambiguating a slug segment from a resource type, adding `Slug` to `TenantConfiguration` + a slug index, making the slug path form canonical, and a slug format/uniqueness validator. Write it as its own plan; it builds on the `TenantAddressing`/recognition-set machinery this plan establishes.

# Package Management System Enhancements

## Status
**Investigation & Planning** - November 17, 2025

## Purpose
This document tracks potential enhancements to the FHIR package management system to improve usability, reliability, and feature completeness.

---

## Current Limitations

### 1. No Dependency Resolution

**Current State:**
- Packages are installed individually with no automatic dependency handling
- Dependencies listed in `package.json` are ignored
- Users must manually install all required packages in dependency order

**Evidence:**

1. **Package Manifest Structure** (`src/Ignixa.PackageManagement/Models/PackageManifest.cs:1`):
   ```csharp
   public record PackageManifest
   {
       public required string Name { get; init; }
       public required string Version { get; init; }
       public required string FhirVersion { get; init; }
       public string? Title { get; init; }
       public string? Description { get; init; }
       public string? License { get; init; }
       // ❌ NO dependencies field!
   }
   ```

2. **Package Extraction** (`src/Ignixa.PackageManagement/Infrastructure/PackageExtractor.cs:82`):
   - Only extracts `package.json` for metadata
   - Does NOT parse or process the `dependencies` field
   - Does NOT trigger recursive package downloads

3. **Load Handler** (`src/Ignixa.Application/Features/Admin/LoadPackageHandler.cs:55`):
   - Only loads the single specified package
   - Does NOT check for or install dependencies

**Real-World Impact:**

**Example: Installing US Core 5.0.1**
```json
// package.json from hl7.fhir.us.core@5.0.1
{
  "name": "hl7.fhir.us.core",
  "version": "5.0.1",
  "dependencies": {
    "hl7.fhir.r4.core": "4.0.1",
    "us.nlm.vsac": "0.3.0",
    "hl7.terminology.r4": "5.0.0"
  }
}
```

**Current Behavior:**
- User installs `hl7.fhir.us.core@5.0.1`
- Dependencies are **NOT** automatically installed
- US Core profiles may reference terminology from `hl7.terminology.r4` that isn't available
- Validation may fail with missing ValueSet errors

**User Workaround:**
```bash
# Manual dependency installation (in correct order)
POST /admin/packages/load
{"packageId": "hl7.fhir.r4.core", "version": "4.0.1", "tenantId": "1"}

POST /admin/packages/load
{"packageId": "hl7.terminology.r4", "version": "5.0.0", "tenantId": "1"}

POST /admin/packages/load
{"packageId": "us.nlm.vsac", "version": "0.3.0", "tenantId": "1"}

POST /admin/packages/load
{"packageId": "hl7.fhir.us.core", "version": "5.0.1", "tenantId": "1"}
```

---

## Proposed Enhancements

### Enhancement 1: Automatic Dependency Resolution

**Priority:** 🟡 MEDIUM
**Estimated Effort:** 3-4 weeks
**Complexity:** Medium (requires circular dependency detection, version conflict resolution)

#### Goals
1. Automatically install package dependencies when loading a package
2. Detect and prevent circular dependencies
3. Resolve version conflicts using semantic versioning
4. Provide clear feedback on dependency installation progress

#### Design

**1. Update PackageManifest Model**

```csharp
// File: src/Ignixa.PackageManagement/Models/PackageManifest.cs
public record PackageManifest
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string FhirVersion { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? License { get; init; }

    // NEW: Dependencies from package.json
    public Dictionary<string, string>? Dependencies { get; init; }
}
```

**2. Create Dependency Resolver Service**

```csharp
// File: src/Ignixa.PackageManagement/Infrastructure/PackageDependencyResolver.cs
public interface IPackageDependencyResolver
{
    /// <summary>
    /// Resolves all dependencies for a package and returns installation order.
    /// </summary>
    Task<DependencyResolutionResult> ResolveAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken);
}

public record DependencyResolutionResult
{
    /// <summary>
    /// Packages to install in dependency order (dependencies first, requested package last).
    /// </summary>
    public required List<PackageReference> InstallationOrder { get; init; }

    /// <summary>
    /// Packages that were already installed (skipped).
    /// </summary>
    public required List<PackageReference> AlreadyInstalled { get; init; }

    /// <summary>
    /// Conflicts detected during resolution (e.g., version mismatches).
    /// </summary>
    public List<DependencyConflict>? Conflicts { get; init; }
}

public record PackageReference(string PackageId, string Version);

public record DependencyConflict(
    string PackageId,
    string RequestedVersion,
    string ExistingVersion,
    string ConflictSource);

public class PackageDependencyResolver : IPackageDependencyResolver
{
    private readonly IPackageLoader _packageLoader;
    private readonly IPackageExtractor _packageExtractor;
    private readonly IPackageResourceRepository _packageRepository;
    private readonly ILogger<PackageDependencyResolver> _logger;

    public async Task<DependencyResolutionResult> ResolveAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken)
    {
        var installationOrder = new List<PackageReference>();
        var alreadyInstalled = new List<PackageReference>();
        var visited = new HashSet<string>(); // For circular dependency detection
        var versionMap = new Dictionary<string, string>(); // For conflict detection

        await ResolveRecursiveAsync(
            packageId,
            version,
            visited,
            versionMap,
            installationOrder,
            alreadyInstalled,
            cancellationToken);

        return new DependencyResolutionResult
        {
            InstallationOrder = installationOrder,
            AlreadyInstalled = alreadyInstalled,
            Conflicts = DetectConflicts(versionMap)
        };
    }

    private async Task ResolveRecursiveAsync(
        string packageId,
        string version,
        HashSet<string> visited,
        Dictionary<string, string> versionMap,
        List<PackageReference> installationOrder,
        List<PackageReference> alreadyInstalled,
        CancellationToken cancellationToken)
    {
        var key = $"{packageId}@{version}";

        // Circular dependency check
        if (visited.Contains(key))
        {
            throw new InvalidOperationException(
                $"Circular dependency detected: {packageId}@{version}");
        }

        visited.Add(key);

        // Check if already installed
        var existing = await _packageRepository.GetPackageAsync(packageId, version, cancellationToken);
        if (existing != null)
        {
            alreadyInstalled.Add(new PackageReference(packageId, version));
            _logger.LogInformation(
                "Package {PackageId}@{Version} already installed, skipping",
                packageId, version);
            return;
        }

        // Download and extract package to read dependencies
        using var packageStream = await _packageLoader.DownloadPackageAsync(
            packageId, version, cancellationToken);
        var extraction = await _packageExtractor.ExtractAsync(
            packageStream, cancellationToken);

        // Recursively resolve dependencies first (depth-first)
        if (extraction.Manifest.Dependencies != null)
        {
            foreach (var (depPackageId, depVersion) in extraction.Manifest.Dependencies)
            {
                // Track version requirements for conflict detection
                if (versionMap.TryGetValue(depPackageId, out var existingVersion))
                {
                    if (existingVersion != depVersion)
                    {
                        _logger.LogWarning(
                            "Version conflict for {PackageId}: {ExistingVersion} vs {NewVersion}",
                            depPackageId, existingVersion, depVersion);
                    }
                }
                else
                {
                    versionMap[depPackageId] = depVersion;
                }

                await ResolveRecursiveAsync(
                    depPackageId,
                    depVersion,
                    visited,
                    versionMap,
                    installationOrder,
                    alreadyInstalled,
                    cancellationToken);
            }
        }

        // Add this package to installation order (after dependencies)
        installationOrder.Add(new PackageReference(packageId, version));
    }

    private List<DependencyConflict>? DetectConflicts(Dictionary<string, string> versionMap)
    {
        // TODO: Implement semantic version conflict detection
        // For now, exact version matching only
        return null;
    }
}
```

**3. Update LoadPackageHandler**

```csharp
// File: src/Ignixa.Application/Features/Admin/LoadPackageHandler.cs
public class LoadPackageHandler : IRequestHandler<LoadPackageCommand, LoadPackageResult>
{
    private readonly IImplementationGuideProvider _provider;
    private readonly IPackageDependencyResolver _dependencyResolver; // NEW
    private readonly IMediator _mediator;
    private readonly ILogger<LoadPackageHandler> _logger;

    public async Task<LoadPackageResult> HandleAsync(
        LoadPackageCommand request,
        CancellationToken cancellationToken)
    {
        // NEW: Resolve dependencies first
        if (request.InstallDependencies) // NEW flag
        {
            _logger.LogInformation(
                "Resolving dependencies for {PackageId}@{Version}",
                request.PackageId, request.Version);

            var resolution = await _dependencyResolver.ResolveAsync(
                request.PackageId,
                request.Version,
                cancellationToken);

            if (resolution.Conflicts?.Any() == true)
            {
                _logger.LogWarning(
                    "Dependency conflicts detected: {Conflicts}",
                    string.Join(", ", resolution.Conflicts.Select(c =>
                        $"{c.PackageId}: {c.RequestedVersion} vs {c.ExistingVersion}")));

                // Option 1: Fail fast
                // throw new InvalidOperationException("Dependency conflicts detected");

                // Option 2: Continue with warning (use highest version)
            }

            _logger.LogInformation(
                "Installing {Count} packages in dependency order: {Packages}",
                resolution.InstallationOrder.Count,
                string.Join(" -> ", resolution.InstallationOrder.Select(p => $"{p.PackageId}@{p.Version}")));

            // Install dependencies first, then the requested package
            foreach (var package in resolution.InstallationOrder)
            {
                await _provider.LoadPackageAsync(
                    request.TenantId,
                    package.PackageId,
                    package.Version,
                    cancellationToken);

                // Publish PackageLoaded event for each package
                await _mediator.PublishAsync(
                    new PackageLoadedEvent(
                        PackageId: package.PackageId,
                        PackageVersion: package.Version,
                        TenantId: int.Parse(request.TenantId),
                        LoadedAt: DateTimeOffset.UtcNow),
                    cancellationToken);
            }

            return new LoadPackageResult
            {
                PackageId = request.PackageId,
                PackageVersion = request.Version,
                TotalResources = resolution.InstallationOrder.Sum(p => p.ResourceCount),
                ImportedResources = resolution.InstallationOrder.Sum(p => p.ResourceCount),
                DependenciesInstalled = resolution.InstallationOrder.Count - 1, // Exclude requested package
                AlreadyInstalled = resolution.AlreadyInstalled.Count
            };
        }

        // Existing code for single package installation
        var importResult = await _provider.LoadPackageAsync(
            request.TenantId,
            request.PackageId,
            request.Version,
            cancellationToken);

        // ... rest of existing code
    }
}
```

**4. Update LoadPackageCommand**

```csharp
// File: src/Ignixa.Application/Features/Admin/LoadPackageCommand.cs
public record LoadPackageCommand : IRequest<LoadPackageResult>
{
    public required string TenantId { get; init; }
    public required string PackageId { get; init; }
    public required string Version { get; init; }

    // NEW: Option to install dependencies
    public bool InstallDependencies { get; init; } = true; // Default: true
}
```

**5. Update API Endpoint**

```csharp
// File: src/Ignixa.Api/Endpoints/AdminPackageEndpoints.cs
app.MapPost("/admin/packages/load", async (
    HttpContext context,
    [FromBody] LoadPackageRequest request,
    IMediator mediator,
    CancellationToken cancellationToken) =>
{
    var command = new LoadPackageCommand
    {
        TenantId = request.TenantId,
        PackageId = request.PackageId,
        Version = request.Version,
        InstallDependencies = request.InstallDependencies ?? true // NEW
    };

    var result = await mediator.SendAsync(command, cancellationToken);
    return Results.Ok(result);
});

public record LoadPackageRequest(
    string TenantId,
    string PackageId,
    string Version,
    bool? InstallDependencies); // NEW: Optional, defaults to true
```

#### Testing Strategy

**1. Unit Tests**

```csharp
// File: test/Ignixa.PackageManagement.Tests/PackageDependencyResolverTests.cs

[Fact]
public async Task ResolveAsync_WithNoDependencies_ReturnsSinglePackage()
{
    // Arrange: Package with no dependencies
    var resolver = CreateResolver();

    // Act
    var result = await resolver.ResolveAsync("test.package", "1.0.0", CancellationToken.None);

    // Assert
    result.InstallationOrder.Should().HaveCount(1);
    result.InstallationOrder[0].Should().Be(new PackageReference("test.package", "1.0.0"));
}

[Fact]
public async Task ResolveAsync_WithLinearDependencies_ReturnsCorrectOrder()
{
    // Arrange: A -> B -> C (linear dependency chain)
    var resolver = CreateResolver(
        ("A", "1.0.0", new[] { ("B", "1.0.0") }),
        ("B", "1.0.0", new[] { ("C", "1.0.0") }),
        ("C", "1.0.0", Array.Empty<(string, string)>()));

    // Act
    var result = await resolver.ResolveAsync("A", "1.0.0", CancellationToken.None);

    // Assert
    result.InstallationOrder.Should().Equal(
        new PackageReference("C", "1.0.0"),
        new PackageReference("B", "1.0.0"),
        new PackageReference("A", "1.0.0"));
}

[Fact]
public async Task ResolveAsync_WithCircularDependency_ThrowsException()
{
    // Arrange: A -> B -> A (circular)
    var resolver = CreateResolver(
        ("A", "1.0.0", new[] { ("B", "1.0.0") }),
        ("B", "1.0.0", new[] { ("A", "1.0.0") }));

    // Act & Assert
    await resolver.Invoking(r => r.ResolveAsync("A", "1.0.0", CancellationToken.None))
        .Should().ThrowAsync<InvalidOperationException>()
        .WithMessage("*circular dependency*");
}

[Fact]
public async Task ResolveAsync_WithVersionConflict_DetectsConflict()
{
    // Arrange: A -> B@1.0.0, C -> B@2.0.0
    var resolver = CreateResolver(
        ("A", "1.0.0", new[] { ("B", "1.0.0"), ("C", "1.0.0") }),
        ("C", "1.0.0", new[] { ("B", "2.0.0") }));

    // Act
    var result = await resolver.ResolveAsync("A", "1.0.0", CancellationToken.None);

    // Assert
    result.Conflicts.Should().ContainSingle(c =>
        c.PackageId == "B" &&
        c.RequestedVersion == "2.0.0" &&
        c.ExistingVersion == "1.0.0");
}
```

**2. Integration Tests**

```csharp
// File: test/Ignixa.Api.Tests/Endpoints/AdminPackageEndpointsTests.cs

[Fact]
public async Task LoadPackage_WithDependencies_InstallsAll()
{
    // Arrange: Mock NPM registry with US Core + dependencies
    var request = new
    {
        tenantId = "1",
        packageId = "hl7.fhir.us.core",
        version = "5.0.1",
        installDependencies = true
    };

    // Act
    var response = await _client.PostAsJsonAsync("/admin/packages/load", request);

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var result = await response.Content.ReadFromJsonAsync<LoadPackageResult>();
    result.DependenciesInstalled.Should().BeGreaterThan(0);

    // Verify packages in database
    var packages = await GetInstalledPackagesAsync();
    packages.Should().Contain(p => p.PackageId == "hl7.fhir.r4.core");
    packages.Should().Contain(p => p.PackageId == "hl7.terminology.r4");
}

[Fact]
public async Task LoadPackage_WithoutDependencies_InstallsOnlyRequested()
{
    // Arrange
    var request = new
    {
        tenantId = "1",
        packageId = "hl7.fhir.us.core",
        version = "5.0.1",
        installDependencies = false
    };

    // Act
    var response = await _client.PostAsJsonAsync("/admin/packages/load", request);

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var result = await response.Content.ReadFromJsonAsync<LoadPackageResult>();
    result.DependenciesInstalled.Should().Be(0);
}
```

#### Migration Path

**Phase 1: Add dependency parsing (Week 1)**
- Update `PackageManifest` model with `Dependencies` field
- Update `PackageExtractor` to parse dependencies from `package.json`
- Add unit tests for manifest parsing

**Phase 2: Implement resolver (Week 2)**
- Create `PackageDependencyResolver` service
- Implement circular dependency detection
- Implement version conflict detection
- Add unit tests for resolver

**Phase 3: Integrate with load handler (Week 3)**
- Update `LoadPackageHandler` to use resolver
- Add `InstallDependencies` flag to command
- Update API endpoint
- Add integration tests

**Phase 4: User experience improvements (Week 4)**
- Add progress reporting for multi-package installations
- Add dry-run mode to preview dependencies before installing
- Add UI for dependency visualization
- Update documentation

#### Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| **Circular dependencies in FHIR packages** | Installation fails | LOW | FHIR packages are well-maintained, circular deps rare |
| **Version conflicts** | Incompatible packages installed | MEDIUM | Implement semantic version resolution, fail fast on conflicts |
| **Network timeouts during multi-package install** | Partial installation state | MEDIUM | Use transaction pattern, rollback on failure |
| **Large dependency trees (e.g., 10+ packages)** | Slow installation | LOW | Implement parallel download (respecting order), progress reporting |

---

### Enhancement 2: Package Update Detection

**Priority:** 🟢 LOW
**Estimated Effort:** 1 week
**Complexity:** Low

#### Goals
- Detect when newer versions of installed packages are available
- Provide API endpoint to check for updates
- Optional: Automatic update notifications

#### Design

```csharp
// File: src/Ignixa.PackageManagement/Infrastructure/PackageUpdateChecker.cs
public interface IPackageUpdateChecker
{
    Task<List<PackageUpdate>> CheckForUpdatesAsync(
        string tenantId,
        CancellationToken cancellationToken);
}

public record PackageUpdate(
    string PackageId,
    string CurrentVersion,
    string LatestVersion,
    bool IsBreakingChange);
```

---

### Enhancement 3: Package Rollback

**Priority:** 🟢 LOW
**Estimated Effort:** 2 weeks
**Complexity:** Medium

#### Goals
- Uninstall packages safely
- Rollback to previous package version
- Detect and prevent breaking other packages that depend on the one being removed

---

## Related Documentation

- [FHIR NPM Package Integration](fhir-npm-simplifier-package-integration.md)
- [Package Management Tenant Guide](../guides/package-management-tenant-guide.md)
- [Multi-Tenant Package Architecture](multi-tenant-providers.md)

---

**Document Status**: PROPOSED
**Last Updated**: 2025-11-17
**Next Review**: After Enhancement 1 implementation
**Owner**: Ignixa Development Team

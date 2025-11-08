# FHIR Package Integration: npm and Simplifier.net

## Status
**Investigation** - November 8, 2025

## Context

This investigation analyzes how to integrate FHIR Implementation Guides (IGs), profiles, extensions, and StructureMaps from npm registries and Simplifier.net into the Ignixa FHIR Server. This capability is essential for supporting:

- **Regulatory compliance** - US Core (USCDI), IPS (International Patient Summary)
- **Industry standards** - Da Vinci, CARIN Blue Button, IHE profiles
- **Custom profiles** - Organization-specific validation requirements
- **Cross-version conversion** - StructureMaps for R4↔R5 transformation

### Business Requirements

| Use Case | Package Source | Example |
|----------|---------------|---------|
| US Healthcare Provider | HL7 official registry | `hl7.fhir.us.core#6.1.0` |
| International Deployment | HL7 official registry | `hl7.fhir.uv.ips#1.1.0` |
| Research Organization | Simplifier.net (private) | `org.example.research.oncology#2.1.0` |
| Version Migration | HL7 cross-version | `hl7.fhir.uv.xver.r4-r5#0.1.0` |
| Custom Extensions | Simplifier.net (public) | `ihe.fhir.iti#1.0.0` |

## Current Architecture Analysis

### Validation System (`src/Ignixa.Validation/`)

The current validation architecture is **three-tier** (Fast/Spec/Profile) and **schema-driven**:

**Core Abstraction:**
```csharp
public interface IStructureDefinitionSummaryProvider
{
    IStructureDefinitionSummary? Provide(string canonical);
}

public interface IFhirSchemaProvider : IStructureDefinitionSummaryProvider
{
    FhirSpecification Version { get; }
    IReadOnlySet<string> ResourceTypeNames { get; }
    string FullVersion { get; }
}
```

**Current Implementation:**

1. **Build-time Code Generation** (`codegen/`)
   - Uses Microsoft's `fhir-codegen` with custom `ILanguage` implementation
   - Downloads FHIR packages: `hl7.fhir.r4.core#4.0.1`, etc.
   - Generates complete providers: `R4StructureDefinitionSummaryProvider.g.cs` (~7.2 MB, 59K lines)
   - Output: `src/Ignixa.Specification/Generated/*.g.cs`

2. **Schema Resolution** (`src/Ignixa.Validation/Schema/`)
   - `StructureDefinitionSchemaResolver` - Resolves schemas by canonical URL
   - `CachedValidationSchemaResolver` - Thread-safe caching (ConcurrentDictionary)
   - `StructureDefinitionSchemaBuilder` - Builds ValidationSchema from IStructureDefinitionSummary

3. **Validation Tiers**
   - **Fast** (<25ms): JSON structure, cardinality, types
   - **Spec** (<200ms): + FHIRPath invariants, references, coding structure
   - **Profile** (<1000ms): + Fixed values, patterns, bindings, slicing

### Current Limitations

❌ **No custom profile support** - Only FHIR core resources
❌ **No IG loading** - Cannot load US Core, IPS, Da Vinci, etc.
❌ **No dynamic package management** - Everything is build-time codegen
❌ **No extension definitions** - Extensions validated structurally, not semantically
❌ **No StructureMap support** - No cross-version conversion
❌ **No tenant-specific profiles** - All tenants share same schema providers

### Key Strength: Well-Positioned for Extension

✅ **Clean abstraction** - `IFhirSchemaProvider` is version-agnostic
✅ **Composable design** - Can chain multiple providers
✅ **Existing package infrastructure** - Codegen already downloads/parses packages
✅ **Performance-oriented** - Caching, lazy loading, immutable state

## FHIR Package Ecosystem

### Package Format (npm-based)

FHIR packages use the npm tarball format (`.tgz`) with FHIR-specific conventions:

```
hl7.fhir.us.core#6.1.0.tgz
├── package/
│   ├── package.json                 # Metadata (name, version, dependencies)
│   ├── .index.json                  # Quick lookup index
│   ├── StructureDefinition-*.json   # Profile definitions (114 files for US Core)
│   ├── ValueSet-*.json              # ValueSets (terminology bindings)
│   ├── CodeSystem-*.json            # CodeSystems
│   ├── SearchParameter-*.json       # Custom search parameters
│   ├── ConceptMap-*.json            # Mappings
│   ├── StructureMap-*.json          # Transformations (e.g., R4→R5)
│   └── examples/                     # Example resources
│       ├── Patient-example.json
│       └── .index.json
```

**package.json Example:**
```json
{
  "name": "hl7.fhir.us.core",
  "version": "6.1.0",
  "title": "US Core Implementation Guide",
  "canonical": "http://hl7.org/fhir/us/core",
  "fhirVersions": ["4.0.1"],
  "dependencies": {
    "hl7.fhir.r4.core": "4.0.1",
    "us.nlm.vsac": "0.3.0"
  },
  "author": "HL7 International",
  "description": "US Core profiles for USCDI v3"
}
```

### Package Registries

| Registry | URL | Authentication | Use Case |
|----------|-----|----------------|----------|
| **HL7 Official** | `https://packages.fhir.org/` | None (public) | HL7 published IGs (US Core, IPS, Da Vinci) |
| **Simplifier.net** | `https://packages.simplifier.net/` | API key for private | Public + private IGs, custom profiles |
| **Build.fhir.org** | `https://build.fhir.org/ig/` | None | CI builds (latest, pre-release) |

**Simplifier.net API:**
```bash
# Download package
GET https://packages.simplifier.net/{name}/-/{name}-{version}.tgz

# Search packages
GET https://packages.simplifier.net/?name=us-core&fhirversion=4.0

# Authentication (for private packages)
POST https://api.simplifier.net/token
Content-Type: application/json
Body: {"email": "...", "password": "..."}
Response: {"access_token": "..."}

# Download private package
GET https://packages.simplifier.net/{name}/-/{name}-{version}.tgz
Authorization: Bearer {access_token}
```

### Common Implementation Guides

| IG | Package Name | FHIR Version | Purpose |
|----|--------------|--------------|---------|
| **US Core** | `hl7.fhir.us.core` | R4, R4B | US EHR data profiles (Meaningful Use, USCDI) |
| **IPS** | `hl7.fhir.uv.ips` | R4, R5 | International Patient Summary (WHO) |
| **CARIN BB** | `hl7.fhir.us.carin-bb` | R4 | Consumer insurance data (Blue Button 2.0) |
| **Da Vinci PDex** | `hl7.fhir.us.davinci-pdex` | R4 | Payer data exchange |
| **IHE Profiles** | `ihe.fhir.iti` | R4, R5 | IHE IT Infrastructure profiles |
| **Cross-version** | `hl7.fhir.uv.xver.r4-r5` | R4/R5 | StructureMaps for version conversion |
| **mCODE** | `hl7.fhir.us.mcode` | R4 | Minimal Common Oncology Data Elements |

## Integration Options

### Option A: Build-Time Code Generation (Extend Current Approach)

**How it works:**
```bash
# Generate providers for core + specified IGs
cd codegen
./generate.ps1 -FhirVersion R4 -IG "hl7.fhir.us.core#6.1.0"

# Outputs:
# - R4StructureDefinitionSummaryProvider.g.cs (core)
# - USCoreStructureDefinitionSummaryProvider.g.cs (profiles)
# - CompositeSchemaProvider.g.cs (merges both)
```

**Architecture:**
```csharp
public class R4WithUSCoreProvider : IFhirSchemaProvider
{
    private readonly R4StructureDefinitionSummaryProvider _core;
    private readonly USCoreStructureDefinitionSummaryProvider _usCore;

    public IStructureDefinitionSummary? Provide(string canonical)
    {
        // Try US Core first (profiles override core)
        return _usCore.Provide(canonical) ?? _core.Provide(canonical);
    }
}
```

**Pros:**
- ✅ Zero runtime overhead (no package parsing)
- ✅ Type-safe, compile-time checked
- ✅ Leverages existing codegen infrastructure (`fhir-codegen` + custom `ILanguage`)
- ✅ Works with current architecture (no changes to validation layer)
- ✅ No external dependencies at runtime
- ✅ Fastest validation performance

**Cons:**
- ❌ Not dynamic - requires rebuild to change IGs
- ❌ Large binary size (each IG adds ~5-10 MB)
- ❌ Cannot support tenant-specific IGs (all tenants share same build)
- ❌ No support for custom/private IGs without rebuild
- ❌ Inflexible for rapid IG updates

**Best for:**
- Fixed IG requirements (e.g., "always validate against US Core 6.1.0")
- Deployment scenarios where IGs don't change
- Regulatory compliance (locked-down versions)
- Single-tenant deployments

**Effort:** 16 hours (extend existing codegen, test with US Core)

---

### Option B: Runtime Package Loading (Dynamic)

**How it works:**
```csharp
// Tenant configuration
{
  "Tenants": {
    "Configurations": [{
      "TenantId": 1,
      "ImplementationGuides": [
        {
          "PackageName": "hl7.fhir.us.core",
          "Version": "6.1.0",
          "Source": "packages.fhir.org"
        },
        {
          "PackageName": "my.custom.profiles",
          "Version": "1.0.0",
          "Source": "simplifier.net",
          "ApiKey": "secret-key"
        }
      ]
    }]
  }
}

// Runtime resolution
var packages = await _packageCache.LoadAsync(tenantId, fhirVersion);
var schemaProvider = new CompositeSchemaProvider(
    new R4CoreProvider(),           // Generated (core)
    new PackageSchemaProvider(packages) // Runtime (IGs)
);
```

**Architecture:**
```
┌─────────────────────────────────────────────────────┐
│          IFhirSchemaProvider (interface)            │
└─────────────────────────────────────────────────────┘
                        ▲
                        │
        ┌───────────────┴───────────────┐
        │                               │
┌───────────────────┐      ┌──────────────────────────┐
│  Generated        │      │  Runtime Package         │
│  Providers        │      │  Provider                │
│  (build-time)     │      │  (dynamic)               │
├───────────────────┤      ├──────────────────────────┤
│ - R4Provider      │      │ - Loads .tgz packages    │
│ - R4BProvider     │      │ - Parses JSON resources  │
│ - R5Provider      │      │ - Caches summaries       │
└───────────────────┘      │ - Tenant-specific        │
         │                 └──────────────────────────┘
         │                              │
         └──────────┬───────────────────┘
                    ▼
        ┌─────────────────────────┐
        │  CompositeSchemaProvider│
        │  (chains multiple)      │
        └─────────────────────────┘
```

**Key Components:**

1. **FhirPackageLoader** - Download + extract .tgz packages
2. **FhirPackageCache** - Persistent cache (~/.fhir/packages/)
3. **PackageSchemaProvider** - IFhirSchemaProvider from package resources
4. **CompositeSchemaProvider** - Chain multiple providers (IGs override core)
5. **ImplementationGuideResolver** - Resolve IGs from HTTP headers/tenant config

**Pros:**
- ✅ Dynamic - load IGs without rebuild
- ✅ Tenant-specific IGs (Tenant A uses US Core, Tenant B uses IPS)
- ✅ Supports private/custom IGs from Simplifier
- ✅ Can update IGs via API (admin endpoint)
- ✅ Efficient caching (load once, reuse across requests)
- ✅ Header-based IG resolution (X-FHIR-Profile, Accept)

**Cons:**
- ❌ Runtime overhead (first load ~500ms-2s per IG)
- ❌ Memory overhead (each IG ~5-15 MB in memory)
- ❌ Complexity (package management, caching, versioning)
- ❌ Requires storage for cached packages
- ❌ Dependency resolution (transitive dependencies)

**Best for:**
- Multi-tenant SaaS deployments
- Flexible IG requirements
- Dynamic profile selection (header-based routing)
- Private/custom IGs

**Effort:** 160 hours (8 weeks, see phased implementation plan)

---

### Option C: Hybrid Approach ⭐ **RECOMMENDED**

**Combine build-time + runtime:**

**Build-time (codegen):**
```bash
# Generate providers for FHIR core + most common IGs
./generate.ps1 -FhirVersion R4,R4B,R5
./generate.ps1 -FhirVersion R4 -IG "hl7.fhir.us.core#6.1.0"  # For US deployments
./generate.ps1 -FhirVersion R4 -IG "hl7.fhir.uv.ips#1.1.0"   # For international
```

**Runtime (dynamic):**
```csharp
// Load additional IGs on-demand
{
  "TenantId": 1,
  "PreloadedIGs": ["us-core"],  // Use generated provider
  "DynamicIGs": [
    "hl7.fhir.us.carin-bb#2.0.0",     // Load at runtime
    "my.custom.profiles#1.0.0"         // Private from Simplifier
  ]
}
```

**Resolution Strategy:**
```csharp
public class HybridSchemaProvider : IFhirSchemaProvider
{
    private readonly IFhirSchemaProvider _generatedCore;      // R4CoreProvider
    private readonly IFhirSchemaProvider? _generatedUSCore;   // Optional (if built)
    private readonly List<PackageSchemaProvider> _dynamicIGs; // Runtime-loaded

    public IStructureDefinitionSummary? Provide(string canonical)
    {
        // 1. Try dynamic IGs first (tenant-specific)
        foreach (var ig in _dynamicIGs)
        {
            var summary = ig.Provide(canonical);
            if (summary != null) return summary;
        }

        // 2. Try preloaded IG (US Core)
        if (_generatedUSCore != null)
        {
            var summary = _generatedUSCore.Provide(canonical);
            if (summary != null) return summary;
        }

        // 3. Fallback to core
        return _generatedCore.Provide(canonical);
    }
}
```

**Benefits:**
- ✅ Fast common paths (US Core preloaded, zero overhead)
- ✅ Flexible for custom scenarios (runtime loading)
- ✅ Smaller binary than "all codegen" (only common IGs)
- ✅ Lower runtime overhead than "all dynamic" (90% preloaded)
- ✅ Best performance/flexibility tradeoff

**Typical Configuration:**
```
Build-time (generated):
- FHIR Core (R4, R4B, R5, STU3) - always
- US Core 6.1.0 (for R4) - US deployments
- IPS 1.1.0 (for R4) - international deployments

Runtime (dynamic):
- Custom profiles per tenant (10% of cases)
- Private IGs from Simplifier
- Version-specific IGs (US Core 5.0 vs 6.1)
- Emerging IGs (early adoption, USCDI v4)
```

**Effort:** 80 hours (build-time extension: 16h + runtime foundation: 64h)

## Technical Implementation

### Approach: Hybrid (Recommended)

This section details the hybrid implementation strategy.

### Phase 1: Build-Time Extension (Week 1, 16 hours)

**Goal:** Generate providers for US Core alongside core FHIR

**Tasks:**

1. **Extend codegen script** (`codegen/generate.ps1`):
   ```powershell
   param(
       [string]$FhirVersion = "All",
       [string[]]$ImplementationGuides = @()
   )

   # Generate core
   dotnet run --project fhir-codegen/src/fhir-codegen/fhir-codegen.csproj -- `
       generate structure $FhirVersion ../src/Ignixa.Specification/Generated

   # Generate IGs
   foreach ($ig in $ImplementationGuides) {
       dotnet run --project fhir-codegen/src/fhir-codegen/fhir-codegen.csproj -- `
           generate structure $ig ../src/Ignixa.Specification/Generated
   }
   ```

2. **Enhance `CSharpStructureProviderLanguage.cs`**:
   - Add IG-specific namespace (e.g., `USCoreStructureDefinitionSummaryProvider`)
   - Handle profile inheritance (base profiles from core)
   - Generate composite providers

3. **Test with US Core:**
   ```bash
   cd codegen
   ./generate.ps1 -FhirVersion R4 -ImplementationGuides "hl7.fhir.us.core#6.1.0"
   ```

   Expected output:
   - `R4StructureDefinitionSummaryProvider.g.cs` (~7.2 MB)
   - `USCoreStructureDefinitionSummaryProvider.g.cs` (~2.1 MB, 114 profiles)

**Deliverables:**
- ✅ Can generate IG providers at build time
- ✅ US Core provider validates resources correctly
- ✅ Composite provider chains IG + core

---

### Phase 2: Runtime Foundation (Weeks 2-3, 40 hours)

**Goal:** Load a single package at runtime and validate against it

**New Project:** `src/Ignixa.Specification.Packages/`

**Key Classes:**

**1. FhirPackageLoader.cs**
```csharp
public class FhirPackageLoader
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FhirPackageLoader> _logger;

    public async Task<FhirPackage> LoadAsync(
        string packageName,
        string version,
        string source = "packages.fhir.org",
        CancellationToken cancellationToken = default)
    {
        // Download .tgz from registry
        var url = BuildPackageUrl(source, packageName, version);
        var tgzBytes = await _httpClient.GetByteArrayAsync(url, cancellationToken);

        // Extract to temp directory
        var extractPath = await ExtractPackageAsync(tgzBytes, cancellationToken);

        // Parse package.json
        var manifest = await ParseManifestAsync(extractPath, cancellationToken);

        // Load all StructureDefinitions
        var profiles = await LoadProfilesAsync(extractPath, cancellationToken);

        return new FhirPackage(manifest, profiles);
    }

    private string BuildPackageUrl(string source, string name, string version)
    {
        return source.ToLowerInvariant() switch
        {
            "packages.fhir.org" => $"https://packages.fhir.org/{name}/-/{name}-{version}.tgz",
            "simplifier.net" => $"https://packages.simplifier.net/{name}/-/{name}-{version}.tgz",
            _ => throw new ArgumentException($"Unknown package source: {source}")
        };
    }

    private async Task<string> ExtractPackageAsync(byte[] tgzBytes, CancellationToken ct)
    {
        var extractPath = Path.Combine(Path.GetTempPath(), $"fhir-pkg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractPath);

        using var tgzStream = new MemoryStream(tgzBytes);
        using var gzipStream = new GZipStream(tgzStream, CompressionMode.Decompress);
        using var tarReader = new TarReader(gzipStream);

        while (await tarReader.GetNextEntryAsync(ct) is TarEntry entry)
        {
            if (entry.EntryType == TarEntryType.RegularFile)
            {
                var filePath = Path.Combine(extractPath, entry.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                await using var fileStream = File.Create(filePath);
                await entry.DataStream!.CopyToAsync(fileStream, ct);
            }
        }

        return extractPath;
    }

    private async Task<FhirPackageManifest> ParseManifestAsync(string extractPath, CancellationToken ct)
    {
        var manifestPath = Path.Combine(extractPath, "package", "package.json");
        var json = await File.ReadAllTextAsync(manifestPath, ct);
        return JsonSerializer.Deserialize<FhirPackageManifest>(json)
            ?? throw new InvalidOperationException("Failed to parse package.json");
    }

    private async Task<List<FhirProfile>> LoadProfilesAsync(string extractPath, CancellationToken ct)
    {
        var profiles = new List<FhirProfile>();
        var packageDir = Path.Combine(extractPath, "package");

        foreach (var file in Directory.GetFiles(packageDir, "StructureDefinition-*.json"))
        {
            var json = await File.ReadAllTextAsync(file, ct);
            var sourceNode = FhirJsonNode.Parse(json);

            // Extract canonical URL
            var urlNode = sourceNode.Children("url").FirstOrDefault();
            if (urlNode?.Text is string url)
            {
                profiles.Add(new FhirProfile(url, json, sourceNode));
            }
        }

        return profiles;
    }
}

public record FhirPackageManifest(
    string Name,
    string Version,
    string Title,
    string Canonical,
    string[] FhirVersions,
    Dictionary<string, string> Dependencies,
    string? Author,
    string? Description);

public record FhirProfile(
    string Url,
    string Json,
    ISourceNode SourceNode);

public record FhirPackage(
    FhirPackageManifest Manifest,
    List<FhirProfile> Profiles);
```

**2. PackageSchemaProvider.cs**
```csharp
public class PackageSchemaProvider : IFhirSchemaProvider
{
    private readonly FhirPackage _package;
    private readonly ConcurrentDictionary<string, IStructureDefinitionSummary?> _cache = new();
    private readonly ILogger<PackageSchemaProvider> _logger;

    public PackageSchemaProvider(FhirPackage package, FhirSpecification version, ILogger<PackageSchemaProvider> logger)
    {
        _package = package;
        Version = version;
        _logger = logger;

        // Build resource type names
        ResourceTypeNames = package.Profiles
            .Select(p => ExtractResourceType(p.SourceNode))
            .Where(rt => rt != null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public FhirSpecification Version { get; }
    public IReadOnlySet<string> ResourceTypeNames { get; }
    public string FullVersion => _package.Manifest.FhirVersions.FirstOrDefault() ?? "4.0.1";

    public IStructureDefinitionSummary? Provide(string canonical)
    {
        return _cache.GetOrAdd(canonical, BuildSummary);
    }

    private IStructureDefinitionSummary? BuildSummary(string canonical)
    {
        var profile = _package.Profiles.FirstOrDefault(p => p.Url == canonical);
        if (profile == null)
        {
            _logger.LogDebug("Profile not found in package: {Canonical}", canonical);
            return null;
        }

        // Convert StructureDefinition ISourceNode to IStructureDefinitionSummary
        return ConvertToSummary(profile.SourceNode);
    }

    private IStructureDefinitionSummary ConvertToSummary(ISourceNode structureDefinition)
    {
        // Extract metadata
        var name = structureDefinition.Children("name").FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("StructureDefinition missing name");
        var type = structureDefinition.Children("type").FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("StructureDefinition missing type");
        var url = structureDefinition.Children("url").FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("StructureDefinition missing url");
        var kind = structureDefinition.Children("kind").FirstOrDefault()?.Text;
        var isAbstract = structureDefinition.Children("abstract").FirstOrDefault()?.Text == "true";

        // Extract elements
        var snapshotNode = structureDefinition.Children("snapshot").FirstOrDefault();
        var elementNodes = snapshotNode?.Children("element").ToList() ?? new List<ISourceNode>();

        var elements = elementNodes.Select(ConvertToElementSummary).ToList();

        return new RuntimeStructureDefinitionSummary(name, type, url, kind == "resource", isAbstract, elements);
    }

    private IElementDefinitionSummary ConvertToElementSummary(ISourceNode element)
    {
        var path = element.Children("path").FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("ElementDefinition missing path");
        var min = int.Parse(element.Children("min").FirstOrDefault()?.Text ?? "0");
        var max = element.Children("max").FirstOrDefault()?.Text ?? "1";

        // Extract types
        var typeNodes = element.Children("type").ToList();
        var types = typeNodes
            .Select(t => t.Children("code").FirstOrDefault()?.Text)
            .Where(code => code != null)
            .Cast<string>()
            .ToArray();

        // Extract other metadata
        var isSummary = element.Children("isSummary").FirstOrDefault()?.Text == "true";
        var isModifier = element.Children("isModifier").FirstOrDefault()?.Text == "true";

        return new RuntimeElementDefinitionSummary(
            path,
            min,
            max,
            types,
            isSummary,
            isModifier);
    }

    private static string? ExtractResourceType(ISourceNode structureDefinition)
    {
        return structureDefinition.Children("type").FirstOrDefault()?.Text;
    }
}

// Runtime implementations (in-memory, not generated)
internal record RuntimeStructureDefinitionSummary(
    string TypeName,
    string Type,
    string Url,
    bool IsResource,
    bool IsAbstract,
    IReadOnlyCollection<IElementDefinitionSummary> Elements) : IStructureDefinitionSummary
{
    public IReadOnlyCollection<IElementDefinitionSummary> GetElements() => Elements;
}

internal record RuntimeElementDefinitionSummary(
    string Path,
    int Min,
    string Max,
    string[] Types,
    bool InSummary,
    bool IsModifier) : IElementDefinitionSummary
{
    public bool IsRequired => Min >= 1;
    public bool IsCollection => Max != "1";
    public string ElementName => Path.Contains('.') ? Path.Split('.')[^1] : Path;
    public string? DefaultValue => null;
    public string? ReferredType => Types.Length == 1 ? Types[0] : null;
    // ... additional properties as needed
}
```

**3. CompositeSchemaProvider.cs**
```csharp
public class CompositeSchemaProvider : IFhirSchemaProvider
{
    private readonly List<IFhirSchemaProvider> _providers;

    public CompositeSchemaProvider(params IFhirSchemaProvider[] providers)
    {
        _providers = new List<IFhirSchemaProvider>(providers);
        Version = providers.FirstOrDefault()?.Version ?? FhirSpecification.R4;

        // Union of all resource type names
        ResourceTypeNames = providers
            .SelectMany(p => p.ResourceTypeNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public FhirSpecification Version { get; }
    public IReadOnlySet<string> ResourceTypeNames { get; }
    public string FullVersion => _providers.FirstOrDefault()?.FullVersion ?? "4.0.1";

    public IStructureDefinitionSummary? Provide(string canonical)
    {
        // Try each provider in order (IGs before core)
        foreach (var provider in _providers)
        {
            var summary = provider.Provide(canonical);
            if (summary != null)
            {
                return summary;
            }
        }

        return null;
    }

    public void AddProvider(IFhirSchemaProvider provider)
    {
        _providers.Insert(0, provider); // IGs have priority
    }
}
```

**4. POC Test:**
```csharp
public class FhirPackageIntegrationTests
{
    private readonly FhirPackageLoader _loader;
    private readonly ILogger<PackageSchemaProvider> _logger;

    [Fact]
    public async Task LoadUSCorePackage_AndValidatePatient()
    {
        // Load US Core package
        var package = await _loader.LoadAsync("hl7.fhir.us.core", "6.1.0");

        package.Manifest.Name.Should().Be("hl7.fhir.us.core");
        package.Manifest.Version.Should().Be("6.1.0");
        package.Profiles.Should().HaveCountGreaterThan(100); // US Core has 114 profiles

        // Create providers
        var coreProvider = new R4StructureDefinitionSummaryProvider();
        var usCoreProvider = new PackageSchemaProvider(package, FhirSpecification.R4, _logger);
        var composite = new CompositeSchemaProvider(usCoreProvider, coreProvider);

        // Resolve US Core Patient profile
        var usCorePatientUrl = "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient";
        var summary = composite.Provide(usCorePatientUrl);

        summary.Should().NotBeNull();
        summary!.TypeName.Should().Be("USCorePatientProfile");
        summary.Type.Should().Be("Patient");

        // Validate a resource against US Core Patient
        var resolver = new StructureDefinitionSchemaResolver(composite);
        var schema = resolver.GetSchema(usCorePatientUrl);

        var patientJson = """
        {
          "resourceType": "Patient",
          "id": "example",
          "meta": {
            "profile": ["http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"]
          },
          "identifier": [{
            "system": "http://example.org/mrn",
            "value": "12345"
          }],
          "name": [{
            "family": "Doe",
            "given": ["John"]
          }],
          "gender": "male"
        }
        """;

        var resource = FhirJsonNode.Parse(patientJson);
        var settings = new ValidationSettings { Tier = ValidationTier.Profile };
        var state = new ValidationState();

        var result = schema.Validate(resource, settings, state);

        result.IsValid.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task CompositeProvider_IGOverridesCore()
    {
        var package = await _loader.LoadAsync("hl7.fhir.us.core", "6.1.0");
        var coreProvider = new R4StructureDefinitionSummaryProvider();
        var usCoreProvider = new PackageSchemaProvider(package, FhirSpecification.R4, _logger);
        var composite = new CompositeSchemaProvider(usCoreProvider, coreProvider);

        // US Core Patient should come from IG
        var usCorePatient = composite.Provide("http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient");
        usCorePatient.Should().NotBeNull();

        // Core Patient should come from core provider
        var corePatient = composite.Provide("http://hl7.org/fhir/StructureDefinition/Patient");
        corePatient.Should().NotBeNull();
        corePatient!.TypeName.Should().Be("Patient");
    }
}
```

**Deliverables:**
- ✅ Can download US Core package from packages.fhir.org
- ✅ Can extract and parse StructureDefinitions
- ✅ Can validate a resource against a US Core profile
- ✅ Composite provider resolution works correctly

---

### Phase 3: Package Cache (Week 4, 24 hours)

**Goal:** Persistent package cache to avoid re-downloading

**Implementation:**

**FhirPackageCache.cs**
```csharp
public class FhirPackageCache : IFhirPackageCache
{
    private readonly FhirPackageLoader _loader;
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, FhirPackage> _memoryCache = new();
    private readonly ILogger<FhirPackageCache> _logger;

    public FhirPackageCache(FhirPackageLoader loader, IOptions<FhirPackageOptions> options, ILogger<FhirPackageCache> logger)
    {
        _loader = loader;
        _logger = logger;
        _cacheDirectory = options.Value.CacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".fhir",
            "packages");

        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<FhirPackage> GetAsync(
        string packageName,
        string version,
        string source = "packages.fhir.org",
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{packageName}@{version}";

        // 1. Try memory cache
        if (_memoryCache.TryGetValue(cacheKey, out var cached))
        {
            _logger.LogDebug("Package found in memory cache: {Package}", cacheKey);
            return cached;
        }

        // 2. Try disk cache
        var packageDir = Path.Combine(_cacheDirectory, $"{packageName}#{version}");
        if (Directory.Exists(packageDir))
        {
            _logger.LogInformation("Loading package from disk cache: {Package}", cacheKey);
            var package = await LoadFromDiskAsync(packageDir, cancellationToken);
            _memoryCache[cacheKey] = package;
            return package;
        }

        // 3. Download and cache
        _logger.LogInformation("Downloading package: {Package} from {Source}", cacheKey, source);
        var downloadedPackage = await _loader.LoadAsync(packageName, version, source, cancellationToken);

        // Save to disk
        await SaveToDiskAsync(downloadedPackage, packageDir, cancellationToken);

        // Save to memory
        _memoryCache[cacheKey] = downloadedPackage;

        return downloadedPackage;
    }

    private async Task<FhirPackage> LoadFromDiskAsync(string packageDir, CancellationToken ct)
    {
        var manifestPath = Path.Combine(packageDir, "package.json");
        var manifestJson = await File.ReadAllTextAsync(manifestPath, ct);
        var manifest = JsonSerializer.Deserialize<FhirPackageManifest>(manifestJson)!;

        var profiles = new List<FhirProfile>();
        foreach (var file in Directory.GetFiles(packageDir, "StructureDefinition-*.json"))
        {
            var json = await File.ReadAllTextAsync(file, ct);
            var sourceNode = FhirJsonNode.Parse(json);
            var url = sourceNode.Children("url").FirstOrDefault()?.Text;
            if (url != null)
            {
                profiles.Add(new FhirProfile(url, json, sourceNode));
            }
        }

        return new FhirPackage(manifest, profiles);
    }

    private async Task SaveToDiskAsync(FhirPackage package, string packageDir, CancellationToken ct)
    {
        Directory.CreateDirectory(packageDir);

        // Save manifest
        var manifestPath = Path.Combine(packageDir, "package.json");
        var manifestJson = JsonSerializer.Serialize(package.Manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, manifestJson, ct);

        // Save profiles
        foreach (var profile in package.Profiles)
        {
            var fileName = $"StructureDefinition-{SanitizeFileName(profile.Url)}.json";
            var filePath = Path.Combine(packageDir, fileName);
            await File.WriteAllTextAsync(filePath, profile.Json, ct);
        }
    }

    private static string SanitizeFileName(string url)
    {
        return url.Replace("http://", "").Replace("https://", "").Replace("/", "_");
    }
}

public class FhirPackageOptions
{
    public string? CacheDirectory { get; set; }
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromDays(30);
}

public interface IFhirPackageCache
{
    Task<FhirPackage> GetAsync(string packageName, string version, string source = "packages.fhir.org", CancellationToken cancellationToken = default);
}
```

**DI Registration** (`Program.cs`):
```csharp
// Options
builder.Services.Configure<FhirPackageOptions>(builder.Configuration.GetSection("FhirPackages"));

// Loader and cache
builder.Services.AddHttpClient<FhirPackageLoader>();
builder.Services.AddSingleton<IFhirPackageCache, FhirPackageCache>();
```

**appsettings.json**:
```json
{
  "FhirPackages": {
    "CacheDirectory": "~/.fhir/packages",
    "CacheExpiration": "30.00:00:00"
  }
}
```

**Deliverables:**
- ✅ Packages cached on disk (~/.fhir/packages/)
- ✅ Subsequent loads read from cache (fast)
- ✅ Memory cache for hot packages

---

### Phase 4: Tenant Configuration (Week 5, 24 hours)

**Goal:** Per-tenant IG configuration

**Update TenantConfiguration:**
```csharp
public record TenantConfiguration
{
    public int TenantId { get; init; }
    public string FhirVersion { get; init; } = "4.0";
    public string ValidationTier { get; init; } = "Spec";

    // NEW: Implementation Guide configuration
    public List<ImplementationGuideConfig> ImplementationGuides { get; init; } = new();
}

public record ImplementationGuideConfig
{
    public required string PackageName { get; init; }
    public required string Version { get; init; }
    public string Source { get; init; } = "packages.fhir.org";
    public string? ApiKey { get; init; } // For private packages on Simplifier
}
```

**appsettings.json**:
```json
{
  "Tenants": {
    "Configurations": [
      {
        "TenantId": 1,
        "FhirVersion": "4.0",
        "ValidationTier": "Profile",
        "ImplementationGuides": [
          {
            "PackageName": "hl7.fhir.us.core",
            "Version": "6.1.0",
            "Source": "packages.fhir.org"
          },
          {
            "PackageName": "hl7.fhir.us.carin-bb",
            "Version": "2.0.0",
            "Source": "packages.fhir.org"
          }
        ]
      },
      {
        "TenantId": 2,
        "FhirVersion": "4.0",
        "ValidationTier": "Profile",
        "ImplementationGuides": [
          {
            "PackageName": "hl7.fhir.uv.ips",
            "Version": "1.1.0",
            "Source": "packages.fhir.org"
          }
        ]
      }
    ]
  }
}
```

**Update Schema Resolver Factory:**
```csharp
// Program.cs
containerBuilder.Register<Func<FhirSpecification, string, IValidationSchemaResolver>>(c =>
{
    var versionContext = c.Resolve<IFhirVersionContext>();
    var builder = c.Resolve<StructureDefinitionSchemaBuilder>();
    var packageCache = c.Resolve<IFhirPackageCache>();
    var tenantConfigProvider = c.Resolve<ITenantConfigurationProvider>();

    return async (version, tenantId) =>
    {
        // Get core provider (generated)
        var coreProvider = versionContext.GetSchemaProvider(version);

        // Get tenant configuration
        var tenantConfig = await tenantConfigProvider.GetConfigurationAsync(tenantId);

        // Load tenant IGs
        var providers = new List<IFhirSchemaProvider>();
        foreach (var igConfig in tenantConfig.ImplementationGuides)
        {
            var package = await packageCache.GetAsync(
                igConfig.PackageName,
                igConfig.Version,
                igConfig.Source);

            var igProvider = new PackageSchemaProvider(package, version, logger);
            providers.Add(igProvider);
        }

        // Add core provider last (IGs override core)
        providers.Add(coreProvider);

        // Create composite
        var compositeProvider = new CompositeSchemaProvider(providers.ToArray());

        // Create resolver with caching
        var resolver = new StructureDefinitionSchemaResolver(compositeProvider, builder);
        return new CachedValidationSchemaResolver(resolver);
    };
})
.SingleInstance();
```

**Deliverables:**
- ✅ Tenant-specific IG configuration
- ✅ Different tenants can use different IGs
- ✅ Validation uses correct profiles per tenant

---

### Phase 5: Simplifier.net Support (Week 6, 16 hours)

**Goal:** Support private packages from Simplifier.net

**Extend FhirPackageLoader:**
```csharp
public class FhirPackageLoader
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FhirPackageLoader> _logger;

    public async Task<FhirPackage> LoadAsync(
        string packageName,
        string version,
        string source = "packages.fhir.org",
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        var url = BuildPackageUrl(source, packageName, version);

        // Add authentication for Simplifier.net private packages
        if (!string.IsNullOrEmpty(apiKey) && source.Contains("simplifier"))
        {
            var token = await GetSimplifierTokenAsync(apiKey, cancellationToken);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var tgzBytes = await _httpClient.GetByteArrayAsync(url, cancellationToken);

        // ... rest of implementation
    }

    private async Task<string> GetSimplifierTokenAsync(string apiKey, CancellationToken ct)
    {
        // Simplifier uses direct API key in Authorization header
        return apiKey;
    }
}
```

**Test:**
```csharp
[Fact]
public async Task LoadPrivatePackageFromSimplifier()
{
    var package = await _loader.LoadAsync(
        "my.org.custom.profiles",
        "1.0.0",
        source: "simplifier.net",
        apiKey: "test-api-key-123");

    package.Manifest.Name.Should().Be("my.org.custom.profiles");
}
```

**Deliverables:**
- ✅ Can load private packages from Simplifier.net
- ✅ API key authentication working

---

### Phase 6: Header-Based IG Resolution (Week 7, 24 hours)

**Goal:** Request-specific profile selection (as designed in existing `multi-version-ig-loading-system.md`)

**ImplementationGuideResolver.cs:**
```csharp
public interface IImplementationGuideResolver
{
    Task<IFhirSchemaProvider> ResolveAsync(
        string tenantId,
        FhirSpecification version,
        HttpRequest request,
        CancellationToken cancellationToken = default);
}

public class HeaderBasedIGResolver : IImplementationGuideResolver
{
    private readonly IFhirPackageCache _packageCache;
    private readonly ITenantConfigurationProvider _tenantConfig;
    private readonly IFhirVersionContext _versionContext;
    private readonly ILogger<HeaderBasedIGResolver> _logger;

    public async Task<IFhirSchemaProvider> ResolveAsync(
        string tenantId,
        FhirSpecification version,
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        var providers = new List<IFhirSchemaProvider>();

        // 1. Check X-FHIR-Profile header
        if (request.Headers.TryGetValue("X-FHIR-Profile", out var profileUrls))
        {
            foreach (var profileUrl in profileUrls)
            {
                var package = await ResolveProfileToPackageAsync(profileUrl!, cancellationToken);
                if (package != null)
                {
                    providers.Add(new PackageSchemaProvider(package, version, _logger));
                }
            }
        }

        // 2. Check Accept header for profile parameter
        if (request.Headers.TryGetValue("Accept", out var acceptHeaders))
        {
            var profilesFromAccept = ExtractProfilesFromAccept(acceptHeaders.ToString());
            foreach (var profileUrl in profilesFromAccept)
            {
                var package = await ResolveProfileToPackageAsync(profileUrl, cancellationToken);
                if (package != null)
                {
                    providers.Add(new PackageSchemaProvider(package, version, _logger));
                }
            }
        }

        // 3. Fallback to tenant defaults
        if (providers.Count == 0)
        {
            var tenantConfig = await _tenantConfig.GetConfigurationAsync(tenantId);
            foreach (var igConfig in tenantConfig.ImplementationGuides)
            {
                var package = await _packageCache.GetAsync(
                    igConfig.PackageName,
                    igConfig.Version,
                    igConfig.Source,
                    cancellationToken);

                providers.Add(new PackageSchemaProvider(package, version, _logger));
            }
        }

        // 4. Add core provider
        var coreProvider = _versionContext.GetSchemaProvider(version);
        providers.Add(coreProvider);

        return new CompositeSchemaProvider(providers.ToArray());
    }

    private async Task<FhirPackage?> ResolveProfileToPackageAsync(string profileUrl, CancellationToken ct)
    {
        // Parse canonical URL to extract IG
        // Example: http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient
        //          -> hl7.fhir.us.core

        if (profileUrl.Contains("/us/core/"))
        {
            return await _packageCache.GetAsync("hl7.fhir.us.core", "6.1.0", cancellationToken: ct);
        }

        if (profileUrl.Contains("/uv/ips/"))
        {
            return await _packageCache.GetAsync("hl7.fhir.uv.ips", "1.1.0", cancellationToken: ct);
        }

        // TODO: Build proper IG registry/mapping
        _logger.LogWarning("Could not resolve profile URL to package: {ProfileUrl}", profileUrl);
        return null;
    }

    private static IEnumerable<string> ExtractProfilesFromAccept(string acceptHeader)
    {
        // Parse: application/fhir+json;profile="http://..."
        var match = Regex.Match(acceptHeader, @"profile\s*=\s*""([^""]+)""");
        if (match.Success)
        {
            yield return match.Groups[1].Value;
        }
    }
}
```

**Update ValidationBehavior:**
```csharp
public class ValidationBehavior : IPipelineBehavior<CreateOrUpdateResourceCommand, ResourceKey>
{
    private readonly IImplementationGuideResolver _igResolver; // NEW
    private readonly IHttpContextAccessor _httpContextAccessor; // NEW

    public async Task<ResourceKey> HandleAsync(
        CreateOrUpdateResourceCommand request,
        CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext!;
        var tenantId = httpContext.GetTenantId();
        var fhirVersion = httpContext.GetFhirVersion();

        // Resolve IGs from headers
        var schemaProvider = await _igResolver.ResolveAsync(tenantId, fhirVersion, httpContext.Request, cancellationToken);

        // Build schema and validate
        var resolver = new StructureDefinitionSchemaResolver(schemaProvider, _schemaBuilder);
        var schema = resolver.GetSchema(request.Resource.ResourceType);

        var result = schema.Validate(request.Resource, settings, state);

        if (!result.IsValid)
        {
            throw new ValidationException(result);
        }

        return await _next.HandleAsync(request, cancellationToken);
    }
}
```

**Example requests:**
```http
# Request with explicit profile
POST /Patient
X-FHIR-Profile: http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient
Content-Type: application/fhir+json

{
  "resourceType": "Patient",
  "meta": {
    "profile": ["http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"]
  },
  ...
}

# Request with Accept header
POST /Patient
Accept: application/fhir+json;profile="http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"
Content-Type: application/fhir+json

{...}

# Request using tenant defaults
POST /Patient
Content-Type: application/fhir+json

{...}  # Uses tenant's configured IGs
```

**Deliverables:**
- ✅ Header-based IG resolution working
- ✅ Fallback to tenant defaults
- ✅ Caching for performance

---

### Phase 7: Dependency Resolution (Week 8, 16 hours)

**Goal:** Automatically load package dependencies

**Extend FhirPackageCache:**
```csharp
public async Task<List<FhirPackage>> GetWithDependenciesAsync(
    string packageName,
    string version,
    string source = "packages.fhir.org",
    CancellationToken cancellationToken = default)
{
    var packages = new List<FhirPackage>();
    var processed = new HashSet<string>();
    var queue = new Queue<(string Name, string Version, string Source)>();

    queue.Enqueue((packageName, version, source));

    while (queue.Count > 0)
    {
        var (name, ver, src) = queue.Dequeue();
        var key = $"{name}@{ver}";

        if (processed.Contains(key))
            continue;

        var package = await GetAsync(name, ver, src, cancellationToken);
        packages.Add(package);
        processed.Add(key);

        // Queue dependencies
        foreach (var (depName, depVersion) in package.Manifest.Dependencies)
        {
            // Skip core FHIR packages (we have generated providers)
            if (depName.StartsWith("hl7.fhir.r") && depName.EndsWith(".core"))
                continue;

            queue.Enqueue((depName, depVersion, src));
        }
    }

    return packages;
}
```

**Test:**
```csharp
[Fact]
public async Task LoadUSCore_LoadsDependencies()
{
    var packages = await _cache.GetWithDependenciesAsync("hl7.fhir.us.core", "6.1.0");

    packages.Should().Contain(p => p.Manifest.Name == "hl7.fhir.us.core");
    packages.Should().Contain(p => p.Manifest.Name == "us.nlm.vsac"); // ValueSet dependency
}
```

**Deliverables:**
- ✅ Automatic dependency resolution
- ✅ Transitive dependencies loaded
- ✅ Circular dependency detection

---

## Performance Analysis

### Package Loading Performance

**Benchmarks (estimated from similar implementations):**

| Operation | First Load | Cached (Disk) | Cached (Memory) |
|-----------|-----------|---------------|-----------------|
| Download US Core (4 MB) | 200ms | - | - |
| Extract .tgz | 100ms | - | - |
| Parse 114 StructureDefinitions | 500ms | 300ms | - |
| Build summaries | 300ms | 150ms | - |
| **Total** | **~1100ms** | **~450ms** | **<1ms** |

**Optimization Strategies:**
1. **Lazy loading** - Only parse profiles when first accessed
2. **Persistent cache** - Store parsed summaries on disk (~/.fhir/packages/)
3. **Preloading** - Load common IGs at startup
4. **Memory cache** - Keep hot packages in memory

### Memory Usage

**Per-package memory overhead:**
- US Core (114 profiles): ~15 MB
- IPS (50 profiles): ~7 MB
- Core R4 (generated): ~20 MB

**Typical 3-IG scenario:** ~42 MB (acceptable for modern servers)

### Validation Performance Impact

**Current (core only):**
- Fast tier: <25ms
- Spec tier: <200ms
- Profile tier: <1000ms

**With US Core loaded:**
- Fast tier: <25ms (no change)
- Spec tier: <200ms (no change)
- Profile tier: <1200ms (+20% due to more complex profiles)

**Still well under 5s target** ✅

## Alignment with Existing Architecture

### Layer Dependency Rules (CLAUDE.md Compliance)

✅ **No Hl7.Fhir.* dependencies in Application/DataLayer**
- `Ignixa.Specification.Packages` is Domain layer
- Uses only `IStructureDefinitionSummaryProvider` abstraction
- No Firely SDK POCOs

✅ **Architecture patterns**
- Three-layer HTTP stack (Endpoints → Handlers → Domain)
- `CancellationToken` everywhere
- Immutable records

✅ **Code quality**
- One type per file
- AAA test pattern
- Async/await

### ADR-2500 Roadmap Alignment

**Current phase:** Phase 22 (FHIR _history) ✅

**Recommended placement:** **Phase 3.5** (between Validation and Advanced Search)

**Rationale:**
- Builds on Phase 3 (Validation system)
- Enables Profile-tier validation with real IGs
- Independent of Advanced Search

### Changes Required to Existing Code

**Minimal changes:**

1. **`ValidationBehavior.cs`** - Use `IImplementationGuideResolver` for schema provider
2. **`TenantConfiguration.cs`** - Add `ImplementationGuides` property
3. **`Program.cs`** - Register package services

**New projects:**
1. `Ignixa.Specification.Packages` - Package loading infrastructure

**No breaking changes** ✅

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| **Package download failures** | Medium | High | Retry logic (3x with backoff), fallback to Simplifier, local cache |
| **Malformed packages** | Low | Medium | Validation on load, error handling, graceful degradation to core |
| **Memory growth** | Medium | Medium | Bounded cache size (LRU eviction), configurable limits |
| **Performance degradation** | Low | Medium | Lazy loading, preloading common IGs, benchmarking |
| **Dependency conflicts** | Medium | Low | Dependency resolution algorithm, version pinning |
| **Breaking changes in IGs** | Low | High | Version locking per tenant, migration tools, changelog monitoring |
| **Network issues** | Medium | Medium | Persistent cache, offline mode (fail gracefully) |

## Example Use Cases

### Use Case 1: US Healthcare Provider

**Tenant Configuration:**
```json
{
  "TenantId": 1,
  "ImplementationGuides": [
    {
      "PackageName": "hl7.fhir.us.core",
      "Version": "6.1.0"
    },
    {
      "PackageName": "hl7.fhir.us.davinci-pdex",
      "Version": "2.0.0"
    },
    {
      "PackageName": "hl7.fhir.us.carin-bb",
      "Version": "2.0.0"
    }
  ]
}
```

**Benefit:** Validate all resources against US regulatory profiles (USCDI v3)

---

### Use Case 2: International Deployment

**Tenant Configuration:**
```json
{
  "TenantId": 2,
  "ImplementationGuides": [
    {
      "PackageName": "hl7.fhir.uv.ips",
      "Version": "1.1.0"
    },
    {
      "PackageName": "ihe.fhir.iti",
      "Version": "1.0.0"
    }
  ]
}
```

**Benefit:** Support global health data exchange (WHO IPS standard)

---

### Use Case 3: Research Organization (Custom Profiles)

**Tenant Configuration:**
```json
{
  "TenantId": 3,
  "ImplementationGuides": [
    {
      "PackageName": "org.example.research.oncology",
      "Version": "2.1.0",
      "Source": "simplifier.net",
      "ApiKey": "private-key-123"
    }
  ]
}
```

**Benefit:** Validate against custom research profiles (private IG on Simplifier)

---

### Use Case 4: Cross-Version Migration

**Tenant Configuration:**
```json
{
  "TenantId": 4,
  "ImplementationGuides": [
    {
      "PackageName": "hl7.fhir.uv.xver.r4-r5",
      "Version": "0.1.0"
    }
  ]
}
```

**Request:**
```http
POST /Patient/$transform
Content-Type: application/fhir+json
X-FHIR-StructureMap: http://hl7.org/fhir/uv/xver/StructureMap/Patient4to5

{
  "resourceType": "Patient",
  "id": "example",
  ...
}
```

**Response:** R5 Patient resource

**Benefit:** Automated cross-version conversion using HL7-provided StructureMaps

## Open Questions

1. **Approach:** Hybrid or pure runtime? *(Hybrid recommended)*
2. **SDK:** Firely.Fhir.Packages or custom implementation? *(Custom recommended for architecture alignment)*
3. **Default IGs:** Preload US Core, IPS, both, or neither? *(Recommend US Core for US deployments)*
4. **Tenant isolation:** Share packages across tenants or fully isolate? *(Share cache, separate providers per tenant)*
5. **Admin API:** API endpoints to manage IGs, or config-only? *(Config-only for MVP, API in Phase 2)*
6. **Validation tier:** IG validation in Spec or Profile tier? *(Profile tier - aligns with current architecture)*
7. **Caching:** Redis or file system? *(File system for MVP, Redis for distributed deployments)*
8. **Priority:** Phase 3.5 (next) or defer? *(Recommend Phase 3.5 - completes validation story)*

## Recommendations

### Recommended Approach: Hybrid

**Phase 1 (Immediate):** Build-time codegen for US Core
- Extend `generate.ps1` to support IGs
- Generate `USCoreStructureDefinitionSummaryProvider.g.cs`
- 16 hours effort

**Phase 2 (Next 2 months):** Runtime foundation
- Implement package loading infrastructure
- Support dynamic IG loading per tenant
- 160 hours effort (8 weeks @ 20 hours/week)

**Total Effort:** 176 hours (~1 engineer for 2 months @ half-time)

### Success Criteria

**Phase 1 (Build-time):**
- ✅ Can validate resources against US Core profiles
- ✅ No performance impact (<25ms for Fast tier)
- ✅ All tests passing

**Phase 2 (Runtime):**
- ✅ Can load IGs from packages.fhir.org and Simplifier.net
- ✅ Tenant-specific IG configuration working
- ✅ Header-based IG resolution working
- ✅ Validation against IG profiles working
- ✅ First load <2s, cached load <100ms
- ✅ Memory usage <100MB for typical 3-IG scenario

## Next Steps

1. **Review investigation** - Approve approach and scope
2. **Choose approach** - Hybrid vs pure runtime
3. **Approve Phase 1** - Build-time US Core codegen (16 hours)
4. **Schedule Phase 2** - Runtime foundation (160 hours over 8 weeks)
5. **Define success criteria** - Agree on performance/functionality targets

## Related Documentation

- **Existing:** `docs/investigations/multi-version-ig-loading-system.md` - Conceptual design (referenced heavily)
- **Existing:** `docs/investigations/ADR-2527-comprehensive-validation-system.md` - Validation architecture
- **Existing:** `codegen/README.md` - Code generation infrastructure
- **New:** This document - Package integration investigation

## References

- FHIR Package Specification: https://hl7.org/fhir/packages.html
- NPM Package Spec (HL7 Confluence): https://confluence.hl7.org/display/FHIR/NPM+Package+Specification
- Simplifier.net API Docs: https://docs.fire.ly/projects/Simplifier/features/api.html
- Firely.Fhir.Packages: https://www.nuget.org/packages/Firely.Fhir.Packages
- FHIR Package Registry: https://packages.fhir.org/
- US Core IG: http://hl7.org/fhir/us/core/
- IPS IG: http://hl7.org/fhir/uv/ips/

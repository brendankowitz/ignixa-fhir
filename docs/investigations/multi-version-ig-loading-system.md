# Multi-Version Implementation Guide Loading System

This document outlines the system for loading and resolving multiple versions of FHIR Implementation Guides (IGs) based on headers, resource definitions, and tenant configurations in the FHIR Server v2 architecture.

## Core IG Management Concepts

### Implementation Guide Versioning Strategy

FHIR Implementation Guides present unique challenges:
- **Multiple IG versions per FHIR version** (e.g., US Core 3.1.1, 4.0.0, 5.0.1 all for R4)
- **Profile versioning within IGs** (profiles can have independent versions)
- **Dependency chains** (IGs depend on other IGs and FHIR versions)
- **Runtime resolution** (client specifies which profiles to use via headers or meta.profile)

### IG Resolution Hierarchy

```
Tenant Context
├── Request Headers (X-FHIR-Profile, X-IG-Version)
├── Resource Meta.Profile URLs
├── Default IG Configuration per FHIR Version
└── Fallback to Base FHIR Specification
```

## Core Abstractions

### Implementation Guide Provider Interface

```csharp
public interface IImplementationGuideProvider
{
    /// <summary>
    /// Get available implementation guides for tenant and FHIR version
    /// </summary>
    ValueTask<IReadOnlyList<ImplementationGuideInfo>> GetAvailableGuidesAsync(
        string tenantId,
        FhirVersion fhirVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Load specific implementation guide
    /// </summary>
    ValueTask<ImplementationGuide> LoadGuideAsync(
        string tenantId,
        ImplementationGuideReference guideRef,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve profile from canonical URL
    /// </summary>
    ValueTask<StructureDefinition?> ResolveProfileAsync(
        string tenantId,
        string canonicalUrl,
        string? version = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all profiles for a resource type across loaded IGs
    /// </summary>
    ValueTask<IReadOnlyList<StructureDefinition>> GetProfilesForResourceAsync(
        string tenantId,
        string resourceType,
        FhirVersion fhirVersion,
        CancellationToken cancellationToken = default);
}

public interface IImplementationGuideResolver
{
    /// <summary>
    /// Resolve which IG versions to use for a request
    /// </summary>
    ValueTask<ImplementationGuideContext> ResolveContextAsync(
        string tenantId,
        FhirVersion fhirVersion,
        HttpRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve IG context from resource meta.profile
    /// </summary>
    ValueTask<ImplementationGuideContext> ResolveFromResourceAsync(
        string tenantId,
        ISourceNode resource,
        FhirVersion fhirVersion,
        CancellationToken cancellationToken = default);
}
```

### IG Data Models

```csharp
public record ImplementationGuideInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required FhirVersion FhirVersion { get; init; }
    public required Uri PackageUrl { get; init; }
    public required string Publisher { get; init; }
    public required DateTimeOffset PublishDate { get; init; }
    public required ImplementationGuideStatus Status { get; init; }
    public required IReadOnlyList<ImplementationGuideDependency> Dependencies { get; init; }
    public string? Description { get; init; }
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
}

public record ImplementationGuideDependency
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required DependencyType Type { get; init; }
    public string? Reason { get; init; }
}

public enum DependencyType
{
    Includes,
    Imports,
    References
}

public enum ImplementationGuideStatus
{
    Draft,
    Active,
    Retired,
    Unknown
}

public record ImplementationGuideReference
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required FhirVersion FhirVersion { get; init; }

    public static ImplementationGuideReference Parse(string reference)
    {
        // Parse formats like "us-core@4.0.0" or "us-core@4.0.0#R4"
        var parts = reference.Split('@');
        if (parts.Length != 2)
            throw new ArgumentException($"Invalid IG reference format: {reference}");

        var versionPart = parts[1];
        var fhirVersion = FhirVersion.R4; // Default

        if (versionPart.Contains('#'))
        {
            var versionAndFhir = versionPart.Split('#');
            versionPart = versionAndFhir[0];
            fhirVersion = Enum.Parse<FhirVersion>(versionAndFhir[1]);
        }

        return new ImplementationGuideReference
        {
            Id = parts[0],
            Version = versionPart,
            FhirVersion = fhirVersion
        };
    }

    public override string ToString() => $"{Id}@{Version}#{FhirVersion}";
}

public record ImplementationGuideContext
{
    public required string TenantId { get; init; }
    public required FhirVersion FhirVersion { get; init; }
    public required IReadOnlyList<ImplementationGuideReference> ActiveGuides { get; init; }
    public required IReadOnlyDictionary<string, StructureDefinition> LoadedProfiles { get; init; }
    public required IFhirSchemaProvider SchemaProvider { get; init; }
    public IReadOnlyDictionary<string, object>? ExtensionData { get; init; }
}
```

### IG Package Loading

```csharp
public interface IImplementationGuidePackageLoader
{
    /// <summary>
    /// Load IG package from URL or file system
    /// </summary>
    ValueTask<ImplementationGuidePackage> LoadPackageAsync(
        Uri packageSource,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extract resources from package
    /// </summary>
    ValueTask<IReadOnlyList<ISourceNode>> ExtractResourcesAsync(
        ImplementationGuidePackage package,
        string? resourceType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate package integrity
    /// </summary>
    ValueTask<PackageValidationResult> ValidatePackageAsync(
        ImplementationGuidePackage package,
        CancellationToken cancellationToken = default);
}

public record ImplementationGuidePackage
{
    public required ImplementationGuideInfo Info { get; init; }
    public required ReadOnlyMemory<byte> PackageData { get; init; }
    public required PackageFormat Format { get; init; }
    public required IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Resources { get; init; }
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
}

public enum PackageFormat
{
    NpmTgz,
    FhirPackage,
    Directory,
    Zip
}

public record PackageValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public IReadOnlyDictionary<string, object>? Details { get; init; }
}
```

## IG Resolution Implementation

### Header-Based IG Resolution

```csharp
public class HeaderBasedIGResolver : IImplementationGuideResolver
{
    private readonly IImplementationGuideProvider _igProvider;
    private readonly IFhirCacheService _cache;
    private readonly ILogger<HeaderBasedIGResolver> _logger;

    public HeaderBasedIGResolver(
        IImplementationGuideProvider igProvider,
        IFhirCacheService cache,
        ILogger<HeaderBasedIGResolver> logger)
    {
        _igProvider = igProvider;
        _cache = cache;
        _logger = logger;
    }

    public async ValueTask<ImplementationGuideContext> ResolveContextAsync(
        string tenantId,
        FhirVersion fhirVersion,
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        // Check cache first
        var cacheKey = BuildContextCacheKey(tenantId, fhirVersion, request);
        var cachedContext = await _cache.GetAsync<ImplementationGuideContext>(cacheKey, cancellationToken);
        if (cachedContext != null)
        {
            return cachedContext;
        }

        var activeGuides = new List<ImplementationGuideReference>();

        // 1. Check X-FHIR-Profile header for explicit profile requests
        if (request.Headers.TryGetValue("X-FHIR-Profile", out var profileHeaders))
        {
            foreach (var profileUrl in profileHeaders)
            {
                var guideRef = await ResolveProfileToGuideAsync(tenantId, profileUrl!, fhirVersion, cancellationToken);
                if (guideRef != null && !activeGuides.Contains(guideRef))
                {
                    activeGuides.Add(guideRef);
                }
            }
        }

        // 2. Check X-IG-Version header for explicit IG version requests
        if (request.Headers.TryGetValue("X-IG-Version", out var igHeaders))
        {
            foreach (var igRef in igHeaders)
            {
                try
                {
                    var parsed = ImplementationGuideReference.Parse(igRef!);
                    if (parsed.FhirVersion == fhirVersion && !activeGuides.Contains(parsed))
                    {
                        activeGuides.Add(parsed);
                    }
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning("Invalid IG reference in header: {Reference} - {Error}", igRef, ex.Message);
                }
            }
        }

        // 3. Check Accept header for profile preferences
        if (request.Headers.TryGetValue("Accept", out var acceptHeaders))
        {
            var profilesFromAccept = ExtractProfilesFromAcceptHeader(acceptHeaders.ToString());
            foreach (var profileUrl in profilesFromAccept)
            {
                var guideRef = await ResolveProfileToGuideAsync(tenantId, profileUrl, fhirVersion, cancellationToken);
                if (guideRef != null && !activeGuides.Contains(guideRef))
                {
                    activeGuides.Add(guideRef);
                }
            }
        }

        // 4. Fall back to tenant default IGs if none specified
        if (activeGuides.Count == 0)
        {
            var defaultGuides = await GetDefaultGuidesAsync(tenantId, fhirVersion, cancellationToken);
            activeGuides.AddRange(defaultGuides);
        }

        // Load and build context
        var context = await BuildContextAsync(tenantId, fhirVersion, activeGuides, cancellationToken);

        // Cache the context
        await _cache.SetAsync(cacheKey, context, TimeSpan.FromMinutes(30), cancellationToken);

        return context;
    }

    public async ValueTask<ImplementationGuideContext> ResolveFromResourceAsync(
        string tenantId,
        ISourceNode resource,
        FhirVersion fhirVersion,
        CancellationToken cancellationToken = default)
    {
        var activeGuides = new List<ImplementationGuideReference>();

        // Extract profile URLs from meta.profile
        var metaNode = resource.Children("meta").FirstOrDefault();
        if (metaNode != null)
        {
            var profileNodes = metaNode.Children("profile");
            foreach (var profileNode in profileNodes)
            {
                var profileUrl = profileNode.Text;
                if (!string.IsNullOrEmpty(profileUrl))
                {
                    var guideRef = await ResolveProfileToGuideAsync(tenantId, profileUrl, fhirVersion, cancellationToken);
                    if (guideRef != null && !activeGuides.Contains(guideRef))
                    {
                        activeGuides.Add(guideRef);
                    }
                }
            }
        }

        // Fall back to defaults if no profiles found
        if (activeGuides.Count == 0)
        {
            var defaultGuides = await GetDefaultGuidesAsync(tenantId, fhirVersion, cancellationToken);
            activeGuides.AddRange(defaultGuides);
        }

        return await BuildContextAsync(tenantId, fhirVersion, activeGuides, cancellationToken);
    }

    private async ValueTask<ImplementationGuideReference?> ResolveProfileToGuideAsync(
        string tenantId,
        string profileUrl,
        FhirVersion fhirVersion,
        CancellationToken cancellationToken)
    {
        // Parse canonical URL to extract IG information
        var canonical = CanonicalUrl.Parse(profileUrl);

        // Check if this is a known IG profile pattern
        var igId = ExtractIgIdFromCanonical(canonical.Url);
        if (string.IsNullOrEmpty(igId))
        {
            return null;
        }

        // Find matching IG version
        var availableGuides = await _igProvider.GetAvailableGuidesAsync(tenantId, fhirVersion, cancellationToken);
        var matchingGuide = availableGuides
            .Where(g => g.Id.Equals(igId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(g => g.Version)
            .FirstOrDefault();

        if (matchingGuide == null)
        {
            _logger.LogWarning("No implementation guide found for profile {ProfileUrl}", profileUrl);
            return null;
        }

        return new ImplementationGuideReference
        {
            Id = matchingGuide.Id,
            Version = matchingGuide.Version,
            FhirVersion = fhirVersion
        };
    }

    private async ValueTask<ImplementationGuideContext> BuildContextAsync(
        string tenantId,
        FhirVersion fhirVersion,
        IReadOnlyList<ImplementationGuideReference> guideRefs,
        CancellationToken cancellationToken)
    {
        var loadedProfiles = new Dictionary<string, StructureDefinition>();
        var loadedGuides = new List<ImplementationGuide>();

        // Load all requested IGs and their dependencies
        var guidesToLoad = new Queue<ImplementationGuideReference>(guideRefs);
        var processedGuides = new HashSet<string>();

        while (guidesToLoad.Count > 0)
        {
            var guideRef = guidesToLoad.Dequeue();
            var guideKey = $"{guideRef.Id}@{guideRef.Version}";

            if (processedGuides.Contains(guideKey))
                continue;

            try
            {
                var guide = await _igProvider.LoadGuideAsync(tenantId, guideRef, cancellationToken);
                loadedGuides.Add(guide);
                processedGuides.Add(guideKey);

                // Extract profiles from this IG
                foreach (var profile in guide.Definition?.Resource ?? Enumerable.Empty<ImplementationGuide.DefinitionResourceComponent>())
                {
                    if (profile.Reference?.Reference != null)
                    {
                        var structureDef = await _igProvider.ResolveProfileAsync(tenantId, profile.Reference.Reference, cancellationToken: cancellationToken);
                        if (structureDef != null)
                        {
                            loadedProfiles[structureDef.Url] = structureDef;
                        }
                    }
                }

                // Queue dependencies
                var guideInfo = await GetGuideInfoAsync(tenantId, guideRef, cancellationToken);
                foreach (var dependency in guideInfo?.Dependencies ?? Enumerable.Empty<ImplementationGuideDependency>())
                {
                    var depRef = new ImplementationGuideReference
                    {
                        Id = dependency.Id,
                        Version = dependency.Version,
                        FhirVersion = fhirVersion
                    };

                    if (!processedGuides.Contains($"{depRef.Id}@{depRef.Version}"))
                    {
                        guidesToLoad.Enqueue(depRef);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load implementation guide {GuideRef}", guideRef);
            }
        }

        // Create composite schema provider
        var schemaProvider = CreateCompositeSchemaProvider(fhirVersion, loadedProfiles.Values);

        return new ImplementationGuideContext
        {
            TenantId = tenantId,
            FhirVersion = fhirVersion,
            ActiveGuides = guideRefs,
            LoadedProfiles = loadedProfiles,
            SchemaProvider = schemaProvider
        };
    }

    private IFhirSchemaProvider CreateCompositeSchemaProvider(FhirVersion fhirVersion, IEnumerable<StructureDefinition> profiles)
    {
        // Create a schema provider that includes both base FHIR and IG profiles
        return new CompositeSchemaProvider(fhirVersion, profiles);
    }

    private static string BuildContextCacheKey(string tenantId, FhirVersion fhirVersion, HttpRequest request)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        // Include tenant and FHIR version
        hasher.AppendData(Encoding.UTF8.GetBytes(tenantId));
        hasher.AppendData(Encoding.UTF8.GetBytes(fhirVersion.ToString()));

        // Include relevant headers
        var headers = new[] { "X-FHIR-Profile", "X-IG-Version", "Accept" };
        foreach (var header in headers)
        {
            if (request.Headers.TryGetValue(header, out var values))
            {
                hasher.AppendData(Encoding.UTF8.GetBytes(string.Join(",", values)));
            }
        }

        var hash = hasher.GetHashAndReset();
        return $"ig-context:{tenantId}:{Convert.ToHexString(hash)}";
    }

    private static string? ExtractIgIdFromCanonical(string canonicalUrl)
    {
        // Extract IG ID from known patterns:
        // http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient -> us-core
        // http://example.org/fhir/myig/StructureDefinition/my-profile -> myig

        var uri = new Uri(canonicalUrl);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Look for common IG path patterns
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].Equals("fhir", StringComparison.OrdinalIgnoreCase) &&
                segments[i + 1] != "StructureDefinition")
            {
                return segments[i + 1];
            }
        }

        return null;
    }

    private static IEnumerable<string> ExtractProfilesFromAcceptHeader(string acceptHeader)
    {
        // Parse Accept header for FHIR profile specifications
        // application/fhir+json;profile="http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"

        var profiles = new List<string>();
        var mediaTypes = acceptHeader.Split(',');

        foreach (var mediaType in mediaTypes)
        {
            var profileMatch = Regex.Match(mediaType, @"profile\s*=\s*""([^""]+)""");
            if (profileMatch.Success)
            {
                profiles.Add(profileMatch.Groups[1].Value);
            }
        }

        return profiles;
    }
}
```

### Composite Schema Provider

```csharp
public class CompositeSchemaProvider : IFhirSchemaProvider
{
    private readonly IFhirSchemaProvider _baseProvider;
    private readonly ConcurrentDictionary<string, IStructureDefinitionSummary> _profileCache = new();
    private readonly IReadOnlySet<string> _resourceTypeNames;

    public CompositeSchemaProvider(FhirVersion fhirVersion, IEnumerable<StructureDefinition> profiles)
    {
        Version = GetFhirSpecification(fhirVersion);
        _baseProvider = new FhirJsonSchemaStructureDefinitionSummaryProvider(Version);

        // Build resource type names including profiles
        var baseResourceTypes = _baseProvider.ResourceTypeNames;
        var profileResourceTypes = profiles
            .Where(p => !string.IsNullOrEmpty(p.Type))
            .Select(p => p.Type!)
            .Distinct();

        _resourceTypeNames = baseResourceTypes
            .Concat(profileResourceTypes)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Pre-populate profile cache
        foreach (var profile in profiles)
        {
            if (!string.IsNullOrEmpty(profile.Url))
            {
                _profileCache[profile.Url] = CreateProfileSummary(profile);
            }
        }
    }

    public FhirSpecification Version { get; }

    public IReadOnlySet<string> ResourceTypeNames => _resourceTypeNames;

    public IStructureDefinitionSummary? Provide(string canonical)
    {
        // First check our profile cache
        if (_profileCache.TryGetValue(canonical, out var profileSummary))
        {
            return profileSummary;
        }

        // Fall back to base provider
        return _baseProvider.Provide(canonical);
    }

    private static IStructureDefinitionSummary CreateProfileSummary(StructureDefinition profile)
    {
        // Convert StructureDefinition to IStructureDefinitionSummary
        // This is a simplified implementation - in practice you'd need full conversion
        return new ProfileStructureDefinitionSummary(profile);
    }

    private static FhirSpecification GetFhirSpecification(FhirVersion version) => version switch
    {
        FhirVersion.Stu3 => FhirSpecification.STU3,
        FhirVersion.R4 => FhirSpecification.R4,
        FhirVersion.R4B => FhirSpecification.R4B,
        FhirVersion.R5 => FhirSpecification.R5,
        _ => throw new ArgumentException($"Unsupported FHIR version: {version}")
    };
}

internal class ProfileStructureDefinitionSummary : IStructureDefinitionSummary
{
    private readonly StructureDefinition _profile;

    public ProfileStructureDefinitionSummary(StructureDefinition profile)
    {
        _profile = profile;
    }

    public string TypeName => _profile.Type ?? throw new InvalidOperationException("Profile missing type");
    public bool IsAbstract => _profile.Abstract ?? false;
    public bool IsResource => _profile.Kind == StructureDefinition.StructureDefinitionKind.Resource;

    // Implement other required properties...
    public IReadOnlyCollection<IElementDefinitionSummary> GetElements() =>
        throw new NotImplementedException("Profile element conversion not implemented");
}
```

## IG Package Management

### NPM Package Loader

```csharp
public class NpmImplementationGuidePackageLoader : IImplementationGuidePackageLoader
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NpmImplementationGuidePackageLoader> _logger;

    public NpmImplementationGuidePackageLoader(HttpClient httpClient, ILogger<NpmImplementationGuidePackageLoader> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async ValueTask<ImplementationGuidePackage> LoadPackageAsync(Uri packageSource, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Loading IG package from {Source}", packageSource);

        // Download package
        using var response = await _httpClient.GetAsync(packageSource, cancellationToken);
        response.EnsureSuccessStatusCode();

        var packageData = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        // Extract package contents
        var resources = await ExtractTgzResourcesAsync(packageData, cancellationToken);

        // Parse package.json for metadata
        var packageInfo = await ParsePackageInfoAsync(resources, cancellationToken);

        return new ImplementationGuidePackage
        {
            Info = packageInfo,
            PackageData = packageData,
            Format = PackageFormat.NpmTgz,
            Resources = resources
        };
    }

    public async ValueTask<IReadOnlyList<ISourceNode>> ExtractResourcesAsync(
        ImplementationGuidePackage package,
        string? resourceType = null,
        CancellationToken cancellationToken = default)
    {
        var resources = new List<ISourceNode>();

        foreach (var kvp in package.Resources)
        {
            if (kvp.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var sourceNode = JsonSourceNodeFactory.Parse(kvp.Value.Span);

                    // Filter by resource type if specified
                    if (resourceType != null)
                    {
                        var nodeResourceType = sourceNode.Children("resourceType").FirstOrDefault()?.Text;
                        if (!string.Equals(nodeResourceType, resourceType, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    resources.Add(sourceNode);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse resource from {FileName}", kvp.Key);
                }
            }
        }

        return resources;
    }

    private async ValueTask<IReadOnlyDictionary<string, ReadOnlyMemory<byte>>> ExtractTgzResourcesAsync(
        ReadOnlyMemory<byte> packageData,
        CancellationToken cancellationToken)
    {
        var resources = new Dictionary<string, ReadOnlyMemory<byte>>();

        using var packageStream = new MemoryStream(packageData.ToArray());
        using var gzipStream = new GZipStream(packageStream, CompressionMode.Decompress);
        using var tarStream = new TarReader(gzipStream);

        while (await tarStream.GetNextEntryAsync(cancellationToken) is TarEntry entry)
        {
            if (entry.EntryType == TarEntryType.RegularFile)
            {
                using var entryStream = FhirStreamManager.GetStream("TarExtract");
                await entry.DataStream!.CopyToAsync(entryStream, cancellationToken);

                resources[entry.Name] = entryStream.GetBuffer().AsMemory(0, (int)entryStream.Length);
            }
        }

        return resources;
    }

    private async ValueTask<ImplementationGuideInfo> ParsePackageInfoAsync(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> resources,
        CancellationToken cancellationToken)
    {
        // Find and parse package.json
        if (!resources.TryGetValue("package.json", out var packageJsonData))
        {
            throw new InvalidOperationException("Package missing package.json");
        }

        using var jsonStream = FhirStreamManager.GetStream(packageJsonData.Span, "PackageJson");
        var packageJson = await JsonSerializer.DeserializeAsync<JsonElement>(jsonStream, cancellationToken: cancellationToken);

        var name = packageJson.GetProperty("name").GetString() ?? throw new InvalidOperationException("Package missing name");
        var version = packageJson.GetProperty("version").GetString() ?? throw new InvalidOperationException("Package missing version");
        var fhirVersion = ParseFhirVersion(packageJson);

        var dependencies = new List<ImplementationGuideDependency>();
        if (packageJson.TryGetProperty("dependencies", out var depsElement))
        {
            foreach (var dep in depsElement.EnumerateObject())
            {
                dependencies.Add(new ImplementationGuideDependency
                {
                    Id = dep.Name,
                    Version = dep.Value.GetString() ?? "*",
                    Type = DependencyType.Includes
                });
            }
        }

        return new ImplementationGuideInfo
        {
            Id = name,
            Name = packageJson.TryGetProperty("title", out var title) ? title.GetString() ?? name : name,
            Version = version,
            FhirVersion = fhirVersion,
            PackageUrl = new Uri($"https://packages.fhir.org/{name}/-/{name}-{version}.tgz"),
            Publisher = packageJson.TryGetProperty("author", out var author) ? author.GetString() ?? "Unknown" : "Unknown",
            PublishDate = DateTimeOffset.UtcNow, // Would be parsed from actual package metadata
            Status = ImplementationGuideStatus.Active,
            Dependencies = dependencies,
            Description = packageJson.TryGetProperty("description", out var desc) ? desc.GetString() : null
        };
    }

    private static FhirVersion ParseFhirVersion(JsonElement packageJson)
    {
        if (packageJson.TryGetProperty("fhirVersions", out var versionsElement))
        {
            var firstVersion = versionsElement.EnumerateArray().FirstOrDefault();
            if (firstVersion.ValueKind == JsonValueKind.String)
            {
                var versionString = firstVersion.GetString();
                return versionString switch
                {
                    "3.0.2" => FhirVersion.Stu3,
                    "4.0.1" => FhirVersion.R4,
                    "4.3.0" => FhirVersion.R4B,
                    "5.0.0" => FhirVersion.R5,
                    _ => FhirVersion.R4
                };
            }
        }

        return FhirVersion.R4; // Default
    }
}
```

## Tenant IG Configuration

### IG Configuration Service

```csharp
public interface ITenantImplementationGuideConfiguration
{
    ValueTask<TenantIGConfiguration> GetConfigurationAsync(string tenantId, CancellationToken cancellationToken = default);
    ValueTask SetConfigurationAsync(string tenantId, TenantIGConfiguration configuration, CancellationToken cancellationToken = default);
    ValueTask AddImplementationGuideAsync(string tenantId, ImplementationGuideReference guideRef, CancellationToken cancellationToken = default);
    ValueTask RemoveImplementationGuideAsync(string tenantId, ImplementationGuideReference guideRef, CancellationToken cancellationToken = default);
}

public record TenantIGConfiguration
{
    public required string TenantId { get; init; }
    public required IReadOnlyDictionary<FhirVersion, IReadOnlyList<ImplementationGuideReference>> DefaultGuides { get; init; }
    public required IReadOnlyDictionary<string, ImplementationGuideReference> ProfileMappings { get; init; }
    public IGResolutionStrategy ResolutionStrategy { get; init; } = IGResolutionStrategy.HeaderFirst;
    public bool AllowDynamicLoading { get; init; } = true;
    public TimeSpan CacheTimeout { get; init; } = TimeSpan.FromHours(4);
}

public enum IGResolutionStrategy
{
    HeaderFirst,      // Check headers first, then defaults
    DefaultFirst,     // Use defaults unless headers specify otherwise
    HeaderOnly,       // Only use header-specified IGs
    DefaultOnly       // Only use configured defaults
}
```

## Dependency Injection Setup

```csharp
public static class ImplementationGuideServiceCollectionExtensions
{
    public static IServiceCollection AddImplementationGuides(this IServiceCollection services, IConfiguration configuration)
    {
        var igSection = configuration.GetSection("ImplementationGuides");

        services.Configure<ImplementationGuideOptions>(igSection);

        services.AddHttpClient<NpmImplementationGuidePackageLoader>();

        services.AddSingleton<IImplementationGuidePackageLoader, NpmImplementationGuidePackageLoader>();
        services.AddSingleton<IImplementationGuideProvider, CachedImplementationGuideProvider>();
        services.AddScoped<IImplementationGuideResolver, HeaderBasedIGResolver>();
        services.AddSingleton<ITenantImplementationGuideConfiguration, TenantIGConfigurationService>();

        return services;
    }
}

public class ImplementationGuideOptions
{
    public string PackageRegistryUrl { get; set; } = "https://packages.fhir.org";
    public string LocalPackagePath { get; set; } = "./packages";
    public TimeSpan PackageCacheTimeout { get; set; } = TimeSpan.FromHours(24);
    public int MaxConcurrentLoads { get; set; } = 5;
    public bool AllowUntrustedPackages { get; set; } = false;
}
```

<function_calls>
<invoke name="TodoWrite">
<parameter name="todos">[{"content": "Design standardized caching interfaces for in-memory and distributed scenarios", "status": "completed", "activeForm": "Designed standardized caching interfaces for in-memory and distributed scenarios"}, {"content": "Create bus abstraction with Medino and Redis transport adapters", "status": "completed", "activeForm": "Created bus abstraction with Medino and Redis transport adapters"}, {"content": "Design native SMART on FHIR v2 implementation", "status": "completed", "activeForm": "Designed native SMART on FHIR v2 implementation"}, {"content": "Create multi-version IG loading and resolution system", "status": "completed", "activeForm": "Created multi-version IG loading and resolution system"}]
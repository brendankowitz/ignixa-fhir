# Investigation: StructureDefinition-Driven Patient Summaries

**Feature**: fhir-operations
**Status**: Investigation
**Created**: 2025-12-19
**Effort Estimate**: 60-80 hours
**Dependencies**:
- IPS Generator implementation (investigations/ips-generator.md)
- Package Management infrastructure (existing)
- StructureDefinition parsing capabilities (Ignixa.Specification)

---

## Context

### Problem Statement

The current IPS implementation hard-codes section definitions in `DefaultIpsGenerationStrategy`:
- LOINC codes manually specified
- Resource type mappings static
- Cardinality (Required/Recommended) hard-coded
- Profile URLs manually maintained

This creates several issues:

1. **Maintenance Burden** - Every IPS IG update requires code changes
2. **Limited Extensibility** - Cannot support jurisdiction-specific summaries (AU PS, US IPS, EU PS) without creating new strategy classes
3. **Duplication** - Section metadata already exists in the IG's Composition StructureDefinition
4. **Package Install Friction** - Installing `hl7.fhir.au.ps` package doesn't automatically enable AU Patient Summary generation

### Opportunity

FHIR IGs define patient summary sections using **StructureDefinition slicing** in the Composition profile. This metadata contains everything needed to generate summaries:

```
Composition.section (sliced by pattern:code)
├─ section:sectionAllergies (1..1) → AllergyIntolerance-uv-ips
├─ section:sectionMedications (1..1) → MedicationStatement-uv-ips
├─ section:sectionProblems (1..1) → Condition-uv-ips
├─ section:sectionImmunizations (0..1) → Immunization-uv-ips
└─ ... (10+ other slices)
```

We can parse this metadata programmatically to create **metadata-driven generation strategies**.

---

## FHIR Specification Analysis

### Composition Profile Slicing

IPS and derivative IGs (AU PS, US IPS, EU PS) define sections using **discriminated slicing**:

**Discriminator**: `pattern:code` (LOINC code)
**Cardinality**: `min..max` per slice (1..1 = Required, 0..1 = Recommended/Optional)
**Entry Profiles**: `section.entry.type.targetProfile[]`

#### Example: IPS Composition Snapshot

```json
{
  "snapshot": {
    "element": [
      {
        "path": "Composition.section",
        "slicing": {
          "discriminator": [{ "type": "pattern", "path": "code" }],
          "rules": "open"
        }
      },
      {
        "path": "Composition.section:sectionAllergies",
        "sliceName": "sectionAllergies",
        "min": 1,
        "max": "1"
      },
      {
        "path": "Composition.section:sectionAllergies.title",
        "min": 1,
        "fixedString": "Allergies and Intolerances"
      },
      {
        "path": "Composition.section:sectionAllergies.code",
        "min": 1,
        "patternCodeableConcept": {
          "coding": [{
            "system": "http://loinc.org",
            "code": "48765-2"
          }]
        }
      },
      {
        "path": "Composition.section:sectionAllergies.entry",
        "type": [{
          "code": "Reference",
          "targetProfile": [
            "http://hl7.org/fhir/uv/ips/StructureDefinition/AllergyIntolerance-uv-ips"
          ]
        }]
      }
    ]
  }
}
```

### Extractable Metadata

From each `Composition.section:[sliceName]` slice, we can extract:

| Metadata | ElementDefinition Path | Use Case |
|----------|----------------------|----------|
| **Slice Name** | `sliceName` | Debugging/logging |
| **LOINC Code** | `code.pattern.coding[0].code` | Section identification |
| **LOINC Display** | `code.pattern.coding[0].display` | Human-readable name |
| **Title** | `title.fixedString` or `title.pattern` | Display in document |
| **Cardinality** | `min`/`max` | Required (1..1), Recommended (0..1) |
| **Entry Profiles** | `entry.type[0].targetProfile[]` | Which resources to include |
| **Resource Types** | Parse profile URLs → resource type | Classification logic |

### Jurisdiction-Specific Summaries

**AU Patient Summary** (hl7.fhir.au.ps):
- Reuses IPS Composition structure
- Same section slicing pattern
- Adds AU-specific entry profiles (AU Core resources)
- Strengthens terminology bindings

**Result**: A metadata-driven approach automatically supports AU PS when the package is installed.

---

## Architecture Overview

### Design Principles

1. **Parse, Don't Code** - Extract section definitions from StructureDefinition rather than hard-coding
2. **Package-Driven** - Installing a patient summary package automatically enables generation
3. **Fallback to Default** - If no package found, use hard-coded default IPS strategy
4. **Strategy Registry** - Multiple strategies can coexist (IPS, AU PS, custom)
5. **Lazy Initialization** - Parse StructureDefinitions on-demand, cache results

### High-Level Flow

```
┌──────────────────────────────────────────────────────────────────┐
│                  Package Installation Event                       │
│  hl7.fhir.uv.ips@2.0.0, hl7.fhir.au.ps@1.0.0, etc.              │
└────────────────────────────┬─────────────────────────────────────┘
                             │
                             ▼
┌──────────────────────────────────────────────────────────────────┐
│            StructureDefinitionStrategyFactory                     │
│  - Scans package for Composition profiles                        │
│  - Identifies patient summary profiles (meta.profile pattern)    │
│  - Parses section slices from snapshot                           │
│  - Creates StructureDefinitionBasedStrategy                       │
│  - Registers in IIpsGenerationStrategyRegistry                    │
└────────────────────────────┬─────────────────────────────────────┘
                             │
                             ▼
┌──────────────────────────────────────────────────────────────────┐
│           IIpsGenerationStrategyRegistry                          │
│  Dictionary<string, IIpsGenerationStrategy>                       │
│  - "http://hl7.org/fhir/uv/ips/..." → IpsStrategy                │
│  - "http://hl7.org.au/fhir/ps/..." → AuPsStrategy               │
│  - Custom profiles → CustomStrategy                              │
└────────────────────────────┬─────────────────────────────────────┘
                             │
                             ▼
┌──────────────────────────────────────────────────────────────────┐
│            Request: GET /Patient/123/$summary                     │
│                     ?profile=http://hl7.org.au/...               │
└────────────────────────────┬─────────────────────────────────────┘
                             │
                             ▼
┌──────────────────────────────────────────────────────────────────┐
│                   IpsGeneratorHandler                             │
│  - Selects strategy from registry by profile URL                 │
│  - Falls back to default IPS if no profile specified             │
│  - Delegates to IIpsGeneratorService                              │
└──────────────────────────────────────────────────────────────────┘
```

---

## Implementation Design

### Core Interfaces

#### IIpsGenerationStrategyRegistry

```csharp
/// <summary>
/// Registry of available patient summary generation strategies.
/// Strategies are registered when packages containing Composition profiles are installed.
/// </summary>
public interface IIpsGenerationStrategyRegistry
{
    /// <summary>
    /// Gets a strategy by its Bundle profile URL.
    /// </summary>
    IIpsGenerationStrategy? GetStrategy(string? profileUrl);

    /// <summary>
    /// Gets the default strategy (IPS).
    /// </summary>
    IIpsGenerationStrategy GetDefaultStrategy();

    /// <summary>
    /// Registers a strategy.
    /// </summary>
    void RegisterStrategy(string profileUrl, IIpsGenerationStrategy strategy);

    /// <summary>
    /// Lists all available strategies.
    /// </summary>
    IReadOnlyDictionary<string, IIpsGenerationStrategy> GetAllStrategies();
}
```

#### IStructureDefinitionStrategyFactory

```csharp
/// <summary>
/// Factory for creating IPS generation strategies from StructureDefinition resources.
/// </summary>
public interface IStructureDefinitionStrategyFactory
{
    /// <summary>
    /// Creates a strategy from a Composition profile StructureDefinition.
    /// Returns null if the StructureDefinition is not a patient summary profile.
    /// </summary>
    IIpsGenerationStrategy? CreateFromStructureDefinition(
        StructureDefinition compositionProfile,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Identifies if a StructureDefinition represents a patient summary Composition.
    /// Heuristics:
    /// - resourceType == "Composition"
    /// - baseDefinition points to IPS or derivative
    /// - Has section slicing with LOINC codes
    /// </summary>
    bool IsPatientSummaryProfile(StructureDefinition structureDefinition);
}
```

### File Structure

```
src/Ignixa.Application.Operations/Features/Ips/
├── Api/
│   ├── IIpsGenerationStrategyRegistry.cs
│   └── IStructureDefinitionStrategyFactory.cs
├── Registry/
│   ├── IpsGenerationStrategyRegistry.cs
│   └── IpsFeature.cs (IPackageFeature implementation)
├── Strategy/
│   ├── StructureDefinitionBasedStrategy.cs
│   ├── StructureDefinitionStrategyFactory.cs
│   ├── SectionMetadataParser.cs
│   └── ResourceTypeResolver.cs
├── Events/
│   └── PackageInstalledStrategyRegistrationHandler.cs
└── Metadata/
    └── IpsSummaryOperationEnricher.cs (ICapabilitySegment)
```

### StructureDefinitionBasedStrategy

```csharp
/// <summary>
/// IPS generation strategy built from a Composition StructureDefinition.
/// Parses section slices to extract metadata dynamically.
/// </summary>
public class StructureDefinitionBasedStrategy : IIpsGenerationStrategy
{
    private readonly StructureDefinition _compositionProfile;
    private readonly IReadOnlyList<Section> _sections;
    private readonly FrozenDictionary<string, Section> _sectionByResourceType;
    private readonly string _bundleProfile;
    private readonly string _compositionProfileUrl;

    public StructureDefinitionBasedStrategy(
        StructureDefinition compositionProfile,
        IReadOnlyList<Section> sections,
        string bundleProfile)
    {
        _compositionProfile = compositionProfile;
        _sections = sections;
        _bundleProfile = bundleProfile;
        _compositionProfileUrl = compositionProfile.Url;

        _sectionByResourceType = CreateSectionByResourceType(sections);
    }

    public string BundleProfile => _bundleProfile;

    public IReadOnlyList<Section> GetSections() => _sections;

    public bool ShouldIncludeResource(Section section, ResourceJsonNode resource, IpsContext context)
    {
        // Default implementation: include all resources that match section's resource types
        // Can be overridden by specific strategies for filtering
        return true;
    }

    public Section? ClassifyResource(ResourceJsonNode resource)
    {
        var resourceType = resource.ResourceType;
        return _sectionByResourceType.GetValueOrDefault(resourceType);
    }

    public ResourceJsonNode CreateAuthor(IpsContext context)
    {
        // Default: Device resource
        var deviceJson = new JsonObject
        {
            ["resourceType"] = "Device",
            ["id"] = Guid.NewGuid().ToString(),
            ["deviceName"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Ignixa FHIR Server",
                    ["type"] = "manufacturer-name"
                }
            }
        };

        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(deviceJson.ToJsonString()!);
    }

    public string CreateTitle(IpsContext context)
    {
        // Default title format
        return $"Patient Summary as of {context.GenerationTime:yyyy-MM-dd}";
    }

    public void PostProcessBundle(ResourceJsonNode bundle, IpsContext context)
    {
        // No post-processing by default
    }

    private static FrozenDictionary<string, Section> CreateSectionByResourceType(
        IReadOnlyList<Section> sections)
    {
        var dict = new Dictionary<string, Section>();

        foreach (var section in sections)
        {
            foreach (var resourceType in section.ResourceTypes)
            {
                // First section wins for resource types that appear in multiple sections
                dict.TryAdd(resourceType, section);
            }
        }

        return dict.ToFrozenDictionary();
    }
}
```

### SectionMetadataParser

```csharp
/// <summary>
/// Parses section metadata from Composition StructureDefinition snapshots.
/// </summary>
public class SectionMetadataParser
{
    private const string SectionPath = "Composition.section";

    /// <summary>
    /// Extracts all section slices from a Composition StructureDefinition.
    /// </summary>
    public IReadOnlyList<Section> ParseSections(StructureDefinition compositionProfile)
    {
        if (compositionProfile.Snapshot?.Element is not { } elements)
        {
            throw new InvalidOperationException(
                $"StructureDefinition {compositionProfile.Url} has no snapshot");
        }

        var sections = new List<Section>();
        var sectionElements = elements
            .Where(e => e.Path.StartsWith(SectionPath + ":") && e.SliceName is not null)
            .GroupBy(e => e.SliceName)
            .ToList();

        foreach (var sectionGroup in sectionElements)
        {
            var sliceName = sectionGroup.Key!;
            var section = ParseSection(sliceName, sectionGroup.ToList());

            if (section is not null)
            {
                sections.Add(section);
            }
        }

        return sections;
    }

    private Section? ParseSection(string sliceName, List<ElementDefinition> sectionElements)
    {
        // Find the root section element
        var rootElement = sectionElements.FirstOrDefault(e => e.Path == $"{SectionPath}:{sliceName}");
        if (rootElement is null)
        {
            return null;
        }

        // Extract LOINC code from pattern or fixed value
        var codeElement = sectionElements.FirstOrDefault(e => e.Path == $"{SectionPath}:{sliceName}.code");
        var loincCode = ExtractLoincCode(codeElement);
        if (loincCode is null)
        {
            return null; // Not a valid IPS section
        }

        // Extract title
        var titleElement = sectionElements.FirstOrDefault(e => e.Path == $"{SectionPath}:{sliceName}.title");
        var title = ExtractTitle(titleElement) ?? sliceName;

        // Extract cardinality
        var cardinality = DetermineCardinality(rootElement.Min, rootElement.Max);

        // Extract entry profiles
        var entryElement = sectionElements.FirstOrDefault(e => e.Path == $"{SectionPath}:{sliceName}.entry");
        var (profile, resourceTypes) = ExtractEntryProfiles(entryElement);

        return new Section
        {
            Title = title,
            Code = loincCode.Code,
            CodeSystem = loincCode.System,
            Display = loincCode.Display,
            Profile = profile,
            ResourceTypes = resourceTypes.ToHashSet(),
            Cardinality = cardinality
        };
    }

    private (string Code, string System, string? Display)? ExtractLoincCode(ElementDefinition? codeElement)
    {
        if (codeElement is null)
        {
            return null;
        }

        // Try pattern first
        if (codeElement.Pattern is CodeableConcept patternCc)
        {
            var coding = patternCc.Coding?.FirstOrDefault();
            if (coding?.System == "http://loinc.org" && coding.Code is not null)
            {
                return (coding.Code, coding.System, coding.Display);
            }
        }

        // Try fixed value
        if (codeElement.Fixed is CodeableConcept fixedCc)
        {
            var coding = fixedCc.Coding?.FirstOrDefault();
            if (coding?.System == "http://loinc.org" && coding.Code is not null)
            {
                return (coding.Code, coding.System, coding.Display);
            }
        }

        return null;
    }

    private string? ExtractTitle(ElementDefinition? titleElement)
    {
        if (titleElement is null)
        {
            return null;
        }

        // Try fixed string
        if (titleElement.Fixed is FhirString fixedString)
        {
            return fixedString.Value;
        }

        // Try pattern
        if (titleElement.Pattern is FhirString patternString)
        {
            return patternString.Value;
        }

        return null;
    }

    private SectionCardinality DetermineCardinality(int? min, string? max)
    {
        // 1..1 = Required
        if (min == 1 && max == "1")
        {
            return SectionCardinality.Required;
        }

        // 0..1 with specific guidance in IPS IG:
        // Immunizations, Procedures, Devices, Diagnostics = Recommended
        // Others = Optional
        // For simplicity, treat all 0..1 as Recommended
        return SectionCardinality.Recommended;
    }

    private (string Profile, List<string> ResourceTypes) ExtractEntryProfiles(ElementDefinition? entryElement)
    {
        if (entryElement?.Type is null || entryElement.Type.Count == 0)
        {
            return ("http://hl7.org/fhir/StructureDefinition/Resource", []);
        }

        var referenceType = entryElement.Type.FirstOrDefault(t => t.Code == "Reference");
        if (referenceType?.TargetProfile is null || referenceType.TargetProfile.Count == 0)
        {
            return ("http://hl7.org/fhir/StructureDefinition/Resource", []);
        }

        var profiles = referenceType.TargetProfile.Select(tp => tp.Value).ToList();
        var resourceTypes = profiles
            .Select(ExtractResourceTypeFromProfile)
            .Where(rt => rt is not null)
            .Distinct()
            .ToList()!;

        // Return first profile as representative
        return (profiles[0], resourceTypes);
    }

    private string? ExtractResourceTypeFromProfile(string profileUrl)
    {
        // Examples:
        // http://hl7.org/fhir/uv/ips/StructureDefinition/AllergyIntolerance-uv-ips → AllergyIntolerance
        // http://hl7.org.au/fhir/core/StructureDefinition/au-core-patient → Patient

        var parts = profileUrl.Split('/');
        if (parts.Length < 2)
        {
            return null;
        }

        var lastPart = parts[^1];

        // Try to extract resource type from profile name
        // Common patterns:
        // - AllergyIntolerance-uv-ips
        // - au-core-patient
        // - Condition-uv-ips

        var hyphenIndex = lastPart.IndexOf('-');
        if (hyphenIndex > 0)
        {
            return lastPart[..hyphenIndex];
        }

        // If no hyphen, assume the whole last part is the resource type
        return lastPart;
    }
}
```

### StructureDefinitionStrategyFactory

```csharp
/// <summary>
/// Factory for creating IPS generation strategies from Composition StructureDefinitions.
/// </summary>
public class StructureDefinitionStrategyFactory(
    SectionMetadataParser sectionParser,
    ILogger<StructureDefinitionStrategyFactory> logger
) : IStructureDefinitionStrategyFactory
{
    public IIpsGenerationStrategy? CreateFromStructureDefinition(
        StructureDefinition compositionProfile,
        CancellationToken cancellationToken = default)
    {
        if (!IsPatientSummaryProfile(compositionProfile))
        {
            return null;
        }

        logger.LogInformation(
            "Creating patient summary strategy from StructureDefinition {Url}",
            compositionProfile.Url);

        // Parse sections from snapshot
        var sections = sectionParser.ParseSections(compositionProfile);

        if (sections.Count == 0)
        {
            logger.LogWarning(
                "No sections found in Composition profile {Url}",
                compositionProfile.Url);
            return null;
        }

        // Infer Bundle profile URL
        var bundleProfile = InferBundleProfile(compositionProfile);

        var strategy = new StructureDefinitionBasedStrategy(
            compositionProfile,
            sections,
            bundleProfile);

        logger.LogInformation(
            "Created strategy with {SectionCount} sections for profile {Profile}",
            sections.Count,
            bundleProfile);

        return strategy;
    }

    public bool IsPatientSummaryProfile(StructureDefinition structureDefinition)
    {
        // Must be a Composition profile
        if (structureDefinition.Type != "Composition")
        {
            return false;
        }

        // Must derive from IPS Composition or have IPS-like characteristics
        if (IsIpsDerivedProfile(structureDefinition))
        {
            return true;
        }

        // Check for section slicing with LOINC codes (heuristic)
        if (HasSectionSlicingWithLoincCodes(structureDefinition))
        {
            return true;
        }

        return false;
    }

    private bool IsIpsDerivedProfile(StructureDefinition structureDefinition)
    {
        var baseDefinition = structureDefinition.BaseDefinition;

        // Direct IPS derivation
        if (baseDefinition == "http://hl7.org/fhir/uv/ips/StructureDefinition/Composition-uv-ips")
        {
            return true;
        }

        // Indirect derivation (would need recursive checking)
        // For now, check for common patterns
        return baseDefinition?.Contains("/ips/") == true;
    }

    private bool HasSectionSlicingWithLoincCodes(StructureDefinition structureDefinition)
    {
        if (structureDefinition.Snapshot?.Element is not { } elements)
        {
            return false;
        }

        // Look for section slicing
        var hasSectionSlicing = elements.Any(e =>
            e.Path == "Composition.section" &&
            e.Slicing is not null);

        if (!hasSectionSlicing)
        {
            return false;
        }

        // Check if any slices have LOINC codes
        var hasLoincCodes = elements.Any(e =>
        {
            if (!e.Path.StartsWith("Composition.section:") || !e.Path.EndsWith(".code"))
            {
                return false;
            }

            var pattern = e.Pattern as CodeableConcept;
            var coding = pattern?.Coding?.FirstOrDefault();
            return coding?.System == "http://loinc.org";
        });

        return hasLoincCodes;
    }

    private string InferBundleProfile(StructureDefinition compositionProfile)
    {
        var compositionUrl = compositionProfile.Url;

        // Common patterns:
        // http://hl7.org/fhir/uv/ips/StructureDefinition/Composition-uv-ips
        // → http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips

        if (compositionUrl.Contains("Composition-uv-ips"))
        {
            return compositionUrl.Replace("Composition-uv-ips", "Bundle-uv-ips");
        }

        // Generic fallback
        var bundleUrl = compositionUrl.Replace("Composition", "Bundle");

        logger.LogWarning(
            "Using inferred Bundle profile {BundleUrl} for Composition {CompositionUrl}",
            bundleUrl,
            compositionUrl);

        return bundleUrl;
    }
}
```

### Package Installation Handler

```csharp
/// <summary>
/// Registers patient summary strategies when packages are installed.
/// </summary>
public class PackageInstalledStrategyRegistrationHandler(
    IStructureDefinitionStrategyFactory strategyFactory,
    IIpsGenerationStrategyRegistry strategyRegistry,
    IStructureDefinitionProvider structureDefinitionProvider,
    ILogger<PackageInstalledStrategyRegistrationHandler> logger
) : INotificationHandler<PackageInstalledNotification>
{
    public async Task HandleAsync(
        PackageInstalledNotification notification,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Scanning package {PackageId}#{Version} for patient summary profiles",
            notification.PackageId,
            notification.Version);

        // Get all StructureDefinitions from package
        var structureDefinitions = await structureDefinitionProvider
            .GetStructureDefinitionsByPackageAsync(
                notification.PackageId,
                notification.Version,
                cancellationToken);

        var registeredCount = 0;

        foreach (var sd in structureDefinitions)
        {
            var strategy = strategyFactory.CreateFromStructureDefinition(sd, cancellationToken);

            if (strategy is not null)
            {
                strategyRegistry.RegisterStrategy(strategy.BundleProfile, strategy);
                registeredCount++;

                logger.LogInformation(
                    "Registered patient summary strategy for profile {Profile}",
                    strategy.BundleProfile);
            }
        }

        if (registeredCount > 0)
        {
            logger.LogInformation(
                "Registered {Count} patient summary strategies from package {PackageId}",
                registeredCount,
                notification.PackageId);
        }
    }
}
```

### Strategy Registry

```csharp
/// <summary>
/// Registry of patient summary generation strategies.
/// </summary>
public class IpsGenerationStrategyRegistry : IIpsGenerationStrategyRegistry
{
    private readonly ConcurrentDictionary<string, IIpsGenerationStrategy> _strategies = new();
    private readonly IIpsGenerationStrategy _defaultStrategy;

    public IpsGenerationStrategyRegistry(IEnumerable<IIpsGenerationStrategy> strategies)
    {
        foreach (var strategy in strategies)
        {
            _strategies.TryAdd(strategy.BundleProfile, strategy);
        }

        // Default to IPS
        _defaultStrategy = _strategies.Values.FirstOrDefault(s =>
            s.BundleProfile.Contains("uv/ips"))
            ?? strategies.First();
    }

    public IIpsGenerationStrategy? GetStrategy(string? profileUrl)
    {
        if (profileUrl is null)
        {
            return null;
        }

        return _strategies.GetValueOrDefault(profileUrl);
    }

    public IIpsGenerationStrategy GetDefaultStrategy() => _defaultStrategy;

    public void RegisterStrategy(string profileUrl, IIpsGenerationStrategy strategy)
    {
        _strategies.AddOrUpdate(profileUrl, strategy, (_, _) => strategy);
    }

    public IReadOnlyDictionary<string, IIpsGenerationStrategy> GetAllStrategies()
    {
        return _strategies.ToFrozenDictionary();
    }
}
```

---

## Benefits

### 1. Zero-Code Jurisdiction Support

**Before** (hard-coded):
```csharp
// To support AU PS, create a new class:
public class AuPsGenerationStrategy : IIpsGenerationStrategy
{
    // Manually code all 14 sections again...
}
```

**After** (metadata-driven):
```bash
# Install package
POST /Package/$install
{
  "packageId": "hl7.fhir.au.ps",
  "version": "1.0.0"
}

# AU PS automatically available
GET /Patient/123/$summary?profile=http://hl7.org.au/fhir/ps/StructureDefinition/Bundle-au-ps
```

### 2. IG Updates = Package Updates

**Before**: IPS 2.0 → 2.1 requires code changes
**After**: Update package, sections automatically refresh

### 3. Custom Summaries

Organizations can define custom Composition profiles and load them:

```json
{
  "resourceType": "StructureDefinition",
  "url": "http://example.org/fhir/StructureDefinition/Composition-oncology-summary",
  "baseDefinition": "http://hl7.org/fhir/uv/ips/StructureDefinition/Composition-uv-ips",
  "snapshot": {
    "element": [
      {
        "path": "Composition.section:sectionTumorMarkers",
        "sliceName": "sectionTumorMarkers",
        "min": 1,
        "max": "1"
      },
      {
        "path": "Composition.section:sectionTumorMarkers.code",
        "patternCodeableConcept": {
          "coding": [{
            "system": "http://loinc.org",
            "code": "90827-7",
            "display": "Tumor markers panel"
          }]
        }
      }
    ]
  }
}
```

Upload → Strategy automatically created

---

## Trade-offs

### Complexity

**Added**:
- StructureDefinition parsing logic
- Strategy registry
- Package event handling

**Removed**:
- Hard-coded section definitions (100+ LOC per jurisdiction)
- Manual strategy classes

**Net**: ~200 LOC added, ~300 LOC saved per jurisdiction

### Performance

**Parse Cost**: One-time on package install (~50ms per StructureDefinition)
**Runtime Cost**: None (strategies cached in registry)
**Memory**: ~5KB per strategy

### Testability

**Pros**:
- Can test with synthetic StructureDefinitions
- Easy to validate parsing logic

**Cons**:
- Need test fixtures for various IG patterns

---

## Implementation Phases

### Phase 1: Core Parsing (20 hours)

- [ ] Implement `SectionMetadataParser`
  - [ ] Extract LOINC codes from patterns/fixed values
  - [ ] Parse entry profiles
  - [ ] Determine cardinality
  - [ ] Extract resource types from profile URLs

- [ ] Unit tests for parser
  - [ ] IPS Composition snapshot
  - [ ] AU PS Composition snapshot
  - [ ] Edge cases (missing data, malformed)

### Phase 2: Strategy Factory (15 hours)

- [ ] Implement `StructureDefinitionStrategyFactory`
  - [ ] `IsPatientSummaryProfile` heuristics
  - [ ] Bundle profile inference
  - [ ] Strategy creation

- [ ] Implement `StructureDefinitionBasedStrategy`
  - [ ] Section metadata storage
  - [ ] Resource classification
  - [ ] Default author/title generation

- [ ] Unit tests

### Phase 3: Registry & Integration (15 hours)

- [ ] Implement `IpsGenerationStrategyRegistry`
  - [ ] Thread-safe registration
  - [ ] Default strategy selection
  - [ ] Lookup by profile URL

- [ ] Implement `PackageInstalledStrategyRegistrationHandler`
  - [ ] Package event subscription
  - [ ] Batch registration

- [ ] Integration tests
  - [ ] Install hl7.fhir.uv.ips → strategy registered
  - [ ] Install hl7.fhir.au.ps → strategy registered
  - [ ] Generate summaries using both

### Phase 4: Fallback & Migration (10 hours)

- [ ] Keep `DefaultIpsGenerationStrategy` as fallback
- [ ] Add configuration option: `UseStructureDefinitionDrivenStrategies`
- [ ] Migration guide for existing deployments

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Parsing failures** | Strategy not created | Validation tests for known IGs, fallback to default |
| **Profile URL mismatches** | Strategy not found | Fuzzy matching, canonical URL resolution |
| **Missing snapshots** | Cannot parse | Require snapshots in packages, generate if needed |
| **Performance regression** | Slower generation | Cache parsed sections, profile one-time cost |

---

## Future Enhancements

### Dynamic Filtering

Allow StructureDefinitions to specify filtering logic via FHIRPath:

```json
{
  "path": "Composition.section:sectionAllergies.entry",
  "extension": [{
    "url": "http://example.org/fhir/StructureDefinition/entry-filter",
    "valueExpression": {
      "language": "text/fhirpath",
      "expression": "clinicalStatus.coding.code != 'inactive' and verificationStatus.coding.code != 'entered-in-error'"
    }
  }]
}
```

Parse and compile FHIRPath expressions → dynamic filtering without code.

### Custom Narrative Templates

Link sections to Scriban templates via extensions:

```json
{
  "path": "Composition.section:sectionAllergies",
  "extension": [{
    "url": "http://example.org/fhir/StructureDefinition/narrative-template",
    "valueUri": "http://example.org/templates/allergies-custom.scriban"
  }]
}
```

### Multi-Version Support

Support multiple versions of the same IG simultaneously:
- hl7.fhir.uv.ips@1.1.0
- hl7.fhir.uv.ips@2.0.0

Different strategies for each version.

---

## References

### FHIR Specifications

- [IPS Composition Profile](http://hl7.org/fhir/uv/ips/StructureDefinition-composition-uv-ips.html)
- [AU Patient Summary](https://build.fhir.org/ig/hl7au/au-fhir-ps/)
- [StructureDefinition Resource](http://hl7.org/fhir/structuredefinition.html)
- [Profiling Guidance](http://hl7.org/fhir/profiling.html)

### Related Investigations

- [IPS Generator](investigations/ips-generator.md)
- [Package Management](../../package-management/readme.md)

---

## Decision Log

| Decision | Rationale | Date |
|----------|-----------|------|
| Parse from snapshots, not differentials | Snapshots have complete element definitions | 2025-12-19 |
| Registry pattern for strategies | Supports dynamic registration, multiple strategies | 2025-12-19 |
| Package installation as trigger | Natural integration point, automatic updates | 2025-12-19 |
| Keep hard-coded fallback | Production safety, gradual migration | 2025-12-19 |

---

## Appendix A: Example Section Metadata

**Input** (StructureDefinition snippet):

```json
{
  "path": "Composition.section:sectionMedications",
  "sliceName": "sectionMedications",
  "min": 1,
  "max": "1"
},
{
  "path": "Composition.section:sectionMedications.title",
  "fixedString": "Medication Summary"
},
{
  "path": "Composition.section:sectionMedications.code",
  "patternCodeableConcept": {
    "coding": [{
      "system": "http://loinc.org",
      "code": "10160-0",
      "display": "History of Medication use Narrative"
    }]
  }
},
{
  "path": "Composition.section:sectionMedications.entry",
  "type": [{
    "code": "Reference",
    "targetProfile": [
      "http://hl7.org/fhir/uv/ips/StructureDefinition/MedicationStatement-uv-ips",
      "http://hl7.org/fhir/uv/ips/StructureDefinition/MedicationRequest-uv-ips"
    ]
  }]
}
```

**Output** (Section object):

```csharp
new Section
{
    Title = "Medication Summary",
    Code = "10160-0",
    CodeSystem = "http://loinc.org",
    Display = "History of Medication use Narrative",
    Profile = "http://hl7.org/fhir/uv/ips/StructureDefinition/MedicationStatement-uv-ips",
    ResourceTypes = new HashSet<string> { "MedicationStatement", "MedicationRequest" },
    Cardinality = SectionCardinality.Required
}
```

---

---

## Profile Selection Strategy

When multiple patient summary profiles are available (IPS, AU PS, custom), clients select which to use via simple priority:

### Priority Order

1. **Explicit `?profile=` parameter** (highest priority)
   ```http
   GET /Patient/123/$summary?profile=http://hl7.org.au/fhir/ps/StructureDefinition/Bundle-au-ps
   ```

2. **First summary profile from CapabilityStatement**
   - Client fetches `/metadata`
   - Finds `Patient.$summary` operation
   - Uses first profile listed in operation extensions
   - Fallback if no explicit profile requested

3. **Global default (IPS)**
   - Hard-coded: `http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips`
   - Used if no profiles registered in CapabilityStatement

### Implementation

```csharp
public class IpsGeneratorHandler(
    IIpsGenerationStrategyRegistry strategyRegistry,
    ICapabilityStatementProvider capabilityProvider,
    ILogger<IpsGeneratorHandler> logger
) : IRequestHandler<IpsGeneratorQuery, IpsGeneratorResult>
{
    public async Task<IpsGeneratorResult> HandleAsync(
        IpsGeneratorQuery request,
        CancellationToken cancellationToken)
    {
        var strategy = await SelectStrategyAsync(request.Profile, cancellationToken);

        logger.LogInformation(
            "Generating patient summary using profile {Profile}",
            strategy.BundleProfile);

        // ... generate IPS
    }

    private async Task<IIpsGenerationStrategy> SelectStrategyAsync(
        string? requestedProfile,
        CancellationToken cancellationToken)
    {
        // 1. Explicit profile parameter
        if (requestedProfile is not null)
        {
            var strategy = strategyRegistry.GetStrategy(requestedProfile);
            if (strategy is not null)
            {
                return strategy;
            }

            logger.LogWarning(
                "Requested profile {Profile} not found, falling back to default",
                requestedProfile);
        }

        // 2. First profile from CapabilityStatement
        var firstProfile = await GetFirstSummaryProfileFromCapabilityStatementAsync(cancellationToken);
        if (firstProfile is not null)
        {
            var strategy = strategyRegistry.GetStrategy(firstProfile);
            if (strategy is not null)
            {
                return strategy;
            }
        }

        // 3. Global default (IPS)
        return strategyRegistry.GetDefaultStrategy();
    }

    private async Task<string?> GetFirstSummaryProfileFromCapabilityStatementAsync(
        CancellationToken cancellationToken)
    {
        var capability = await capabilityProvider.GetCapabilityStatementAsync(cancellationToken);

        var patientResource = capability.Rest?[0].Resource?
            .FirstOrDefault(r => r.Type == "Patient");

        var summaryOperation = patientResource?.Operation?
            .FirstOrDefault(op => op.Name == "summary");

        // Find first profile extension
        var profileExtension = summaryOperation?.Extension?
            .FirstOrDefault(ext => ext.Url ==
                "http://hl7.org/fhir/StructureDefinition/capabilitystatement-operation-profile");

        return profileExtension?.ValueCanonical;
    }
}
```

### Discovery via CapabilityStatement

```http
GET /metadata
```

```json
{
  "resourceType": "CapabilityStatement",
  "rest": [{
    "resource": [{
      "type": "Patient",
      "operation": [{
        "name": "summary",
        "definition": "http://hl7.org/fhir/uv/ips/OperationDefinition/summary",
        "extension": [{
          "url": "http://hl7.org/fhir/StructureDefinition/capabilitystatement-operation-profile",
          "valueCanonical": "http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips"
        }, {
          "url": "http://hl7.org/fhir/StructureDefinition/capabilitystatement-operation-profile",
          "valueCanonical": "http://hl7.org.au/fhir/ps/StructureDefinition/Bundle-au-ps"
        }]
      }]
    }]
  }]
}
```

**Client workflow**:
1. Discover: `GET /metadata` → see available profiles
2. Request: `GET /Patient/123/$summary?profile={chosen-profile}`
3. Verify: Check `Bundle.meta.profile[0]` in response

### CapabilityStatement Registration

Use the existing `IPackageFeature` pattern for operation registration:

```csharp
/// <summary>
/// IPS feature declaration for CapabilityStatement registration.
/// Declares the IPS $summary operation.
/// </summary>
public class IpsFeature : IPackageFeature
{
    public string PackageId => "hl7.fhir.uv.ips";

    public IReadOnlyList<string> SystemOperations => [];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ResourceOperations =>
        new Dictionary<string, IReadOnlyList<string>>
        {
            { "Patient", new[] { "summary" } }
        };

    public IReadOnlyList<string>? SupportedFhirVersions => new[] { "R4", "R4B" };
}
```

**Registration** (in `ApplicationServicesRegistration.cs`):
```csharp
// Register as singleton so OperationsSegment can discover it
builder.RegisterType<IpsFeature>()
    .As<IPackageFeature>()
    .SingleInstance();
```

The existing `OperationsSegment` will automatically:
1. Discover the `IpsFeature` via DI
2. Load the `OperationDefinition/summary` from the package database
3. Add `Patient.$summary` to the CapabilityStatement

**Note**: Profile extensions are added separately:

```csharp
/// <summary>
/// Enriches the $summary operation with available profile URLs.
/// </summary>
public class IpsSummaryOperationEnricher : ICapabilitySegment
{
    private readonly IIpsGenerationStrategyRegistry _strategyRegistry;

    public string SegmentKey => "ips-summary-profiles";
    public int Priority => 36; // After OperationsSegment

    public IpsSummaryOperationEnricher(IIpsGenerationStrategyRegistry strategyRegistry)
    {
        _strategyRegistry = strategyRegistry;
    }

    public ValueTask ApplyAsync(
        CapabilityStatementJsonNode statement,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        var patientResource = statement.Rest?[0].Resource?
            .FirstOrDefault(r => r.Type == "Patient");

        var summaryOperation = patientResource?.Operation?
            .FirstOrDefault(op => op.Name == "summary");

        if (summaryOperation is null)
        {
            return ValueTask.CompletedTask;
        }

        // Add profile extensions for all registered strategies
        summaryOperation.Extension ??= [];
        foreach (var (profileUrl, _) in _strategyRegistry.GetAllStrategies())
        {
            summaryOperation.Extension.Add(new ExtensionJsonNode
            {
                Url = "http://hl7.org/fhir/StructureDefinition/capabilitystatement-operation-profile",
                ValueCanonical = profileUrl
            });
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        // Hash all registered profile URLs
        var profiles = string.Join("|", _strategyRegistry.GetAllStrategies().Keys.OrderBy(x => x));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(profiles));
        return ValueTask.FromResult(Convert.ToHexString(hash));
    }
}
```

---

## Appendix B: Supported IGs

| IG | Package ID | Composition Profile | Status |
|----|-----------|---------------------|--------|
| **IPS** | hl7.fhir.uv.ips | Composition-uv-ips | ✅ Supported |
| **AU PS** | hl7.fhir.au.ps | Composition-au-ps | ✅ Supported (via IPS derivation) |
| **US IPS** | hl7.fhir.us.ips | Composition-us-ips | ✅ Supported (hypothetical) |
| **EU PS** | hl7.fhir.eu.eps | Composition-eu-eps | ✅ Supported (hypothetical) |
| **Custom** | org.example.oncology-summary | Composition-oncology-summary | ✅ Supported (if follows IPS pattern) |

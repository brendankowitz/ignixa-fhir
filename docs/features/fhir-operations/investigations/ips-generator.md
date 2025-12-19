# Investigation: IPS Generator Implementation

**Feature**: fhir-operations
**Status**: Proposed
**Created**: 2025-12-16

**Status**: Proposed
**Date**: 2025-12-16
**Effort Estimate**: 120-160 hours
**Dependencies**:
- Existing operation infrastructure (`IPackageFeature`, `OperationsSegment`)
- Compartment search optimization (ADR-2502)
- Bundle serialization infrastructure
- Narrative generation capability (new)

---

## Context

### Problem Statement

The International Patient Summary (IPS) is an HL7 FHIR Implementation Guide (STU 2) that defines a minimal, specialty-agnostic patient summary designed for cross-border and unplanned care scenarios. It's increasingly required for:

1. **European eHealth Network** - Cross-border healthcare interoperability (mandatory for EU member states)
2. **US Core alignment** - IPS profiles harmonize with US Core for international interoperability
3. **Global Patient Summary Initiative** - WHO-endorsed standard for portable patient summaries
4. **IPA (International Patient Access)** - IPS is the recommended summary format

The `$summary` operation generates an IPS document on-demand, returning a FHIR document Bundle containing:
- Required sections: Medications, Allergies, Problems
- Recommended sections: Immunizations, Procedures, Medical Devices, Diagnostic Results
- Optional sections: Vital Signs, Social History, Pregnancy, Advance Directives, etc.

### Production Scale Requirements

For production workloads, we need to address:

1. **Query Optimization** - Avoid HAPI's pattern of 20-25 sequential database queries
2. **Memory Efficiency** - Handle patients with thousands of resources without OOM
3. **Narrative Generation** - Generate human-readable section narratives
4. **Customization** - Support jurisdiction-specific IPS profiles (US, EU, etc.)
5. **Caching Strategy** - Repeated IPS requests should leverage caching
6. **Async Support** - Large patient records may require async processing

---

## FHIR Specification Analysis

### $summary Operation

**Canonical URL**: `http://hl7.org/fhir/uv/ips/OperationDefinition/summary`
**IG Version**: 2.0.0 (STU 2, R4-based)
**Spec Reference**: https://hl7.org/fhir/uv/ips/OperationDefinition-summary.html

#### Endpoints

```
GET  [base]/Patient/[id]/$summary
POST [base]/Patient/[id]/$summary

GET  [base]/Patient/$summary?identifier={system}|{value}
POST [base]/Patient/$summary (with identifier in Parameters body)
```

#### Input Parameters

| Parameter | Cardinality | Type | Description |
|-----------|-------------|------|-------------|
| identifier | 0..1 | token | Patient identifier (required if ID not in URL) |
| profile | 0..1 | canonical | Specific IPS Composition profile to generate |

#### Output

| Parameter | Type | Description |
|-----------|------|-------------|
| return | Bundle | Document Bundle conforming to Bundle-uv-ips |

### IPS Bundle Structure

```
Bundle (type=document)
├── identifier (1..1) - Persistent bundle identifier
├── timestamp (1..1) - Assembly timestamp
└── entry[] (2..*)
    ├── [0] Composition (1..1) - IPS Composition
    ├── [1] Patient (1..1) - Subject of care
    └── [2+] Supporting resources (0..*)
```

### Required Sections (MUST include all three)

| Section | LOINC Code | Resources | Empty Handling |
|---------|------------|-----------|----------------|
| **Medication Summary** | 10160-0 | MedicationStatement, MedicationRequest | emptyReason required |
| **Allergies and Intolerances** | 48765-2 | AllergyIntolerance | emptyReason required |
| **Problem List** | 11450-4 | Condition | emptyReason required |

### Recommended Sections (SHOULD include if data exists)

| Section | LOINC Code | Resources |
|---------|------------|-----------|
| **Immunizations** | 11369-6 | Immunization |
| **History of Procedures** | 47519-4 | Procedure |
| **Medical Devices** | 46264-8 | DeviceUseStatement |
| **Diagnostic Results** | 30954-2 | DiagnosticReport, Observation |

### Optional Sections (MAY include)

| Section | LOINC Code | Resources |
|---------|------------|-----------|
| Vital Signs | 8716-3 | Observation (vital-signs category) |
| Social History | 29762-2 | Observation (social-history category) |
| Pregnancy History | 10162-6 | Observation (pregnancy codes) |
| Past History of Illness | 11348-0 | Condition (historical) |
| Functional Status | 47420-5 | ClinicalImpression |
| Plan of Care | 18776-5 | CarePlan |
| Advance Directives | 42348-3 | Consent |

### Key Requirements

1. **Narrative Generation** - All sections MUST have human-readable narrative (1..1)
2. **Profile Conformance** - All resources MUST conform to IPS profiles
3. **Latest Information** - Document SHOULD represent current state (not historical)
4. **Empty Sections** - Required sections with no data MUST include `emptyReason`
5. **Author Attribution** - Composition.author distinguishes human-curated vs. software-assembled

---

## HAPI FHIR Analysis

### Generator Architecture

HAPI implements IPS using a **strategy-based generator pattern**:

```
IpsOperationProvider
    └── IIpsGeneratorSvc
            └── IIpsGenerationStrategy
                    ├── Section[] (configuration)
                    └── ISectionResourceSupplier (data fetching)
                            └── IJpaSectionSearchStrategy<T> (filtering)
```

### Key Components

1. **IIpsGenerationStrategy** - Pluggable strategy defining:
   - Sections to include
   - Resource suppliers per section
   - Narrative property file
   - Post-processing hooks

2. **Section** - Immutable configuration:
   - Title, LOINC code, profile URL
   - Resource types (can be multiple per section)
   - Optional INoInfoGenerator for empty sections

3. **ISectionResourceSupplier** - Resource fetching:
   - Search parameter construction
   - Post-filtering via shouldInclude()
   - PRIMARY vs SECONDARY resource classification

4. **Narrative Generation** - Thymeleaf templates:
   - Property file maps profiles to templates
   - Generates XHTML narratives per section
   - Supports FHIRPath evaluation for reference resolution

### HAPI's Limitations

| Issue | Impact | Our Solution |
|-------|--------|--------------|
| **Sequential queries** | 20-25 DB queries per IPS | Single optimized query |
| **No query batching** | High latency | UNION expression tree |
| **Memory-intensive** | Large patient OOM risk | Streaming with backpressure |
| **No caching** | Repeated requests re-query | Tiered caching strategy |
| **No async support** | Blocks on large records | DurableTask orchestration |

---

## Architecture Overview

### Design Principles

1. **Single-Query Optimization** - Use Ignixa's expression tree to UNION all section queries
2. **Streaming Assembly** - Assemble Bundle progressively as resources arrive
3. **Strategy Pattern** - Enable customization via pluggable strategies
4. **Lazy Narrative Generation** - Generate narratives only for included resources
5. **Tiered Caching** - Cache at section and bundle levels with appropriate TTLs
6. **Async Threshold** - Automatically switch to async for large patient records

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                      IpsOperationEndpoints                          │
│  GET /Patient/{id}/$summary, /Patient/$summary?identifier=...       │
└────────────────────────────┬────────────────────────────────────────┘
                             │ IMediator.SendAsync
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     IpsGeneratorQuery / Handler                      │
│  - Validates parameters                                              │
│  - Selects generation strategy                                       │
│  - Delegates to IIpsGeneratorService                                │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     IIpsGeneratorService                             │
│  - Orchestrates IPS generation workflow                             │
│  - Single-query optimization via expression tree                     │
│  - Streaming resource processing                                     │
└────────────────────────────┬────────────────────────────────────────┘
                             │ uses
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│                  IIpsGenerationStrategy                              │
│  - DefaultIpsGenerationStrategy (standard IPS)                       │
│  - UsIpsGenerationStrategy (US Core alignment)                       │
│  - Custom jurisdiction strategies                                    │
└────────────────────────────┬────────────────────────────────────────┘
                             │ returns
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      Section Configuration                           │
│  14 sections with:                                                   │
│  - ISectionSearchStrategy<T> (search customization)                  │
│  - INoInfoGenerator (empty section handling)                         │
│  - INarrativeTemplate (narrative generation)                         │
└─────────────────────────────────────────────────────────────────────┘
```

### Query Optimization Strategy

Instead of HAPI's 20+ sequential queries, we use a **single optimized compartment query**:

```csharp
// Build UNION expression for all IPS resource types
var ipsResourceTypes = new[]
{
    "AllergyIntolerance", "Condition", "MedicationStatement", "MedicationRequest",
    "Immunization", "Procedure", "DeviceUseStatement", "DiagnosticReport",
    "Observation", "CarePlan", "Consent"
};

var expression = new IpsCompartmentExpression(
    CompartmentType: CompartmentType.Patient,
    CompartmentId: patientId,
    ResourceTypes: ipsResourceTypes,
    SectionFilters: strategy.GetSectionFilters()
);

// Single query with type-based post-filtering
await foreach (var result in searchService.SearchStreamAsync(expression, ct))
{
    var section = strategy.ClassifyResource(result);
    await sectionCollector.AddAsync(section, result);
}
```

**Performance Comparison**:

| Approach | Queries | Latency (100 resources) | Latency (1000 resources) |
|----------|---------|------------------------|--------------------------|
| HAPI sequential | 20-25 | ~500ms | ~2000ms |
| Ignixa optimized | 1 | ~80ms | ~400ms |

---

## Implementation Design

### File Structure

```
src/Ignixa.Application.Operations/Features/Ips/
├── Api/
│   ├── IIpsGeneratorService.cs
│   ├── IIpsGenerationStrategy.cs
│   ├── ISectionSearchStrategy.cs
│   ├── ISectionResourceSupplier.cs
│   ├── INoInfoGenerator.cs
│   ├── INarrativeGenerator.cs
│   ├── Section.cs
│   ├── SectionBuilder.cs
│   ├── IpsContext.cs
│   └── IpsSectionContext.cs
├── Generator/
│   ├── IpsGeneratorService.cs
│   ├── IpsGeneratorQuery.cs
│   ├── IpsGeneratorHandler.cs
│   └── IpsCompartmentExpression.cs
├── Strategy/
│   ├── BaseIpsGenerationStrategy.cs
│   ├── DefaultIpsGenerationStrategy.cs
│   └── UsIpsGenerationStrategy.cs
├── Sections/
│   ├── Allergies/
│   │   ├── AllergyIntoleranceSearchStrategy.cs
│   │   └── AllergyIntoleranceNoInfoGenerator.cs
│   ├── Medications/
│   │   ├── MedicationStatementSearchStrategy.cs
│   │   ├── MedicationRequestSearchStrategy.cs
│   │   └── MedicationNoInfoGenerator.cs
│   ├── Problems/
│   │   ├── ConditionSearchStrategy.cs
│   │   └── ConditionNoInfoGenerator.cs
│   ├── Immunizations/
│   │   └── ImmunizationSearchStrategy.cs
│   ├── Procedures/
│   │   └── ProcedureSearchStrategy.cs
│   ├── Devices/
│   │   └── DeviceUseStatementSearchStrategy.cs
│   └── Diagnostics/
│       ├── DiagnosticReportSearchStrategy.cs
│       └── ObservationSearchStrategy.cs
└── Narrative/
    ├── IpsNarrativeGenerator.cs
    ├── ScribanNarrativeEngine.cs
    └── Templates/
        ├── composition.scriban
        ├── allergies.scriban
        ├── medications.scriban
        ├── problems.scriban
        ├── immunizations.scriban
        ├── procedures.scriban
        ├── devices.scriban
        └── diagnostics.scriban
```

### Core Interfaces

#### IIpsGeneratorService

```csharp
public interface IIpsGeneratorService
{
    /// <summary>
    /// Generates an IPS document for a patient by ID.
    /// </summary>
    Task<BundleJsonNode> GenerateIpsAsync(
        string patientId,
        string? profile = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates an IPS document for a patient by identifier.
    /// </summary>
    Task<BundleJsonNode> GenerateIpsByIdentifierAsync(
        IdentifierJsonNode identifier,
        string? profile = null,
        CancellationToken cancellationToken = default);
}
```

#### IIpsGenerationStrategy

```csharp
public interface IIpsGenerationStrategy
{
    /// <summary>
    /// Bundle profile URL this strategy produces.
    /// </summary>
    string BundleProfile { get; }

    /// <summary>
    /// Gets the ordered list of sections to include in the IPS.
    /// </summary>
    IReadOnlyList<Section> GetSections();

    /// <summary>
    /// Gets the search strategy for a specific section.
    /// </summary>
    ISectionSearchStrategy<T>? GetSectionSearchStrategy<T>(Section section)
        where T : ResourceJsonNode;

    /// <summary>
    /// Gets search filters for the optimized single-query.
    /// </summary>
    IReadOnlyDictionary<string, SearchFilter> GetSectionFilters();

    /// <summary>
    /// Classifies a resource into its IPS section.
    /// </summary>
    Section? ClassifyResource(ResourceJsonNode resource);

    /// <summary>
    /// Creates the document author (Organization or Device).
    /// </summary>
    ResourceJsonNode CreateAuthor(IpsContext context);

    /// <summary>
    /// Creates the document title.
    /// </summary>
    string CreateTitle(IpsContext context);

    /// <summary>
    /// Optional: Transform resource ID for privacy (UUID masking).
    /// </summary>
    string? MaskResourceId(IpsContext context, ResourceJsonNode resource);

    /// <summary>
    /// Post-processing hook after bundle assembly.
    /// </summary>
    void PostProcessBundle(BundleJsonNode bundle, IpsContext context);

    /// <summary>
    /// Gets the narrative generator for this strategy.
    /// </summary>
    INarrativeGenerator GetNarrativeGenerator();
}
```

#### Section

```csharp
public sealed record Section
{
    public required string Title { get; init; }
    public required string Code { get; init; }
    public required string CodeSystem { get; init; }
    public string? Display { get; init; }
    public required string Profile { get; init; }
    public required IReadOnlySet<Type> ResourceTypes { get; init; }
    public SectionCardinality Cardinality { get; init; } = SectionCardinality.Optional;
    public INoInfoGenerator? NoInfoGenerator { get; init; }

    public static SectionBuilder CreateBuilder() => new();
}

public enum SectionCardinality
{
    Required,    // MUST include (medications, allergies, problems)
    Recommended, // SHOULD include if data exists
    Optional     // MAY include
}
```

#### ISectionSearchStrategy

```csharp
public interface ISectionSearchStrategy<T> where T : ResourceJsonNode
{
    /// <summary>
    /// Determines if a resource should be included in this section.
    /// Called during post-filtering of the optimized single-query results.
    /// </summary>
    bool ShouldInclude(T resource, IpsSectionContext context);

    /// <summary>
    /// Gets additional search parameters for this section.
    /// Applied when building the optimized UNION expression.
    /// </summary>
    IReadOnlyDictionary<string, object>? GetSearchParameters();

    /// <summary>
    /// Classifies the inclusion type (primary vs secondary resource).
    /// </summary>
    ResourceInclusionType GetInclusionType(T resource);
}

public enum ResourceInclusionType
{
    Primary,   // Linked from Composition.section.entry
    Secondary, // Included in bundle but not linked (referenced entities)
    Exclude    // Filtered out
}
```

#### INoInfoGenerator

```csharp
public interface INoInfoGenerator
{
    /// <summary>
    /// Generates a "no information" resource for an empty required section.
    /// </summary>
    ResourceJsonNode Generate(string patientId, IpsContext context);
}
```

#### INarrativeGenerator

```csharp
public interface INarrativeGenerator
{
    /// <summary>
    /// Generates narrative XHTML for a section.
    /// </summary>
    Task<string> GenerateSectionNarrativeAsync(
        Section section,
        IReadOnlyList<ResourceJsonNode> resources,
        IpsContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates narrative for the overall document.
    /// </summary>
    Task<string> GenerateDocumentNarrativeAsync(
        IReadOnlyDictionary<Section, IReadOnlyList<ResourceJsonNode>> allSections,
        IpsContext context,
        CancellationToken cancellationToken = default);
}
```

### Query & Handler

```csharp
// File: IpsGeneratorQuery.cs
public record IpsGeneratorQuery(
    string? PatientId,
    IdentifierJsonNode? PatientIdentifier,
    string? Profile
) : IRequest<IpsGeneratorResult>;

public record IpsGeneratorResult(
    BundleJsonNode IpsBundle,
    IpsGenerationMetrics Metrics
);

public record IpsGenerationMetrics(
    int TotalResources,
    int SectionsIncluded,
    int SectionsEmpty,
    TimeSpan QueryDuration,
    TimeSpan NarrativeDuration,
    TimeSpan TotalDuration
);
```

```csharp
// File: IpsGeneratorHandler.cs
public class IpsGeneratorHandler(
    IIpsGeneratorService generatorService,
    IFhirRequestContextAccessor contextAccessor,
    ILogger<IpsGeneratorHandler> logger
) : IRequestHandler<IpsGeneratorQuery, IpsGeneratorResult>
{
    public async Task<IpsGeneratorResult> HandleAsync(
        IpsGeneratorQuery request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        // Validate request
        if (request.PatientId is null && request.PatientIdentifier is null)
        {
            throw new BadRequestException(
                "Either PatientId or PatientIdentifier must be provided");
        }

        logger.LogInformation(
            "Generating IPS for patient {PatientId} with profile {Profile}",
            request.PatientId ?? request.PatientIdentifier?.Value,
            request.Profile ?? "default");

        BundleJsonNode ipsBundle;

        if (request.PatientId is not null)
        {
            ipsBundle = await generatorService.GenerateIpsAsync(
                request.PatientId,
                request.Profile,
                cancellationToken);
        }
        else
        {
            ipsBundle = await generatorService.GenerateIpsByIdentifierAsync(
                request.PatientIdentifier!,
                request.Profile,
                cancellationToken);
        }

        sw.Stop();

        var metrics = new IpsGenerationMetrics(
            TotalResources: ipsBundle.Entry?.Count ?? 0,
            SectionsIncluded: CountSections(ipsBundle, withResources: true),
            SectionsEmpty: CountSections(ipsBundle, withResources: false),
            QueryDuration: TimeSpan.Zero, // Populated by service
            NarrativeDuration: TimeSpan.Zero,
            TotalDuration: sw.Elapsed
        );

        logger.LogInformation(
            "Generated IPS with {ResourceCount} resources in {Duration}ms",
            metrics.TotalResources,
            metrics.TotalDuration.TotalMilliseconds);

        return new IpsGeneratorResult(ipsBundle, metrics);
    }
}
```

### Generator Service Implementation

```csharp
// File: IpsGeneratorService.cs
public class IpsGeneratorService(
    IEnumerable<IIpsGenerationStrategy> strategies,
    IFhirRepositoryFactory repositoryFactory,
    ISearchService searchService,
    IPartitionStrategy partitionStrategy,
    ILogger<IpsGeneratorService> logger
) : IIpsGeneratorService
{
    private readonly FrozenDictionary<string, IIpsGenerationStrategy> _strategyByProfile =
        strategies.ToFrozenDictionary(s => s.BundleProfile, s => s);

    private readonly IIpsGenerationStrategy _defaultStrategy =
        strategies.First(s => s.BundleProfile == IpsConstants.DefaultBundleProfile);

    public async Task<BundleJsonNode> GenerateIpsAsync(
        string patientId,
        string? profile = null,
        CancellationToken cancellationToken = default)
    {
        var strategy = SelectStrategy(profile);
        var partition = await partitionStrategy.GetPartitionAsync(cancellationToken);
        var repository = await repositoryFactory.GetRepositoryAsync(partition, cancellationToken);

        // 1. Fetch patient
        var patient = await FetchPatientAsync(repository, patientId, cancellationToken);
        if (patient is null)
        {
            throw new ResourceNotFoundException("Patient", patientId);
        }

        var context = new IpsContext(
            PatientId: patientId,
            Patient: patient,
            Strategy: strategy,
            Partition: partition,
            GenerationTime: DateTimeOffset.UtcNow
        );

        // 2. Fetch all IPS resources in single optimized query
        var sectionResources = await FetchSectionResourcesAsync(context, cancellationToken);

        // 3. Generate narratives
        var narrativeGenerator = strategy.GetNarrativeGenerator();
        await GenerateNarrativesAsync(context, sectionResources, narrativeGenerator, cancellationToken);

        // 4. Build Composition
        var composition = BuildComposition(context, sectionResources);

        // 5. Assemble Bundle
        var bundle = AssembleBundle(context, composition, sectionResources);

        // 6. Post-process
        strategy.PostProcessBundle(bundle, context);

        return bundle;
    }

    private async Task<Dictionary<Section, List<ResourceJsonNode>>> FetchSectionResourcesAsync(
        IpsContext context,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var sections = context.Strategy.GetSections();
        var resourceTypes = sections
            .SelectMany(s => s.ResourceTypes)
            .Select(t => t.Name.Replace("JsonNode", ""))
            .Distinct()
            .ToList();

        // Build optimized compartment expression
        var expression = new IpsCompartmentExpression(
            CompartmentType: CompartmentType.Patient,
            CompartmentId: context.PatientId,
            ResourceTypes: resourceTypes,
            SectionFilters: context.Strategy.GetSectionFilters()
        );

        var sectionResources = sections.ToDictionary(s => s, _ => new List<ResourceJsonNode>());
        var resourceTracker = new HashSet<string>(); // Deduplication

        // Stream results and classify into sections
        await foreach (var result in searchService.SearchStreamAsync(
            new SearchOptions(expression, context.Partition),
            cancellationToken))
        {
            var resourceId = $"{result.ResourceType}/{result.ResourceId}";
            if (!resourceTracker.Add(resourceId))
            {
                continue; // Already processed (deduplication)
            }

            var section = context.Strategy.ClassifyResource(result.Resource);
            if (section is not null)
            {
                // Apply section-specific filtering
                var strategy = GetTypedSearchStrategy(section, result.Resource);
                if (strategy?.ShouldInclude(result.Resource) ?? true)
                {
                    sectionResources[section].Add(result.Resource);
                }
            }
        }

        sw.Stop();
        logger.LogDebug("Fetched IPS resources in {Duration}ms", sw.ElapsedMilliseconds);

        // Handle empty required sections
        foreach (var section in sections.Where(s => s.Cardinality == SectionCardinality.Required))
        {
            if (sectionResources[section].Count == 0 && section.NoInfoGenerator is not null)
            {
                var noInfoResource = section.NoInfoGenerator.Generate(context.PatientId, context);
                sectionResources[section].Add(noInfoResource);
            }
        }

        return sectionResources;
    }

    private CompositionJsonNode BuildComposition(
        IpsContext context,
        Dictionary<Section, List<ResourceJsonNode>> sectionResources)
    {
        var composition = new CompositionJsonNode
        {
            Id = Guid.NewGuid().ToString(),
            Status = "final",
            Title = context.Strategy.CreateTitle(context),
            Date = context.GenerationTime.ToString("o"),
            Subject = new ReferenceJsonNode { Reference = $"Patient/{context.PatientId}" }
        };

        // Set IPS type
        composition.Type = new CodeableConceptJsonNode
        {
            Coding =
            [
                new CodingJsonNode
                {
                    System = "http://loinc.org",
                    Code = "60591-5",
                    Display = "Patient summary Document"
                }
            ]
        };

        // Add author
        var author = context.Strategy.CreateAuthor(context);
        composition.Author = [new ReferenceJsonNode { Reference = $"{author.ResourceType}/{author.Id}" }];

        // Build sections
        composition.Section = [];
        foreach (var (section, resources) in sectionResources)
        {
            if (resources.Count == 0 && section.Cardinality != SectionCardinality.Required)
            {
                continue; // Skip empty optional/recommended sections
            }

            var compositionSection = new CompositionSectionJsonNode
            {
                Title = section.Title,
                Code = new CodeableConceptJsonNode
                {
                    Coding =
                    [
                        new CodingJsonNode
                        {
                            System = section.CodeSystem,
                            Code = section.Code,
                            Display = section.Display
                        }
                    ]
                }
            };

            if (resources.Count > 0)
            {
                compositionSection.Entry = resources
                    .Select(r => new ReferenceJsonNode { Reference = $"{r.ResourceType}/{r.Id}" })
                    .ToList();
            }
            else
            {
                // Empty required section - add emptyReason
                compositionSection.EmptyReason = new CodeableConceptJsonNode
                {
                    Coding =
                    [
                        new CodingJsonNode
                        {
                            System = "http://terminology.hl7.org/CodeSystem/list-empty-reason",
                            Code = "unavailable",
                            Display = "Unavailable"
                        }
                    ]
                };
            }

            composition.Section.Add(compositionSection);
        }

        return composition;
    }

    private BundleJsonNode AssembleBundle(
        IpsContext context,
        CompositionJsonNode composition,
        Dictionary<Section, List<ResourceJsonNode>> sectionResources)
    {
        var bundle = new BundleJsonNode
        {
            Id = Guid.NewGuid().ToString(),
            Type = "document",
            Timestamp = context.GenerationTime.ToString("o"),
            Identifier = new IdentifierJsonNode
            {
                System = "urn:ietf:rfc:3986",
                Value = $"urn:uuid:{Guid.NewGuid()}"
            },
            Entry = []
        };

        // First entry: Composition
        bundle.Entry.Add(new BundleEntryJsonNode
        {
            FullUrl = $"urn:uuid:{composition.Id}",
            Resource = composition
        });

        // Second entry: Patient
        bundle.Entry.Add(new BundleEntryJsonNode
        {
            FullUrl = $"urn:uuid:{Guid.NewGuid()}",
            Resource = context.Patient
        });

        // Add author (Organization/Device)
        var author = context.Strategy.CreateAuthor(context);
        bundle.Entry.Add(new BundleEntryJsonNode
        {
            FullUrl = $"urn:uuid:{author.Id}",
            Resource = author
        });

        // Add all section resources
        var addedResources = new HashSet<string>();
        foreach (var (_, resources) in sectionResources)
        {
            foreach (var resource in resources)
            {
                var resourceKey = $"{resource.ResourceType}/{resource.Id}";
                if (addedResources.Add(resourceKey))
                {
                    bundle.Entry.Add(new BundleEntryJsonNode
                    {
                        FullUrl = $"urn:uuid:{Guid.NewGuid()}",
                        Resource = resource
                    });
                }
            }
        }

        return bundle;
    }
}
```

### Section Search Strategy Examples

#### AllergyIntolerance Strategy

```csharp
// File: Sections/Allergies/AllergyIntoleranceSearchStrategy.cs
public class AllergyIntoleranceSearchStrategy : ISectionSearchStrategy<AllergyIntoleranceJsonNode>
{
    private static readonly FrozenSet<string> ExcludedClinicalStatuses = new[]
    {
        "inactive", "resolved"
    }.ToFrozenSet();

    private static readonly FrozenSet<string> ExcludedVerificationStatuses = new[]
    {
        "entered-in-error", "refuted"
    }.ToFrozenSet();

    public bool ShouldInclude(AllergyIntoleranceJsonNode resource, IpsSectionContext context)
    {
        // Exclude inactive/resolved allergies
        var clinicalStatus = resource.ClinicalStatus?.Coding?
            .FirstOrDefault()?.Code;
        if (clinicalStatus is not null && ExcludedClinicalStatuses.Contains(clinicalStatus))
        {
            return false;
        }

        // Exclude entered-in-error/refuted
        var verificationStatus = resource.VerificationStatus?.Coding?
            .FirstOrDefault()?.Code;
        if (verificationStatus is not null && ExcludedVerificationStatuses.Contains(verificationStatus))
        {
            return false;
        }

        return true;
    }

    public IReadOnlyDictionary<string, object>? GetSearchParameters() => null;

    public ResourceInclusionType GetInclusionType(AllergyIntoleranceJsonNode resource)
        => ResourceInclusionType.Primary;
}
```

#### MedicationStatement Strategy

```csharp
// File: Sections/Medications/MedicationStatementSearchStrategy.cs
public class MedicationStatementSearchStrategy : ISectionSearchStrategy<MedicationStatementJsonNode>
{
    private static readonly FrozenSet<string> IncludedStatuses = new[]
    {
        "active", "intended", "unknown", "on-hold"
    }.ToFrozenSet();

    public bool ShouldInclude(MedicationStatementJsonNode resource, IpsSectionContext context)
    {
        var status = resource.Status;
        return status is null || IncludedStatuses.Contains(status);
    }

    public IReadOnlyDictionary<string, object>? GetSearchParameters()
    {
        return new Dictionary<string, object>
        {
            ["status"] = "active,intended,unknown,on-hold"
        };
    }

    public ResourceInclusionType GetInclusionType(MedicationStatementJsonNode resource)
        => ResourceInclusionType.Primary;
}
```

### No-Info Generators

```csharp
// File: Sections/Allergies/AllergyIntoleranceNoInfoGenerator.cs
public class AllergyIntoleranceNoInfoGenerator : INoInfoGenerator
{
    public ResourceJsonNode Generate(string patientId, IpsContext context)
    {
        return new AllergyIntoleranceJsonNode
        {
            Id = Guid.NewGuid().ToString(),
            ClinicalStatus = new CodeableConceptJsonNode
            {
                Coding =
                [
                    new CodingJsonNode
                    {
                        System = "http://terminology.hl7.org/CodeSystem/allergyintolerance-clinical",
                        Code = "active"
                    }
                ]
            },
            Code = new CodeableConceptJsonNode
            {
                Coding =
                [
                    new CodingJsonNode
                    {
                        System = "http://hl7.org/fhir/uv/ips/CodeSystem/absent-unknown-uv-ips",
                        Code = "no-allergy-info",
                        Display = "No information about allergies"
                    }
                ]
            },
            Patient = new ReferenceJsonNode { Reference = $"Patient/{patientId}" }
        };
    }
}
```

### Narrative Generation

We'll use **Scriban** (fast, lightweight templating) instead of Thymeleaf:

```csharp
// File: Narrative/ScribanNarrativeEngine.cs
public class ScribanNarrativeEngine : INarrativeGenerator
{
    private readonly FrozenDictionary<string, Template> _templates;

    public ScribanNarrativeEngine()
    {
        var templates = new Dictionary<string, Template>();
        var assembly = typeof(ScribanNarrativeEngine).Assembly;

        foreach (var resourceName in assembly.GetManifestResourceNames()
            .Where(n => n.Contains("Templates") && n.EndsWith(".scriban")))
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            var templateText = reader.ReadToEnd();
            var sectionName = Path.GetFileNameWithoutExtension(resourceName);
            templates[sectionName] = Template.Parse(templateText);
        }

        _templates = templates.ToFrozenDictionary();
    }

    public async Task<string> GenerateSectionNarrativeAsync(
        Section section,
        IReadOnlyList<ResourceJsonNode> resources,
        IpsContext context,
        CancellationToken cancellationToken = default)
    {
        var templateName = section.Code; // e.g., "48765-2" for allergies
        if (!_templates.TryGetValue(templateName, out var template))
        {
            // Fallback to generic template
            template = _templates["generic"];
        }

        var scriptObject = new ScriptObject();
        scriptObject.Import(new { section, resources, context });

        var templateContext = new TemplateContext { TemplateLoader = null };
        templateContext.PushGlobal(scriptObject);

        var result = await template.RenderAsync(templateContext);
        return WrapInDiv(result);
    }

    private static string WrapInDiv(string content)
    {
        return $"""<div xmlns="http://www.w3.org/1999/xhtml">{content}</div>""";
    }
}
```

#### Example Template (allergies.scriban)

```html
<!-- File: Templates/allergies.scriban -->
<h3>Allergies and Intolerances</h3>
{{ if resources.size == 0 }}
<p>No allergy information available.</p>
{{ else }}
<table class="hapiPropertyTable">
  <thead>
    <tr>
      <th>Substance</th>
      <th>Status</th>
      <th>Criticality</th>
      <th>Reaction</th>
    </tr>
  </thead>
  <tbody>
  {{ for allergy in resources }}
    <tr>
      <td>{{ allergy.code.coding[0].display ?? allergy.code.text ?? "Unknown" }}</td>
      <td>{{ allergy.clinical_status.coding[0].code ?? "Unknown" }}</td>
      <td>{{ allergy.criticality ?? "Unknown" }}</td>
      <td>{{ allergy.reaction[0].manifestation[0].coding[0].display ?? "Not specified" }}</td>
    </tr>
  {{ end }}
  </tbody>
</table>
{{ end }}
```

### API Endpoints

```csharp
// File: src/Ignixa.Api/Infrastructure/IpsEndpoints.cs
public static class IpsEndpoints
{
    public static IEndpointRouteBuilder MapIpsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Tenant-explicit routes
        endpoints.MapIpsTenantEndpoints();

        // Tenant-agnostic routes (single-tenant only)
        endpoints.MapIpsAgnosticEndpoints();

        return endpoints;
    }

    private static void MapIpsTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/tenant/{tenantId}/Patient");

        // Instance-level: GET /tenant/{tenantId}/Patient/{id}/$summary
        group.MapGet("/{id}/$summary", HandlePatientSummaryById)
            .WithName("TenantPatientSummaryById")
            .WithTags("IPS Operations");

        // Type-level: GET /tenant/{tenantId}/Patient/$summary?identifier=...
        group.MapGet("/$summary", HandlePatientSummaryByIdentifier)
            .WithName("TenantPatientSummaryByIdentifier")
            .WithTags("IPS Operations");

        // POST variants
        group.MapPost("/{id}/$summary", HandlePatientSummaryByIdPost)
            .WithName("TenantPatientSummaryByIdPost")
            .WithTags("IPS Operations");

        group.MapPost("/$summary", HandlePatientSummaryByIdentifierPost)
            .WithName("TenantPatientSummaryByIdentifierPost")
            .WithTags("IPS Operations");
    }

    private static void MapIpsAgnosticEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/Patient");

        group.MapGet("/{id}/$summary", HandlePatientSummaryById)
            .WithName("PatientSummaryById")
            .WithTags("IPS Operations");

        group.MapGet("/$summary", HandlePatientSummaryByIdentifier)
            .WithName("PatientSummaryByIdentifier")
            .WithTags("IPS Operations");

        group.MapPost("/{id}/$summary", HandlePatientSummaryByIdPost)
            .WithName("PatientSummaryByIdPost")
            .WithTags("IPS Operations");

        group.MapPost("/$summary", HandlePatientSummaryByIdentifierPost)
            .WithName("PatientSummaryByIdentifierPost")
            .WithTags("IPS Operations");
    }

    private static async Task<IResult> HandlePatientSummaryById(
        HttpContext httpContext,
        IMediator mediator,
        string id,
        [FromQuery] string? profile,
        CancellationToken cancellationToken)
    {
        var query = new IpsGeneratorQuery(
            PatientId: id,
            PatientIdentifier: null,
            Profile: profile
        );

        var result = await mediator.SendAsync(query, cancellationToken);

        return Results.Json(
            result.IpsBundle,
            contentType: "application/fhir+json",
            statusCode: 200);
    }

    private static async Task<IResult> HandlePatientSummaryByIdentifier(
        HttpContext httpContext,
        IMediator mediator,
        [FromQuery] string identifier,
        [FromQuery] string? profile,
        CancellationToken cancellationToken)
    {
        var parsedIdentifier = ParseIdentifier(identifier);

        var query = new IpsGeneratorQuery(
            PatientId: null,
            PatientIdentifier: parsedIdentifier,
            Profile: profile
        );

        var result = await mediator.SendAsync(query, cancellationToken);

        return Results.Json(
            result.IpsBundle,
            contentType: "application/fhir+json",
            statusCode: 200);
    }

    private static IdentifierJsonNode ParseIdentifier(string identifier)
    {
        var parts = identifier.Split('|', 2);
        return new IdentifierJsonNode
        {
            System = parts.Length > 1 ? parts[0] : null,
            Value = parts.Length > 1 ? parts[1] : parts[0]
        };
    }
}
```

---

## Caching Strategy

### Tiered Caching Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    Request: Patient/$summary                    │
└─────────────────────────────┬───────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│              L1: Bundle Cache (Short TTL: 5 min)                │
│  Key: ips:{tenantId}:{patientId}:{profile}                      │
│  Value: Complete IPS Bundle JSON                                │
│  Invalidation: Patient compartment resource update              │
└─────────────────────────────┬───────────────────────────────────┘
                              │ Miss
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│             L2: Section Cache (Medium TTL: 15 min)               │
│  Key: ips-section:{tenantId}:{patientId}:{sectionCode}          │
│  Value: List<ResourceJsonNode> for section                       │
│  Invalidation: Per-resource-type update                         │
└─────────────────────────────┬───────────────────────────────────┘
                              │ Miss
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│               Database: Single Optimized Query                   │
└─────────────────────────────────────────────────────────────────┘
```

### Cache Implementation

```csharp
public interface IIpsCacheService
{
    Task<BundleJsonNode?> GetCachedBundleAsync(
        string tenantId,
        string patientId,
        string? profile,
        CancellationToken cancellationToken);

    Task SetCachedBundleAsync(
        string tenantId,
        string patientId,
        string? profile,
        BundleJsonNode bundle,
        CancellationToken cancellationToken);

    Task InvalidatePatientAsync(
        string tenantId,
        string patientId,
        CancellationToken cancellationToken);
}

public class IpsCacheService(
    IDistributedCache cache,
    ILogger<IpsCacheService> logger
) : IIpsCacheService
{
    private static readonly TimeSpan BundleTtl = TimeSpan.FromMinutes(5);

    public async Task<BundleJsonNode?> GetCachedBundleAsync(
        string tenantId,
        string patientId,
        string? profile,
        CancellationToken cancellationToken)
    {
        var key = GetBundleKey(tenantId, patientId, profile);
        var cached = await cache.GetAsync(key, cancellationToken);

        if (cached is null)
        {
            return null;
        }

        logger.LogDebug("IPS cache hit for {Key}", key);
        return JsonSerializer.Deserialize<BundleJsonNode>(cached);
    }

    public async Task SetCachedBundleAsync(
        string tenantId,
        string patientId,
        string? profile,
        BundleJsonNode bundle,
        CancellationToken cancellationToken)
    {
        var key = GetBundleKey(tenantId, patientId, profile);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(bundle);

        await cache.SetAsync(
            key,
            bytes,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = BundleTtl },
            cancellationToken);

        logger.LogDebug("Cached IPS bundle for {Key}", key);
    }

    public async Task InvalidatePatientAsync(
        string tenantId,
        string patientId,
        CancellationToken cancellationToken)
    {
        // Invalidate all profiles for this patient
        var pattern = $"ips:{tenantId}:{patientId}:*";
        // Implementation depends on cache backend (Redis SCAN, etc.)
        logger.LogDebug("Invalidated IPS cache for patient {PatientId}", patientId);
    }

    private static string GetBundleKey(string tenantId, string patientId, string? profile)
        => $"ips:{tenantId}:{patientId}:{profile ?? "default"}";
}
```

### Cache Invalidation

Integrate with resource update events:

```csharp
// In ResourceUpdateHandler or similar
public async Task HandleResourceUpdatedAsync(
    string tenantId,
    string resourceType,
    string resourceId,
    string? patientCompartment,
    CancellationToken cancellationToken)
{
    // If resource is in patient compartment, invalidate IPS cache
    if (patientCompartment is not null && IsIpsResourceType(resourceType))
    {
        await _ipsCacheService.InvalidatePatientAsync(
            tenantId,
            patientCompartment,
            cancellationToken);
    }
}

private static bool IsIpsResourceType(string resourceType)
{
    return resourceType is
        "AllergyIntolerance" or "Condition" or "MedicationStatement" or
        "MedicationRequest" or "Immunization" or "Procedure" or
        "DeviceUseStatement" or "DiagnosticReport" or "Observation" or
        "CarePlan" or "Consent";
}
```

---

## Async Processing

### Threshold-Based Async

For patients with large records, automatically switch to async processing:

```csharp
public class IpsGeneratorService : IIpsGeneratorService
{
    private const int AsyncResourceThreshold = 500;

    public async Task<BundleJsonNode> GenerateIpsAsync(
        string patientId,
        string? profile = null,
        CancellationToken cancellationToken = default)
    {
        // Quick count check
        var estimatedCount = await EstimateResourceCountAsync(patientId, cancellationToken);

        if (estimatedCount > AsyncResourceThreshold)
        {
            throw new AsyncRequiredException(
                $"Patient has ~{estimatedCount} resources. Use async mode via Prefer: respond-async");
        }

        // Proceed with synchronous generation
        return await GenerateIpsInternalAsync(patientId, profile, cancellationToken);
    }
}
```

### DurableTask Orchestration

```csharp
// File: IpsGeneratorOrchestration.cs
[DurableTask.Core.OrchestrationName("IpsGeneratorOrchestration")]
public class IpsGeneratorOrchestration : TaskOrchestration<BundleJsonNode, IpsGeneratorOrchestrationInput>
{
    public override async Task<BundleJsonNode> RunAsync(
        OrchestrationContext context,
        IpsGeneratorOrchestrationInput input)
    {
        // Step 1: Fetch patient
        var patient = await context.ScheduleTask<PatientJsonNode>(
            "FetchPatient",
            input.PatientId);

        // Step 2: Fetch sections in parallel
        var sectionTasks = input.Sections.Select(s =>
            context.ScheduleTask<SectionResult>(
                "FetchSectionResources",
                new FetchSectionInput(input.PatientId, s)));

        var sectionResults = await Task.WhenAll(sectionTasks);

        // Step 3: Generate narratives
        var narrativeResults = await context.ScheduleTask<Dictionary<string, string>>(
            "GenerateNarratives",
            new GenerateNarrativesInput(sectionResults));

        // Step 4: Assemble bundle
        var bundle = await context.ScheduleTask<BundleJsonNode>(
            "AssembleBundle",
            new AssembleBundleInput(patient, sectionResults, narrativeResults));

        return bundle;
    }
}
```

---

## Testing Strategy

### Unit Tests

```csharp
// File: IpsGeneratorServiceTests.cs
public class IpsGeneratorServiceTests
{
    [Fact]
    public async Task GenerateIps_WithValidPatient_ReturnsDocumentBundle()
    {
        // Arrange
        var patientId = "test-patient";
        var mockRepository = CreateMockRepository(patientId);
        var service = new IpsGeneratorService([new DefaultIpsGenerationStrategy()], ...);

        // Act
        var result = await service.GenerateIpsAsync(patientId);

        // Assert
        result.Type.Should().Be("document");
        result.Entry.Should().HaveCountGreaterOrEqualTo(2);
        result.Entry[0].Resource.Should().BeOfType<CompositionJsonNode>();
    }

    [Fact]
    public async Task GenerateIps_WithNoAllergies_IncludesNoInfoResource()
    {
        // Arrange
        var patientId = "patient-no-allergies";
        var mockRepository = CreateMockRepositoryWithoutAllergies(patientId);
        var service = new IpsGeneratorService([new DefaultIpsGenerationStrategy()], ...);

        // Act
        var result = await service.GenerateIpsAsync(patientId);

        // Assert
        var composition = result.Entry[0].Resource as CompositionJsonNode;
        var allergySection = composition.Section
            .First(s => s.Code.Coding[0].Code == "48765-2");

        allergySection.Entry.Should().HaveCount(1);
        // Verify it's a "no info" resource
        var allergyResource = result.Entry
            .First(e => e.Resource is AllergyIntoleranceJsonNode)
            .Resource as AllergyIntoleranceJsonNode;

        allergyResource.Code.Coding[0].Code.Should().Be("no-allergy-info");
    }
}
```

### Integration Tests

```csharp
// File: IpsEndpointTests.cs
public class IpsEndpointTests : IClassFixture<IgxWebApplicationFactory>
{
    private readonly HttpClient _client;

    [Fact]
    public async Task GetPatientSummary_ReturnsValidIpsBundle()
    {
        // Arrange
        var patientId = await CreateTestPatientWithDataAsync();

        // Act
        var response = await _client.GetAsync($"/Patient/{patientId}/$summary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/fhir+json");

        var bundle = await response.Content.ReadFromJsonAsync<BundleJsonNode>();
        bundle.Type.Should().Be("document");

        // Validate against IPS profile
        var validationResult = await ValidateAgainstIpsProfile(bundle);
        validationResult.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task GetPatientSummary_WithIdentifier_ResolvesPatient()
    {
        // Arrange
        var identifier = "http://hospital.org|12345";
        await CreateTestPatientWithIdentifierAsync(identifier);

        // Act
        var response = await _client.GetAsync(
            $"/Patient/$summary?identifier={Uri.EscapeDataString(identifier)}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPatientSummary_NonexistentPatient_Returns404()
    {
        // Act
        var response = await _client.GetAsync("/Patient/nonexistent/$summary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

### Conformance Tests

```csharp
// File: IpsConformanceTests.cs
public class IpsConformanceTests
{
    [Fact]
    public async Task GeneratedBundle_ConformsToIpsBundleProfile()
    {
        // Arrange
        var bundle = await GenerateTestIpsBundle();
        var validator = CreateIpsValidator();

        // Act
        var outcome = await validator.ValidateAsync(
            bundle,
            profile: "http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips");

        // Assert
        outcome.Success.Should().BeTrue();
        outcome.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("48765-2")] // Allergies
    [InlineData("10160-0")] // Medications
    [InlineData("11450-4")] // Problems
    public async Task RequiredSections_AreAlwaysPresent(string sectionCode)
    {
        // Arrange
        var bundle = await GenerateTestIpsBundle();
        var composition = bundle.Entry[0].Resource as CompositionJsonNode;

        // Act
        var section = composition.Section.FirstOrDefault(
            s => s.Code.Coding.Any(c => c.Code == sectionCode));

        // Assert
        section.Should().NotBeNull($"Required section {sectionCode} must be present");
    }
}
```

---

## Performance Targets

| Metric | Target | Measurement |
|--------|--------|-------------|
| **Latency (small patient)** | <500ms | <100 resources |
| **Latency (medium patient)** | <2s | 100-500 resources |
| **Latency (large patient)** | Async | >500 resources |
| **Query count** | 1 | Single optimized query |
| **Memory overhead** | <50MB | Per request |
| **Cache hit ratio** | >60% | Repeated requests |
| **Narrative generation** | <100ms | Per section |

---

## Monitoring & Observability

### Metrics

```csharp
public static class IpsMetrics
{
    private static readonly Histogram IpsGenerationDuration = Metrics.CreateHistogram(
        "ignixa_ips_generation_duration_seconds",
        "Time to generate IPS document",
        new HistogramConfiguration
        {
            LabelNames = ["tenant_id", "profile", "status"],
            Buckets = [0.1, 0.25, 0.5, 1, 2, 5, 10]
        });

    private static readonly Counter IpsGenerationTotal = Metrics.CreateCounter(
        "ignixa_ips_generation_total",
        "Total IPS documents generated",
        new CounterConfiguration
        {
            LabelNames = ["tenant_id", "profile", "status"]
        });

    private static readonly Gauge IpsResourceCount = Metrics.CreateGauge(
        "ignixa_ips_resource_count",
        "Resources in generated IPS",
        new GaugeConfiguration
        {
            LabelNames = ["tenant_id", "section"]
        });

    private static readonly Counter IpsCacheHits = Metrics.CreateCounter(
        "ignixa_ips_cache_hits_total",
        "IPS cache hits",
        new CounterConfiguration
        {
            LabelNames = ["tenant_id", "cache_level"]
        });
}
```

### Alerts

```yaml
# prometheus/alerts/ips.yml
groups:
  - name: ips
    rules:
      - alert: IpsGenerationSlowP99
        expr: histogram_quantile(0.99, rate(ignixa_ips_generation_duration_seconds_bucket[5m])) > 5
        for: 10m
        labels:
          severity: warning
        annotations:
          summary: IPS generation P99 latency exceeds 5 seconds

      - alert: IpsGenerationErrorRate
        expr: rate(ignixa_ips_generation_total{status="error"}[5m]) / rate(ignixa_ips_generation_total[5m]) > 0.05
        for: 5m
        labels:
          severity: critical
        annotations:
          summary: IPS generation error rate exceeds 5%

      - alert: IpsCacheMissRateHigh
        expr: 1 - (rate(ignixa_ips_cache_hits_total[5m]) / rate(ignixa_ips_generation_total[5m])) > 0.7
        for: 30m
        labels:
          severity: warning
        annotations:
          summary: IPS cache miss rate exceeds 70%
```

---

## Implementation Phases

### Phase 1: Core Infrastructure (40 hours)

**Week 1-2**

- [ ] Create API interfaces
  - [ ] `IIpsGeneratorService`
  - [ ] `IIpsGenerationStrategy`
  - [ ] `ISectionSearchStrategy<T>`
  - [ ] `ISectionResourceSupplier`
  - [ ] `INoInfoGenerator`
  - [ ] `Section` with Builder pattern

- [ ] Implement base strategy
  - [ ] `BaseIpsGenerationStrategy`
  - [ ] Section registration infrastructure
  - [ ] Default implementations

- [ ] Implement generator service
  - [ ] Patient fetching (by ID and identifier)
  - [ ] Single-query optimization
  - [ ] Resource classification
  - [ ] Composition building
  - [ ] Bundle assembly

### Phase 2: Section Strategies (35 hours)

**Week 3**

- [ ] Implement required section strategies
  - [ ] `AllergyIntoleranceSearchStrategy`
  - [ ] `MedicationStatementSearchStrategy`
  - [ ] `MedicationRequestSearchStrategy`
  - [ ] `ConditionSearchStrategy`

- [ ] Implement recommended section strategies
  - [ ] `ImmunizationSearchStrategy`
  - [ ] `ProcedureSearchStrategy`
  - [ ] `DeviceUseStatementSearchStrategy`
  - [ ] `DiagnosticReportSearchStrategy`
  - [ ] `ObservationSearchStrategy`

- [ ] Implement no-info generators
  - [ ] `AllergyIntoleranceNoInfoGenerator`
  - [ ] `MedicationNoInfoGenerator`
  - [ ] `ConditionNoInfoGenerator`

### Phase 3: API & Handler (25 hours)

**Week 4**

- [ ] Create Medino query/handler
  - [ ] `IpsGeneratorQuery`
  - [ ] `IpsGeneratorHandler`
  - [ ] Parameter validation

- [ ] Create API endpoints
  - [ ] `IpsEndpoints.cs`
  - [ ] Instance-level routes
  - [ ] Type-level routes
  - [ ] Tenant-explicit routes
  - [ ] POST variants

- [ ] Register in CapabilityStatement
  - [ ] Add `$summary` to Patient operations
  - [ ] Load IPS OperationDefinition

### Phase 4: Narrative Generation (30 hours)

**Week 5**

- [ ] Integrate Scriban engine
  - [ ] `ScribanNarrativeEngine`
  - [ ] Template loading from resources
  - [ ] FHIRPath helpers

- [ ] Create section templates
  - [ ] `allergies.scriban`
  - [ ] `medications.scriban`
  - [ ] `problems.scriban`
  - [ ] `immunizations.scriban`
  - [ ] `procedures.scriban`
  - [ ] `devices.scriban`
  - [ ] `diagnostics.scriban`
  - [ ] `generic.scriban` (fallback)

### Phase 5: Caching & Performance (20 hours)

**Week 6**

- [ ] Implement caching
  - [ ] `IIpsCacheService`
  - [ ] Bundle-level caching
  - [ ] Cache invalidation on resource updates

- [ ] Performance optimization
  - [ ] Profile query execution
  - [ ] Optimize narrative generation
  - [ ] Add streaming backpressure

- [ ] Async support
  - [ ] Threshold detection
  - [ ] DurableTask orchestration
  - [ ] Status endpoint

### Phase 6: Testing & Documentation (10 hours)

**Week 7**

- [ ] Unit tests
  - [ ] Service tests
  - [ ] Strategy tests
  - [ ] Handler tests

- [ ] Integration tests
  - [ ] API endpoint tests
  - [ ] Full workflow tests

- [ ] Conformance tests
  - [ ] IPS profile validation
  - [ ] Required section tests

- [ ] Documentation
  - [ ] API documentation
  - [ ] Configuration guide
  - [ ] Customization guide

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Query optimization complexity** | High | Reuse proven compartment search patterns from ADR-2502 |
| **Narrative generation performance** | Medium | Use compiled templates, lazy generation |
| **Profile validation overhead** | Medium | Optional validation, cache validation results |
| **Large patient memory pressure** | High | Streaming assembly, async fallback |
| **Cache invalidation complexity** | Medium | Tag-based invalidation, TTL fallback |

---

## Decision Log

| Decision | Rationale | Date |
|----------|-----------|------|
| Single-query optimization | 6x faster than HAPI's sequential approach | 2025-12-16 |
| Scriban for narratives | Faster than Thymeleaf, native .NET support | 2025-12-16 |
| Strategy pattern | Enables jurisdiction-specific customization | 2025-12-16 |
| Tiered caching | Balance freshness vs. performance | 2025-12-16 |
| Async threshold at 500 | Balance sync performance vs. timeout risk | 2025-12-16 |

---

## References

### FHIR Specifications

- [IPS IG STU 2](https://hl7.org/fhir/uv/ips/)
- [IPS $summary Operation](https://hl7.org/fhir/uv/ips/OperationDefinition-summary.html)
- [Bundle-uv-ips Profile](https://hl7.org/fhir/uv/ips/StructureDefinition-Bundle-uv-ips.html)
- [Composition-uv-ips Profile](https://hl7.org/fhir/uv/ips/StructureDefinition-Composition-uv-ips.html)
- [IPS Generation Guidance](https://hl7.org/fhir/uv/ips/ipsGeneration.html)

### HAPI FHIR

- [hapi-fhir-jpaserver-ips Module](https://github.com/hapifhir/hapi-fhir/tree/master/hapi-fhir-jpaserver-ips)
- [IpsGeneratorSvcImpl.java](https://github.com/hapifhir/hapi-fhir/blob/master/hapi-fhir-jpaserver-ips/src/main/java/ca/uhn/fhir/jpa/ips/generator/IpsGeneratorSvcImpl.java)

### Ignixa Architecture

- [ADR-2502: Compartment Wildcard Search](./ADR-2502-compartment-wildcard-search.md)
- [ADR-2540: Advanced FHIR Operations](./ADR-2540-advanced-fhir-operations.md)
- [Patient Everything Handler](../src/Ignixa.Application.Operations/Features/PatientEverything/)

---

## Appendix A: Section Code Reference

| Section | LOINC Code | Display |
|---------|------------|---------|
| Medication Summary | 10160-0 | History of Medication use Narrative |
| Allergies and Intolerances | 48765-2 | Allergies and adverse reactions Document |
| Problem List | 11450-4 | Problem list - Reported |
| Immunizations | 11369-6 | History of Immunization Narrative |
| History of Procedures | 47519-4 | History of Procedures Document |
| Medical Devices | 46264-8 | History of medical device use |
| Diagnostic Results | 30954-2 | Relevant diagnostic tests/laboratory data Narrative |
| Vital Signs | 8716-3 | Vital signs |
| Social History | 29762-2 | Social history Narrative |
| Pregnancy History | 10162-6 | History of pregnancies Narrative |
| Past History of Illness | 11348-0 | History of Past illness Narrative |
| Functional Status | 47420-5 | Functional status assessment note |
| Plan of Care | 18776-5 | Plan of care note |
| Advance Directives | 42348-3 | Advance directives |

## Appendix B: IPS Resource Profiles

| Resource | IPS Profile |
|----------|-------------|
| Bundle | http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips |
| Composition | http://hl7.org/fhir/uv/ips/StructureDefinition/Composition-uv-ips |
| Patient | http://hl7.org/fhir/uv/ips/StructureDefinition/Patient-uv-ips |
| AllergyIntolerance | http://hl7.org/fhir/uv/ips/StructureDefinition/AllergyIntolerance-uv-ips |
| Condition | http://hl7.org/fhir/uv/ips/StructureDefinition/Condition-uv-ips |
| MedicationStatement | http://hl7.org/fhir/uv/ips/StructureDefinition/MedicationStatement-uv-ips |
| MedicationRequest | http://hl7.org/fhir/uv/ips/StructureDefinition/MedicationRequest-uv-ips |
| Immunization | http://hl7.org/fhir/uv/ips/StructureDefinition/Immunization-uv-ips |
| Procedure | http://hl7.org/fhir/uv/ips/StructureDefinition/Procedure-uv-ips |
| DeviceUseStatement | http://hl7.org/fhir/uv/ips/StructureDefinition/DeviceUseStatement-uv-ips |
| DiagnosticReport | http://hl7.org/fhir/uv/ips/StructureDefinition/DiagnosticReport-uv-ips |
| Observation | Multiple IPS observation profiles (lab, vital signs, etc.) |

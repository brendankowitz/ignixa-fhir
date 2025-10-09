# Investigation: FHIR Version Conversion and Multi-Version Support

## Executive Summary

This investigation addresses FHIR version conversion strategies and multi-version server support for a deployment supporting STU3, R4, R4B, R5, and R6 simultaneously.

**Key Finding**: Industry practice favors **per-deployment version isolation** over automatic conversion, due to conversion fidelity challenges and operational complexity.

**Recommended Approach**: Path-based version routing with version-tagged storage and **no automatic conversion**.

## Problem Statement

### The Multi-Version Challenge

FHIR has evolved through multiple major versions:
- **DSTU2** (2015): Initial widespread adoption
- **STU3** (2017): Significant breaking changes
- **R4** (2019): First normative content
- **R4B** (2020): Minor update
- **R5** (2023): Expanded normative content
- **R6** (2025+): Additional resources, breaking changes

**Business Reality**:
- Healthcare organizations have resources in multiple FHIR versions
- Clients (EHRs, apps) support different versions
- Regulatory requirements may mandate specific versions
- Migration between versions is expensive and risky

**Technical Challenges**:
1. **Breaking Changes**: Resources removed (Media in R5), renamed elements, cardinality changes
2. **Conversion Fidelity**: R4 → R5 → R4 round-trip lossy
3. **New Required Fields**: R5 adds required fields not in R4
4. **Version-Specific Features**: Search parameters, operations, resource types unique to versions
5. **Storage Strategy**: Store in native version or normalize to single version?

## Version Conversion Mechanisms

### 1. Official FHIR Mechanisms

#### StructureMap Resource

**Purpose**: Defines automated transformation rules between FHIR structures

**Specification**: Available in R4, R5, R6
**Language**: FHIR Mapping Language (FML) with concrete syntax
**Foundation**: Built on FHIRPath; requires FHIRPath implementation
**Use Cases**:
- Version-to-version conversion (R4 ↔ R5)
- Format transformation (HL7 v2/CDA → FHIR)
- Profile-to-profile mapping

**Official Examples**:
- R4B ↔ R5 working transforms available
- Tutorial: https://build.fhir.org/mapping-tutorial.html
- Mapping Language: https://confluence.hl7.org/display/FHIR/Using+the+FHIR+Mapping+Language

**Example StructureMap** (simplified):
```
map "http://example.com/fhir/StructureMap/PatientR4ToR5" = "Patient R4 to R5"

uses "http://hl7.org/fhir/4.0/StructureDefinition/Patient" as source
uses "http://hl7.org/fhir/5.0/StructureDefinition/Patient" as target

group PatientTransform(source src : Patient, target tgt : Patient) {
  src.id -> tgt.id;
  src.meta -> tgt.meta;
  src.name -> tgt.name;
  src.gender -> tgt.gender;
  src.birthDate -> tgt.birthDate;
  // R5 new required field - default value
  src -> tgt.created = now() "set-created";
}
```

**Implementation Tools**:
- HAPI FHIR (Java)
- matchbox-engine (Java)
- FHIR Java Validator (CLI)
- Pascal implementation
- C# implementation

#### ConceptMap Resource

**Purpose**: Maps relationships between code systems and value sets
**Use Case**: Terminology translation, concept-level equivalencies
**Relationship**: ConceptMap handles high-level model relationships; StructureMap handles full transformation

**Example**:
```json
{
  "resourceType": "ConceptMap",
  "url": "http://example.com/fhir/ConceptMap/gender-r4-r5",
  "sourceCanonical": "http://hl7.org/fhir/4.0/ValueSet/administrative-gender",
  "targetCanonical": "http://hl7.org/fhir/5.0/ValueSet/administrative-gender",
  "group": [{
    "source": "http://hl7.org/fhir/4.0/administrative-gender",
    "target": "http://hl7.org/fhir/5.0/administrative-gender",
    "element": [{
      "code": "male",
      "target": [{
        "code": "male",
        "equivalence": "equivalent"
      }]
    }]
  }]
}
```

#### Cross-Version Extensions Package

**Package**: hl7.fhir.xver-extensions (current: 0.0.11)
**Purpose**: Enable R5-specific elements in R4 and vice versa
**Pattern**: Extension URLs like `http://hl7.org/fhir/5.0/StructureDefinition/extension-Observation.value`

**Strategy**:
- Use correct element when available
- Fall back to extension for version-specific features
- Enables round-trip fidelity

**Adoption**:
- HL7 Kindling (FHIR spec publisher)
- org.hl7.fhir.core validator
- HAPI FHIR Validator
- Sushi (FSH compiler)

**Example** (R5 Observation.value in R4):
```json
{
  "resourceType": "Observation",
  "status": "final",
  "code": { "coding": [{"system": "http://loinc.org", "code": "8480-6"}] },
  "extension": [{
    "url": "http://hl7.org/fhir/5.0/StructureDefinition/extension-Observation.value",
    "valueQuantity": {
      "value": 100,
      "unit": "mg/dL",
      "system": "http://unitsofmeasure.org",
      "code": "mg/dL"
    }
  }]
}
```

### 2. Conversion Libraries

#### HAPI FHIR VersionConvertor

**Package**: ca.uhn.hapi.fhir:hapi-fhir-converter
**License**: Apache 2.0
**Language**: Java
**Status**: Experimental (use with caution)

**Pattern**:
```java
import org.hl7.fhir.convertors.factory.VersionConvertorFactory_40_50;

// R4 → R5
org.hl7.fhir.r4.model.Patient r4Patient = ...;
org.hl7.fhir.r5.model.Patient r5Patient =
    (org.hl7.fhir.r5.model.Patient) VersionConvertorFactory_40_50
        .convertResource(r4Patient);

// R5 → R4
org.hl7.fhir.r5.model.Patient r5Patient = ...;
org.hl7.fhir.r4.model.Patient r4Patient =
    (org.hl7.fhir.r4.model.Patient) VersionConvertorFactory_40_50
        .convertResource(r5Patient);
```

**Available Converters**:
- `VersionConvertorFactory_10_30` (DSTU2 ↔ STU3)
- `VersionConvertorFactory_10_40` (DSTU2 ↔ R4)
- `VersionConvertorFactory_10_50` (DSTU2 ↔ R5)
- `VersionConvertorFactory_14_30` (DSTU2016May ↔ STU3)
- `VersionConvertorFactory_30_40` (STU3 ↔ R4)
- `VersionConvertorFactory_30_50` (STU3 ↔ R5)
- `VersionConvertorFactory_40_50` (R4 ↔ R5)
- `VersionConvertorFactory_43_50` (R4B ↔ R5)

**Validation Strategy**:
- Single R5 codebase for all validation
- StructureDefinitions "up-converted" to R5 validator
- Applies version-specific rules when validating

**Performance**: ~300ms overhead per conversion (serialization-dominant)

#### Firely .NET SDK

**Package**: Hl7.Fhir.STU3, Hl7.Fhir.R4, Hl7.Fhir.R4B, Hl7.Fhir.R5
**License**: BSD-3-Clause (open source), commercial license available
**Language**: C#

**Multi-Version Pattern**: Extern aliases
```csharp
// .csproj
<ItemGroup>
  <PackageReference Include="Hl7.Fhir.R4" Version="5.0.0" Aliases="FhirR4" />
  <PackageReference Include="Hl7.Fhir.R5" Version="5.0.0" Aliases="FhirR5" />
</ItemGroup>

// .cs
extern alias FhirR4;
extern alias FhirR5;

using R4Patient = FhirR4::Hl7.Fhir.Model.Patient;
using R5Patient = FhirR5::Hl7.Fhir.Model.Patient;

// Manual conversion required
R4Patient r4Patient = ...;
R5Patient r5Patient = ConvertPatientR4ToR5(r4Patient);  // YOU implement this
```

**IMPORTANT**: **NO automatic conversion** provided. Developers must manually convert.

**Rationale**: Conversion is lossy and context-dependent; library doesn't make assumptions.

#### Microsoft FHIR Converter

**Repository**: https://github.com/microsoft/FHIR-Converter
**Purpose**: Legacy format conversion (HL7 v2, CCDA, JSON) → FHIR R4
**Operation**: `$convert-data` endpoint
**Integration**: Azure Logic Apps, Azure Data Factory
**Target Version**: FHIR R4 only

**Not for FHIR-to-FHIR conversion** (designed for ETL pipelines)

## Conversion Patterns and Challenges

### 1. Simple Element Renames

**Pattern**: Direct field mapping with name changes
**Fidelity**: Lossless if data types match

**Example**: Media (R4) → DocumentReference (R5)

**StructureMap**:
```
map "http://example.com/MediaToDocRef" = "Media to DocumentReference"

uses "http://hl7.org/fhir/4.0/StructureDefinition/Media" as source
uses "http://hl7.org/fhir/5.0/StructureDefinition/DocumentReference" as target

group MediaTransform(source src : Media, target tgt : DocumentReference) {
  src.id -> tgt.id;
  src.status -> tgt.status;
  src.content -> tgt.content;
  src.subject -> tgt.subject;
}
```

### 2. Cardinality Changes (0..1 → 0..*)

**Challenge**: Forward (0..1 → 0..*) is safe; backward (0..* → 0..1) loses data

**Forward Conversion (R4 → R5)**:
```csharp
// R4: single value
r4Resource.Value = "single";

// R5: array
r5Resource.Value = new[] { r4Resource.Value };
```

**Backward Conversion (R5 → R4)**:
```csharp
// R5: array with multiple values
r5Resource.Value = new[] { "first", "second", "third" };

// R4: single value - LOSSY!
if (r5Resource.Value.Length > 1)
{
    _logger.LogWarning("Multiple values found, taking first. Data loss!");
}
r4Resource.Value = r5Resource.Value.FirstOrDefault();

// Alternative: Store extras in extension
r4Resource.Extension.Add(new Extension
{
    Url = "http://example.com/additional-values",
    Value = new FhirString(string.Join(",", r5Resource.Value.Skip(1)))
});
```

**Profiling Rule**: Profiles cannot convert array ↔ scalar

### 3. Breaking Changes (Removed Elements)

**Example**: Media resource removed in R5, replaced by DocumentReference

**Strategy**:
1. **Semantic Mapping**: Map to equivalent resource
2. **Extension Storage**: Store unmapped data in extensions
3. **Document Limitations**: Clearly document conversion fidelity loss

**R4 Media → R5 DocumentReference**:
```json
{
  "resourceType": "DocumentReference",
  "status": "current",
  "content": [{
    "attachment": {
      "contentType": "image/jpeg",
      "url": "http://example.com/media/photo.jpg"
    }
  }],
  "extension": [{
    "url": "http://example.com/original-resource-type",
    "valueCode": "Media"
  }]
}
```

**Fidelity**: Often lossy; may require application-level semantics

### 4. New Required Elements in Newer Versions

**Challenge**: R5 adds required field not present in R4

**Strategies**:

#### Option 1: Placeholder/Default Values
```csharp
// R4 Patient → R5 Patient
// R5 adds required "created" timestamp
r5Patient.Created = r4Patient.Meta?.LastUpdated ?? DateTimeOffset.UtcNow;
```

**Common Defaults**:
- Timestamps: Use `Meta.lastUpdated` or current time
- Status fields: `"active"` or `"unknown"`
- Required objects: Empty `{}`

#### Option 2: Validation Failure
```csharp
if (!CanSafelyConvert(r4Resource, FhirVersion.R5))
{
    throw new FhirConversionException(
        "Cannot convert R4 resource to R5: missing required field 'created'");
}
```

#### Option 3: Extension-Based Round-Tripping
```json
{
  "created": "2024-10-08T12:00:00Z",
  "extension": [{
    "url": "http://hl7.org/fhir/4.0/StructureDefinition/extension-synthetic-created",
    "valueBoolean": true
  }]
}
```

**Recommended**: Option 1 (defaults) for forward compatibility, Option 2 (fail) for backward

### 5. Conversion Fidelity and Data Loss

#### Known Lossy Scenarios

| Scenario | Example | Data Loss |
|----------|---------|-----------|
| Resource removal | Media (R4) → DocumentReference (R5) | Semantic meaning changes |
| Cardinality reduction | 0..* → 0..1 | Extra values lost |
| Data type changes | code → anyURI validation failures | String length, format constraints |
| Mandatory fields | R5 required fields not in R4 | Synthetic/placeholder data |
| Clinical semantics | Medication dosage reformatting | Meaning may change |

#### Round-Trip Testing Results

**Organization Resource**: ✅ All tests pass R3 ↔ R4
**ValueSet Expansion**: ⚠️ Requires cross-version extensions R4 ↔ R5
**General**: Resource-dependent; normative resources more stable

**Best Practice**: Test round-trip for each resource type used

```csharp
[Theory]
[InlineData("Patient")]
[InlineData("Observation")]
[InlineData("Medication")]
public async Task RoundTrip_R4_R5_R4_PreservesData(string resourceType)
{
    var r4Original = await LoadSampleResourceAsync(resourceType, FhirVersion.R4);

    var r5Converted = _converter.ConvertR4ToR5(r4Original);
    var r4RoundTrip = _converter.ConvertR5ToR4(r5Converted);

    // Deep equality comparison
    Assert.True(FhirResourceEqualityComparer.AreEqual(r4Original, r4RoundTrip),
        "Round-trip conversion should preserve data");
}
```

## Multi-Version Storage Strategies

### Pattern 1: Per-Version Storage (Separate Databases)

**Used By**: Google Cloud Healthcare API, AWS HealthLake, Azure Health Data Services

**Implementation**:
- FHIR version specified at store/service creation
- Cannot change version after creation
- Multiple stores for multiple versions

**Storage Model**:
```
/data/r4-store/Patient/123.json
/data/r5-store/Patient/123.json
```

**Pros**:
- ✅ Simple implementation
- ✅ No conversion overhead
- ✅ Version-specific optimization
- ✅ Perfect fidelity (no conversion)

**Cons**:
- ❌ Data duplication if same resource in multiple versions
- ❌ Difficult to migrate between versions
- ❌ Multiple deployments to manage

**Example** (Google Cloud):
```bash
# Create R4 store
gcloud healthcare fhir-stores create my-r4-store \
  --dataset=my-dataset \
  --version=R4

# Create R5 store
gcloud healthcare fhir-stores create my-r5-store \
  --dataset=my-dataset \
  --version=R5
```

**Recommended for**: ✅ **FHIR Server v2** (aligns with industry practice)

### Pattern 2: Native Version Storage + On-The-Fly Conversion

**Used By**: HAPI FHIR (via interceptors), Smile CDR

**Implementation**:
- Resources stored in submitted FHIR version
- Conversion at request time based on Accept header
- Servlet filters/interceptors handle conversion

**Storage Model**:
```json
{
  "id": "Patient/123",
  "fhirVersion": "4.0",
  "resource": { /* R4 Patient */ }
}
```

**Request/Response Flow**:
```http
POST /Patient HTTP/1.1
Content-Type: application/fhir+json;fhirVersion=4.0

{ /* R4 Patient */ }

→ Store as R4

GET /Patient/123 HTTP/1.1
Accept: application/fhir+json;fhirVersion=5.0

← Convert to R5 on-the-fly
{ /* R5 Patient */ }
```

**Pros**:
- ✅ Preserves original data fidelity
- ✅ True multi-version from single deployment
- ✅ Conversion only when needed

**Cons**:
- ❌ Conversion performance overhead (~300ms per request)
- ❌ Complex error handling (conversion failures)
- ❌ Round-trip fidelity issues

**Performance**:
- Conversion: ~300ms per request (HAPI benchmark)
- Serialization-dominant cost
- Caching can help but cache invalidation is hard

**Not recommended**: Complexity and performance issues outweigh benefits

### Pattern 3: Normalized Storage (Convert to Single Version)

**Used By**: Firely Server (optional multi-version mode)

**Implementation**:
- Convert all resources to canonical version (e.g., R5) on write
- Store in canonical version
- Convert back on read if different version requested

**Storage Model**:
```json
{
  "id": "Patient/123",
  "canonicalVersion": "5.0",
  "originalVersion": "4.0",
  "resource": { /* R5 Patient (normalized) */ }
}
```

**Pros**:
- ✅ Simplified storage schema
- ✅ Consistent indexing and search
- ✅ Single validation path

**Cons**:
- ❌ Conversion fidelity loss (lossy on write)
- ❌ Cannot preserve original submitted version
- ❌ Conversion overhead on every write
- ❌ Backward conversion issues (R5 → R4 problematic)

**Not recommended**: Data loss unacceptable for healthcare

### Pattern 4: Dual Storage (Original + Canonical)

**Theoretical** - Not widely implemented

**Implementation**:
- Store both original AND normalized version
- Use canonical for search/indexing
- Return original on GET

**Storage Model**:
```json
{
  "id": "Patient/123",
  "originalVersion": "4.0",
  "originalResource": { /* R4 Patient */ },
  "canonicalVersion": "5.0",
  "canonicalResource": { /* R5 Patient */ },
  "searchIndices": [ /* from R5 */ ]
}
```

**Pros**:
- ✅ Best fidelity (original preserved)
- ✅ Efficient search (normalized index)
- ✅ Supports round-tripping

**Cons**:
- ❌ 2x storage cost
- ❌ Synchronization complexity
- ❌ What if conversion fails?

**Not recommended**: Complexity and cost outweigh benefits

## Recommended Architecture for FHIR Server v2

### Decision: Path-Based Multi-Version with No Automatic Conversion

**Rationale**:
1. ✅ Industry standard (Google, Azure, AWS)
2. ✅ Avoids conversion fidelity issues
3. ✅ Clear version isolation
4. ✅ Zero conversion overhead
5. ✅ Simple implementation and testing
6. ✅ Aligns with existing `IFhirSchemaProvider` abstraction

### URL Pattern

```
/{tenantId}/{version}/{resourceType}/{id?}

Examples:
GET /acme/R4/Patient/123
GET /acme/R5/Patient/456
POST /acme/R4/Observation
```

### Version Context Resolution

```csharp
public record FhirRequestContext
{
    public required string TenantId { get; init; }
    public required FhirVersion Version { get; init; }
    public required IFhirSchemaProvider SchemaProvider { get; init; }
    public required IImplementationGuideContext? IgContext { get; init; }
}

public class FhirRequestContextResolver
{
    private readonly IFhirSchemaProviderFactory _schemaFactory;

    public async ValueTask<FhirRequestContext> ResolveAsync(
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = httpContext.GetRouteValue("tenantId")?.ToString()
            ?? throw new InvalidOperationException("TenantId required");

        var versionString = httpContext.GetRouteValue("version")?.ToString()
            ?? throw new InvalidOperationException("Version required");

        var version = versionString.ToUpper() switch
        {
            "STU3" or "3.0" => FhirVersion.STU3,
            "R4" or "4.0" => FhirVersion.R4,
            "R4B" or "4.3" => FhirVersion.R4B,
            "R5" or "5.0" => FhirVersion.R5,
            "R6" or "6.0" => FhirVersion.R6,
            _ => throw new NotSupportedException($"FHIR version {versionString} not supported")
        };

        var schemaProvider = _schemaFactory.GetProvider(version);

        return new FhirRequestContext
        {
            TenantId = tenantId,
            Version = version,
            SchemaProvider = schemaProvider,
            IgContext = await ResolveIgContextAsync(tenantId, version, ct)
        };
    }
}
```

### Routing Configuration

```csharp
// Startup.cs
app.UseRouting();
app.UseMiddleware<FhirRequestContextMiddleware>();  // Sets FhirRequestContext
app.UseMiddleware<VersionEnforcementMiddleware>();  // Rejects unsupported resource types
app.UseEndpoints(endpoints =>
{
    // Pattern: /{tenantId}/{version}/{resourceType}/{id?}
    endpoints.MapControllers();
});

// Controllers
[ApiController]
[Route("{tenantId}/{version:regex(^(STU3|R4|R4B|R5|R6)$)}")]
public class FhirResourceController : ControllerBase
{
    [HttpGet("{resourceType}/{id}")]
    public async Task<IActionResult> Read(
        [FromServices] FhirRequestContext context,
        string resourceType,
        string id,
        CancellationToken ct)
    {
        // context.Version already resolved
        // context.SchemaProvider already loaded

        var resource = await _repository.GetAsync(
            context.TenantId,
            context.Version,
            resourceType,
            id,
            ct);

        return Ok(resource);
    }
}
```

### Storage Model with Version Tagging

#### File-Based Storage (Phase 1)

```
/data/{tenantId}/{version}/{resourceType}/{id}.json
/data/{tenantId}/{version}/{resourceType}/{id}.metadata.ndjson

Example:
/data/acme/R4/Patient/123.json
/data/acme/R4/Patient/123.metadata.ndjson
/data/acme/R5/Patient/456.json
/data/acme/R5/Patient/456.metadata.ndjson
```

**Metadata Sidecar**:
```json
{
  "resourceType": "Patient",
  "id": "123",
  "fhirVersion": "4.0",
  "versionId": "1",
  "lastModified": "2024-10-08T12:00:00Z",
  "searchIndices": {
    "name": ["John", "Doe"],
    "birthdate": ["1980-01-01"],
    "identifier": ["MRN|12345"]
  }
}
```

#### SQL Server Storage (Phase 8)

**3-Table Split with FhirVersion Column**:

```sql
CREATE TABLE Resource (
    ResourceId BIGINT PRIMARY KEY IDENTITY,
    TenantId VARCHAR(50) NOT NULL,
    ResourceType VARCHAR(50) NOT NULL,
    Id VARCHAR(64) NOT NULL,
    FhirVersion VARCHAR(10) NOT NULL,  -- "3.0", "4.0", "4.3", "5.0", "6.0"
    VersionId INT NOT NULL,
    LastModified DATETIME2 NOT NULL,
    IsDeleted BIT NOT NULL DEFAULT 0,
    StorageLocation VARCHAR(500) NOT NULL,  -- URN: sql://, blob://, etc.

    CONSTRAINT UQ_Resource_Tenant_Type_Id_Version
        UNIQUE (TenantId, ResourceType, Id, FhirVersion),
    INDEX IX_Resource_Tenant_Type_Version (TenantId, ResourceType, FhirVersion)
);

CREATE TABLE RawResource (
    ResourceId BIGINT PRIMARY KEY,
    ResourceJson NVARCHAR(MAX) NOT NULL,
    CONSTRAINT FK_RawResource_Resource
        FOREIGN KEY (ResourceId) REFERENCES Resource(ResourceId) ON DELETE CASCADE
);

CREATE TABLE SearchIndex (
    ResourceId BIGINT NOT NULL,
    ParamName VARCHAR(100) NOT NULL,
    ParamType VARCHAR(20) NOT NULL,  -- String, Token, Number, Date, Reference

    -- Type-specific columns
    StringValue NVARCHAR(256) NULL,
    TokenSystem NVARCHAR(256) NULL,
    TokenCode NVARCHAR(256) NULL,
    NumberValue DECIMAL(18,6) NULL,
    DateStart DATETIME2 NULL,
    DateEnd DATETIME2 NULL,
    ReferenceType VARCHAR(50) NULL,
    ReferenceId VARCHAR(64) NULL,

    INDEX IX_SearchIndex_String (ParamName, StringValue) INCLUDE (ResourceId),
    INDEX IX_SearchIndex_Token (ParamName, TokenSystem, TokenCode) INCLUDE (ResourceId),
    INDEX IX_SearchIndex_Number (ParamName, NumberValue) INCLUDE (ResourceId),
    INDEX IX_SearchIndex_Date (ParamName, DateStart, DateEnd) INCLUDE (ResourceId),
    INDEX IX_SearchIndex_Reference (ParamName, ReferenceType, ReferenceId) INCLUDE (ResourceId),

    CONSTRAINT FK_SearchIndex_Resource
        FOREIGN KEY (ResourceId) REFERENCES Resource(ResourceId) ON DELETE CASCADE
);
```

**Query Pattern**:
```sql
-- Get R4 Patient
SELECT r.ResourceJson
FROM Resource res
JOIN RawResource r ON res.ResourceId = r.ResourceId
WHERE res.TenantId = 'acme'
  AND res.ResourceType = 'Patient'
  AND res.Id = '123'
  AND res.FhirVersion = '4.0'
  AND res.IsDeleted = 0;

-- Search R5 Patients by name
SELECT r.ResourceJson
FROM Resource res
JOIN RawResource r ON res.ResourceId = r.ResourceId
JOIN SearchIndex si ON res.ResourceId = si.ResourceId
WHERE res.TenantId = 'acme'
  AND res.ResourceType = 'Patient'
  AND res.FhirVersion = '5.0'
  AND res.IsDeleted = 0
  AND si.ParamName = 'name'
  AND si.StringValue LIKE 'John%';
```

### Version Enforcement Middleware

```csharp
/// <summary>
/// Rejects requests for resource types not supported in the requested FHIR version
/// </summary>
public class VersionEnforcementMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<VersionEnforcementMiddleware> _logger;

    public async Task InvokeAsync(HttpContext context, FhirRequestContext fhirContext)
    {
        var resourceType = context.GetRouteValue("resourceType")?.ToString();

        if (string.IsNullOrEmpty(resourceType))
        {
            await _next(context);
            return;
        }

        // Check if resource type exists in this FHIR version
        if (!fhirContext.SchemaProvider.ResourceTypeNames.Contains(resourceType))
        {
            _logger.LogWarning(
                "Resource type {ResourceType} not supported in FHIR version {Version}",
                resourceType, fhirContext.Version);

            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/fhir+json";

            await context.Response.WriteAsJsonAsync(new OperationOutcome
            {
                Issue = new List<OperationOutcome.IssueComponent>
                {
                    new()
                    {
                        Severity = OperationOutcome.IssueSeverity.Error,
                        Code = OperationOutcome.IssueType.NotSupported,
                        Diagnostics = $"Resource type '{resourceType}' is not supported in FHIR version {fhirContext.Version}. " +
                                     $"This resource type may exist in other FHIR versions."
                    }
                }
            });
            return;
        }

        await _next(context);
    }
}
```

### Capability Statement Per Version

```csharp
public interface ICapabilityStatementService
{
    ValueTask<CapabilityStatement> GetCapabilityStatementAsync(
        string tenantId,
        FhirVersion version,
        CancellationToken ct = default);
}

public class CapabilityStatementService : ICapabilityStatementService
{
    private readonly IEnumerable<ICapabilitySegment> _segments;
    private readonly MemoryCache _cache;

    public async ValueTask<CapabilityStatement> GetCapabilityStatementAsync(
        string tenantId,
        FhirVersion version,
        CancellationToken ct)
    {
        var cacheKey = $"{tenantId}:{version}";

        if (_cache.TryGetValue<CapabilityStatement>(cacheKey, out var cached))
        {
            return cached;
        }

        var context = new CapabilityContext(version, tenantId);
        var builder = new CapabilityStatementBuilder(version);

        // Apply all segments
        foreach (var segment in _segments.OrderBy(s => s.Priority))
        {
            await segment.ApplyAsync(builder, context, ct);
        }

        var capability = builder.Build();

        // Cache with version hash for invalidation
        var versionHash = await ComputeVersionHashAsync(context, ct);
        _cache.Set(cacheKey, capability, TimeSpan.FromHours(1));

        return capability;
    }
}
```

**Example Output** (R4 vs R5):

```json
{
  "resourceType": "CapabilityStatement",
  "fhirVersion": "4.0",
  "rest": [{
    "mode": "server",
    "resource": [{
      "type": "Patient",
      "interaction": [{"code": "read"}, {"code": "search-type"}]
    }, {
      "type": "Media",
      "interaction": [{"code": "read"}]
    }]
  }]
}
```

```json
{
  "resourceType": "CapabilityStatement",
  "fhirVersion": "5.0",
  "rest": [{
    "mode": "server",
    "resource": [{
      "type": "Patient",
      "interaction": [{"code": "read"}, {"code": "search-type"}]
    }, {
      "type": "DocumentReference",
      "interaction": [{"code": "read"}]
    }]
  }]
}
```

**Note**: Media exists in R4 but removed in R5 (replaced by DocumentReference)

## Search Parameters Across Versions

### Challenge: Version-Specific Search Parameters

**Example**: R5 adds search parameters not in R4

**Strategy**: Version-specific search parameter loading

```csharp
public interface ISearchParameterProvider
{
    ValueTask<IReadOnlyList<SearchParameterInfo>> GetSearchParametersAsync(
        string resourceType,
        FhirVersion version,
        CancellationToken ct = default);
}

public class SearchParameterRepository : ISearchParameterProvider
{
    public async ValueTask<IReadOnlyList<SearchParameterInfo>> GetSearchParametersAsync(
        string resourceType,
        FhirVersion version,
        CancellationToken ct)
    {
        // Load base search parameters for this version
        var baseParams = await LoadBaseSearchParametersAsync(version, ct);

        // Filter by resource type
        var forType = baseParams.Where(p => p.Base.Contains(resourceType));

        // Add IG-specific parameters for this version
        var igParams = await LoadIgSearchParametersAsync(resourceType, version, ct);

        return forType.Concat(igParams).ToList();
    }
}
```

### Cross-Version Search: Not Supported

```http
GET /acme/Patient?name=John&fhirVersion=4.0,5.0
```

**Response**:
```http
HTTP/1.1 400 Bad Request
Content-Type: application/fhir+json

{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "error",
    "code": "not-supported",
    "diagnostics": "Cross-version search is not supported. Please search within a single FHIR version endpoint (e.g., /R4/Patient or /R5/Patient)."
  }]
}
```

**Rationale**:
- Search parameters differ between versions
- Result format inconsistent
- Complexity not justified by use cases

## Validation Across Versions

### Two-Tier Validation (from ADR-2500 Phase 3)

**Tier 1: Structural Validation (<50ms)**
- Always runs
- Version-specific schema validation
- Required fields, data types, cardinality
- No profile validation

**Tier 2: Profile Validation (<5s)**
- Opt-in via `$validate` operation or `X-Validate: true` header
- Full StructureDefinition conformance
- IG-specific constraints
- Firely SDK 6.0 with caching

**Version Detection**:
1. URL path (`/R4/`, `/R5/`)
2. MIME type parameter: `Content-Type: application/fhir+json;fhirVersion=4.0`
3. Resource.meta.profile canonical URL

**Implementation**:
```csharp
public interface IFhirValidator
{
    ValueTask<OperationOutcome> ValidateAsync(
        ITypedElement resource,
        FhirVersion version,
        ValidationLevel level,
        CancellationToken ct = default);
}

public enum ValidationLevel
{
    Structural,  // Tier 1: <50ms
    Profile      // Tier 2: <5s
}
```

## Performance Targets

| Operation | Per-Version Storage | On-The-Fly Conversion | Normalized Storage |
|-----------|---------------------|----------------------|-------------------|
| Read (in-memory) | <10ms | ~310ms | <10ms |
| Read (file) | <20ms | ~320ms | <20ms |
| Read (SQL) | <100ms | ~400ms | <100ms |
| Write (any) | <50ms | <50ms | ~350ms (conversion) |
| Search (in-memory) | <50ms | ~350ms | <50ms |

**Recommendation**: Per-version storage achieves target performance without conversion overhead

## Phase Integration

### Phase 5: Multi-Version Support (Weeks 15-19) - ADR-2508

**Deliverables**:
- `IFhirSchemaProviderFactory` for STU3, R4, R4B, R5, R6
- Version-specific routing (`/{tenantId}/{version}/*`)
- `FhirRequestContext` with version resolution
- `VersionEnforcementMiddleware` (reject unsupported resource types)
- Version-tagged storage (add `FhirVersion` column/field)
- Version-specific search parameter loading
- Per-version CapabilityStatement generation
- **NO automatic conversion** (explicit design decision)

**E2E Tests**:
- `MultiVersionTests.cs` (20+ tests)
  - R4 and R5 Patient CRUD in same tenant
  - Version enforcement (reject Media in R5, accept in R4)
  - Cross-version search rejection
  - Capability statement differences per version

**Key Innovation**: Path-based version routing with zero conversion overhead

## Summary

### Key Decisions

1. ✅ **Path-Based Multi-Version**: `/{tenantId}/{version}/*` routing
2. ✅ **Version-Tagged Storage**: `FhirVersion` column/field in all storage patterns
3. ✅ **No Automatic Conversion**: Explicit rejection over lossy conversion
4. ✅ **Version Enforcement**: Middleware rejects unsupported resource types
5. ✅ **Version-Specific Search Parameters**: Loaded per FHIR version
6. ✅ **Per-Version Capability Statements**: Cached with version hash

### Industry Alignment

**Cloud Providers**:
- Google Cloud Healthcare API: ✅ Per-version stores
- Azure Health Data Services: ✅ Per-version services
- AWS HealthLake: ✅ R4-only (strictest isolation)

**Open Source**:
- HAPI FHIR: ⚠️ Single-version or servlet filters (complex)
- Firely Server: ✅ Side-by-side multi-version (no auto-conversion)
- Smile CDR: ⚠️ Automatic conversion (fidelity issues)

**Recommendation**: Follow Google/Azure/AWS/Firely pattern (per-version isolation)

### Conversion Guidance

**When clients need conversion**:
- Use external conversion service (not in FHIR server)
- Use HAPI FHIR VersionConvertor or StructureMap implementations
- Document conversion fidelity limitations
- Test round-trip conversions

**Within FHIR server**:
- Accept resources in any supported version
- Store in submitted version
- Serve in same version
- Clear error if client requests unsupported version

### Next Steps

1. **Phase 5 Implementation** (Weeks 15-19):
   - Add `FhirVersion` to storage metadata
   - Implement version-specific routing
   - Create `VersionEnforcementMiddleware`
   - Update CapabilityStatement service for multi-version

2. **Future Consideration** (Post-Phase 18):
   - Optional conversion endpoint: `POST /$convert-version` for client-initiated conversion
   - Documented as experimental with fidelity warnings
   - Uses HAPI FHIR VersionConvertor or StructureMap

## References

- FHIR Versioning: https://build.fhir.org/versioning.html
- StructureMap: https://hl7.org/fhir/structuremap.html
- Cross-Version Extensions: https://build.fhir.org/ig/HL7/fhir-cross-version/
- HAPI FHIR Converter: https://hapifhir.io/hapi-fhir/docs/model/converter.html
- Firely Multi-Version: https://docs.fire.ly/projects/Firely-NET-SDK/en/latest/model/multiple-fhir-versions.html
- Smile CDR Versions: https://smilecdr.com/docs/fhir_repository/fhir_versions.html

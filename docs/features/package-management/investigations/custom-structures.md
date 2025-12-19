# Investigation: Custom Structures and R6 Extensibility

## Executive Summary

This investigation addresses how FHIR Server v2 should handle custom resource types, extensions, and R6's new "Additional Resources" feature for faster adoption of new models without waiting for major FHIR releases.

**Key Finding**: FHIR R6 introduces **Additional Resources** - a mechanism for registering new resource types outside the core spec, enabling faster innovation while maintaining interoperability.

**Recommended Approach**: Support standard extensions, custom profiles, and R6 Additional Resources through a pluggable `IFhirSchemaProvider` architecture.

## Problem Statement

### The Extensibility Challenge

Healthcare is diverse and evolving. Standard FHIR resources don't cover all use cases:

1. **Proprietary Data Models**: Organization-specific workflows and data
2. **Emerging Standards**: New clinical concepts not yet in FHIR core
3. **Regional Requirements**: Country-specific data elements (e.g., Japanese insurance IDs)
4. **Research Protocols**: Clinical trial data not in standard resources
5. **Device-Specific Data**: Proprietary medical device outputs

**Legacy Approaches**:
- **Extensions**: Add data to existing resources (recommended)
- **Contained Resources**: Nest resources that don't exist independently
- **Basic Resource**: Generic wrapper for non-standard data
- **Custom Resource Types**: Create new resource types (HAPI FHIR pattern)

**R6 Innovation**:
- **Additional Resources**: Formally registered resource types outside core spec
- Faster adoption cycle (months vs years)
- HL7-approved but not in base FHIR spec

### Real-World Examples

**Example 1: Japanese Insurance Cards**
- Standard FHIR Coverage resource doesn't have fields for Japanese insurance card number format
- Solution: Extension on Coverage or custom JapanCoverage profile

**Example 2: Clinical Trial Protocols**
- ResearchStudy exists but doesn't cover all protocol details
- Solution: Extensions or Additional Resource (R6)

**Example 3: Genomics**
- FHIR Genomics IG adds many extensions and profiles
- Eventually some may become Additional Resources

## Extension Mechanisms

### 1. Simple Extensions

**Definition**: Add new data elements to existing resources

**When to Use**:
- Single new field (e.g., Patient.petName)
- Organization-specific metadata
- Temporary fields pending standardization

**Structure**:
```json
{
  "resourceType": "Patient",
  "id": "123",
  "name": [{"family": "Doe", "given": ["John"]}],
  "extension": [{
    "url": "http://example.com/fhir/StructureDefinition/patient-pet-name",
    "valueString": "Fluffy"
  }]
}
```

**StructureDefinition**:
```json
{
  "resourceType": "StructureDefinition",
  "url": "http://example.com/fhir/StructureDefinition/patient-pet-name",
  "name": "PatientPetName",
  "status": "active",
  "kind": "complex-type",
  "abstract": false,
  "context": [{
    "type": "element",
    "expression": "Patient"
  }],
  "type": "Extension",
  "baseDefinition": "http://hl7.org/fhir/StructureDefinition/Extension",
  "derivation": "constraint",
  "differential": {
    "element": [{
      "id": "Extension",
      "path": "Extension",
      "short": "Patient's pet name",
      "definition": "The name of the patient's pet (for ice-breaker purposes)"
    }, {
      "id": "Extension.url",
      "path": "Extension.url",
      "fixedUri": "http://example.com/fhir/StructureDefinition/patient-pet-name"
    }, {
      "id": "Extension.value[x]",
      "path": "Extension.value[x]",
      "type": [{
        "code": "string"
      }]
    }]
  }
}
```

**Server Requirements**:
- Parse extension JSON
- Index extension values if searchable
- Return extension in responses
- Validate extension structure if profile validation enabled

### 2. Complex Extensions

**Definition**: Nested extensions with multiple sub-elements

**When to Use**:
- Structured data (e.g., address with validation status)
- Multiple related fields
- Complex datatypes not in FHIR core

**Example** (Birth Place):
```json
{
  "resourceType": "Patient",
  "extension": [{
    "url": "http://hl7.org/fhir/StructureDefinition/patient-birthPlace",
    "valueAddress": {
      "city": "Seattle",
      "state": "WA",
      "country": "USA"
    }
  }]
}
```

**Example** (Japanese Insurance):
```json
{
  "resourceType": "Coverage",
  "extension": [{
    "url": "http://example.jp/fhir/StructureDefinition/jp-insurance-details",
    "extension": [{
      "url": "insurerNumber",
      "valueString": "12345678"
    }, {
      "url": "cardType",
      "valueCodeableConcept": {
        "coding": [{
          "system": "http://example.jp/insurance-card-type",
          "code": "national-health",
          "display": "National Health Insurance"
        }]
      }
    }, {
      "url": "validityPeriod",
      "valuePeriod": {
        "start": "2024-04-01",
        "end": "2025-03-31"
      }
    }]
  }]
}
```

**StructureDefinition** (simplified):
```json
{
  "resourceType": "StructureDefinition",
  "url": "http://example.jp/fhir/StructureDefinition/jp-insurance-details",
  "name": "JapanInsuranceDetails",
  "kind": "complex-type",
  "type": "Extension",
  "differential": {
    "element": [{
      "id": "Extension.extension:insurerNumber",
      "path": "Extension.extension",
      "sliceName": "insurerNumber",
      "min": 1,
      "max": "1"
    }, {
      "id": "Extension.extension:insurerNumber.url",
      "path": "Extension.extension.url",
      "fixedUri": "insurerNumber"
    }, {
      "id": "Extension.extension:insurerNumber.value[x]",
      "path": "Extension.extension.value[x]",
      "type": [{"code": "string"}]
    }]
  }
}
```

### 3. Standard Extensions

**Registry**: http://hl7.org/fhir/extensions/extensions-registry.html
**Package**: hl7.fhir.uv.extensions

**Best Practice**: Check standard extensions before creating custom

**Common Standard Extensions**:
- `patient-birthPlace`: Address
- `patient-citizenship`: CodeableConcept
- `patient-disability`: CodeableConcept
- `patient-importance`: CodeableConcept
- `patient-religion`: CodeableConcept
- `iso21090-preferred`: boolean (preferred name)
- `data-absent-reason`: code (why data missing)
- `observation-timeOffset`: integer (milliseconds from trigger)

**Example**:
```json
{
  "resourceType": "Patient",
  "name": [{
    "use": "official",
    "family": "Doe",
    "given": ["John"],
    "extension": [{
      "url": "http://hl7.org/fhir/StructureDefinition/iso21090-preferred",
      "valueBoolean": true
    }]
  }]
}
```

## Custom Resource Types (HAPI FHIR Pattern)

### When to Use Custom Resources

**Warning**: Custom resources break interoperability. Use only when:
- No existing FHIR resource fits (even with extensions)
- Data exists independently (not just extension of another resource)
- Organization-internal use only (not shared with external systems)

**Better Alternatives**:
1. Use standard resources with extensions
2. Use Basic resource with extensions
3. Wait for R6 Additional Resources (see below)

### HAPI FHIR Custom Resource Pattern

**Implementation** (Java):
```java
import ca.uhn.fhir.model.api.annotation.*;
import org.hl7.fhir.r4.model.*;

@ResourceDef(name = "CustomPatientDevice", profile = "http://example.com/StructureDefinition/CustomPatientDevice")
public class CustomPatientDevice extends DomainResource {

    @Child(name = "patient", type = {Patient.class}, min = 1, max = 1)
    @Description(shortDefinition = "Patient using the device")
    private Reference patient;

    @Child(name = "device", type = {Device.class}, min = 1, max = 1)
    @Description(shortDefinition = "Device being used")
    private Reference device;

    @Child(name = "startDate", type = {DateTimeType.class}, min = 1, max = 1)
    @Description(shortDefinition = "When device usage started")
    private DateTimeType startDate;

    @Child(name = "readings", max = Child.MAX_UNLIMITED)
    @Description(shortDefinition = "Device readings")
    private List<DeviceReading> readings;

    // Getters, setters, copy(), isEmpty()
}

// Register with FhirContext
FhirContext ctx = FhirContext.forR4();
ctx.registerCustomType(CustomPatientDevice.class);
```

**C# Equivalent** (Firely SDK):
```csharp
[FhirType("CustomPatientDevice", IsResource = true)]
public class CustomPatientDevice : DomainResource
{
    public override string TypeName => "CustomPatientDevice";

    [FhirElement("patient", Order = 10)]
    [References("Patient")]
    public ResourceReference Patient { get; set; }

    [FhirElement("device", Order = 20)]
    [References("Device")]
    public ResourceReference Device { get; set; }

    [FhirElement("startDate", Order = 30)]
    public FhirDateTime StartDate { get; set; }

    [FhirElement("readings", Order = 40)]
    public List<DeviceReading> Readings { get; set; }
}

// Register with ModelInfo
ModelInfo.RegisterCustomType(typeof(CustomPatientDevice));
```

**Challenges**:
- Non-standard resource type
- Clients must support custom type
- Cannot share with other systems
- Search parameters must be manually defined
- Validation requires custom StructureDefinition

**Not Recommended**: Use R6 Additional Resources instead (see below)

## R6 Additional Resources

### What Are Additional Resources?

**Introduction**: FHIR R6 ballot (2024-2025)
**Purpose**: Formally registered resources outside core FHIR specification
**Approval**: Must be registered and approved by HL7
**Benefit**: Faster innovation cycle without waiting 2-3 years for next FHIR release

**Key Difference from Custom Resources**:
- **Custom Resources**: Organization-specific, not shared
- **Additional Resources**: HL7-approved, shared across organizations, canonical URLs

**Examples** (theoretical):
- **ClinicalTrialProtocol**: Detailed research protocol beyond ResearchStudy
- **GenomicVariantAnnotation**: Detailed genomic annotations beyond Observation
- **MedicationFormulary**: Detailed formulary rules beyond MedicationKnowledge

### Additional Resource Lifecycle

```
1. Community identifies need for new resource
   ↓
2. Submit proposal to HL7 work group
   ↓
3. HL7 review and approval (balloting)
   ↓
4. Resource published with canonical URL
   ↓
5. Implementation Guides reference it
   ↓
6. Eventually may be incorporated into core FHIR spec
```

**Timeline**: Months (vs years for core FHIR release)

### Additional Resource Structure

**Canonical URL Pattern**: `http://hl7.org/fhir/additional/StructureDefinition/[ResourceType]`

**Example StructureDefinition**:
```json
{
  "resourceType": "StructureDefinition",
  "url": "http://hl7.org/fhir/additional/StructureDefinition/ClinicalTrialProtocol",
  "name": "ClinicalTrialProtocol",
  "status": "active",
  "kind": "resource",
  "abstract": false,
  "type": "ClinicalTrialProtocol",
  "baseDefinition": "http://hl7.org/fhir/StructureDefinition/DomainResource",
  "derivation": "specialization",
  "differential": {
    "element": [{
      "id": "ClinicalTrialProtocol",
      "path": "ClinicalTrialProtocol",
      "short": "Clinical trial protocol details"
    }, {
      "id": "ClinicalTrialProtocol.identifier",
      "path": "ClinicalTrialProtocol.identifier",
      "min": 1,
      "max": "*",
      "type": [{"code": "Identifier"}]
    }, {
      "id": "ClinicalTrialProtocol.title",
      "path": "ClinicalTrialProtocol.title",
      "min": 1,
      "max": "1",
      "type": [{"code": "string"}]
    }]
  }
}
```

**Distribution**: NPM package (e.g., `hl7.fhir.additional.resources#1.0.0`)

### Server Support for Additional Resources

**Challenge**: R4/R5 parsers don't know about R6 Additional Resources

**Solution Options**:

#### Option 1: Dynamic Resource Loading

```csharp
public interface IAdditionalResourceRegistry
{
    ValueTask RegisterResourceTypeAsync(
        StructureDefinition definition,
        CancellationToken ct = default);

    bool IsRegistered(string resourceType);

    StructureDefinition? GetDefinition(string resourceType);
}

public class DynamicResourceParser
{
    private readonly IAdditionalResourceRegistry _registry;
    private readonly IFhirJsonParser _baseParser;

    public async ValueTask<Resource> ParseAsync(
        string json,
        CancellationToken ct)
    {
        // Parse JSON to extract resourceType
        using var doc = JsonDocument.Parse(json);
        var resourceType = doc.RootElement.GetProperty("resourceType").GetString();

        // Check if it's an Additional Resource
        if (_registry.IsRegistered(resourceType!))
        {
            // Parse as dynamic object or Basic resource with extensions
            return ParseAdditionalResource(json, resourceType!, ct);
        }

        // Standard resource - use base parser
        return _baseParser.Parse<Resource>(json);
    }

    private Resource ParseAdditionalResource(string json, string resourceType, CancellationToken ct)
    {
        var definition = _registry.GetDefinition(resourceType)!;

        // Option A: Wrap in Basic resource
        return new Basic
        {
            Code = new CodeableConcept
            {
                Coding = new List<Coding>
                {
                    new() { System = "http://hl7.org/fhir/additional", Code = resourceType }
                }
            },
            Extension = new List<Extension>
            {
                new()
                {
                    Url = "http://hl7.org/fhir/StructureDefinition/additional-resource-data",
                    Value = new FhirString(json)  // Store original JSON
                }
            }
        };

        // Option B: Use ITypedElement abstraction (better)
        // Return parsed ElementNode that implements ITypedElement
    }
}
```

#### Option 2: ITypedElement Abstraction

**Firely SDK Pattern**: Use `ITypedElement` for schema-agnostic parsing

```csharp
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Specification;

public class AdditionalResourceParser
{
    private readonly IStructureDefinitionSummaryProvider _provider;

    public ITypedElement Parse(string json, string resourceType)
    {
        // Get StructureDefinition for Additional Resource
        var structureDefinition = _provider.Provide($"http://hl7.org/fhir/additional/StructureDefinition/{resourceType}");

        if (structureDefinition == null)
        {
            throw new NotSupportedException($"Additional Resource type '{resourceType}' not registered");
        }

        // Parse JSON into ISourceNode
        var sourceNode = FhirJsonNode.Parse(json);

        // Wrap with type information from StructureDefinition
        return sourceNode.ToTypedElement(_provider, resourceType);
    }
}
```

**Benefit**: Works with existing validation, search indexing, serialization infrastructure

#### Option 3: R6 SDK for R6 Additional Resources

**Simple**: Use FHIR R6 SDK when R6 released
**Limitation**: Requires R6 parser/serializer
**Migration Path**: R4/R5 servers use dynamic parsing until R6 adoption

### Storage Strategy for Additional Resources

**Recommendation**: Same storage pattern as standard resources

**File-Based**:
```
/data/{tenantId}/{version}/ClinicalTrialProtocol/{id}.json
/data/{tenantId}/{version}/ClinicalTrialProtocol/{id}.metadata.ndjson
```

**SQL**:
```sql
INSERT INTO Resource (TenantId, ResourceType, Id, FhirVersion, ...)
VALUES ('acme', 'ClinicalTrialProtocol', '123', '6.0', ...);
```

**Search Indices**: Extract from StructureDefinition search parameters

**CapabilityStatement**: Include in rest.resource array

```json
{
  "resourceType": "CapabilityStatement",
  "fhirVersion": "6.0",
  "rest": [{
    "resource": [{
      "type": "ClinicalTrialProtocol",
      "profile": "http://hl7.org/fhir/additional/StructureDefinition/ClinicalTrialProtocol",
      "interaction": [{"code": "read"}, {"code": "create"}],
      "extension": [{
        "url": "http://hl7.org/fhir/StructureDefinition/capabilitystatement-additional-resource",
        "valueBoolean": true
      }]
    }]
  }]
}
```

## Custom Profiles

### What Are Profiles?

**Definition**: Constraints on existing resources without creating new types
**Use Case**: Add extensions, tighten cardinality, bind value sets

**Example**: US Core Patient Profile

```json
{
  "resourceType": "StructureDefinition",
  "url": "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient",
  "name": "USCorePatientProfile",
  "kind": "resource",
  "abstract": false,
  "type": "Patient",
  "baseDefinition": "http://hl7.org/fhir/StructureDefinition/Patient",
  "derivation": "constraint",
  "differential": {
    "element": [{
      "id": "Patient.extension:race",
      "path": "Patient.extension",
      "sliceName": "race",
      "min": 0,
      "max": "*",
      "type": [{
        "code": "Extension",
        "profile": ["http://hl7.org/fhir/us/core/StructureDefinition/us-core-race"]
      }]
    }, {
      "id": "Patient.identifier",
      "path": "Patient.identifier",
      "min": 1,
      "mustSupport": true
    }, {
      "id": "Patient.name",
      "path": "Patient.name",
      "min": 1,
      "mustSupport": true
    }]
  }
}
```

**Profile Claims** (in resource):
```json
{
  "resourceType": "Patient",
  "meta": {
    "profile": ["http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"]
  },
  "identifier": [{"system": "http://hl7.org/fhir/sid/us-ssn", "value": "123-45-6789"}],
  "name": [{"family": "Doe", "given": ["John"]}],
  "extension": [{
    "url": "http://hl7.org/fhir/us/core/StructureDefinition/us-core-race",
    "extension": [{
      "url": "ombCategory",
      "valueCoding": {
        "system": "urn:oid:2.16.840.1.113883.6.238",
        "code": "2106-3",
        "display": "White"
      }
    }]
  }]
}
```

### Server Requirements for Profiles

**Discovery**:
- Load profiles from Implementation Guides (NPM packages)
- Store in `IStructureDefinitionSummaryProvider`

**Validation**:
- Tier 2 validation (opt-in via `$validate` or header)
- Check mustSupport, cardinality, value set bindings
- Validate extensions

**Search**:
- Extract extension values if search parameters defined
- Index according to profile-specific search parameters

**CapabilityStatement**:
- List supported profiles in `rest.resource.supportedProfile`

```json
{
  "type": "Patient",
  "supportedProfile": [
    "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"
  ]
}
```

## Architecture Recommendations

### 1. Pluggable Schema Provider

**Current Design** (from your investigations):

```csharp
public interface IFhirSchemaProvider
{
    IEnumerable<string> ResourceTypeNames { get; }
    IStructureDefinitionSummary? Provide(string canonical);
}

public class CompositeSchemaProvider : IFhirSchemaProvider
{
    private readonly IFhirSchemaProvider _baseProvider;  // Base FHIR R4/R5/R6
    private readonly ConcurrentDictionary<string, IStructureDefinitionSummary> _profileCache;  // IGs
    private readonly ConcurrentDictionary<string, IStructureDefinitionSummary> _additionalResourceCache;  // R6 Additional Resources

    public IStructureDefinitionSummary? Provide(string canonical)
    {
        // Check Additional Resources first (most specific)
        if (_additionalResourceCache.TryGetValue(canonical, out var additional))
            return additional;

        // Check IG profiles
        if (_profileCache.TryGetValue(canonical, out var profile))
            return profile;

        // Fall back to base FHIR
        return _baseProvider.Provide(canonical);
    }

    public IEnumerable<string> ResourceTypeNames =>
        _baseProvider.ResourceTypeNames
            .Concat(_additionalResourceCache.Keys)
            .Distinct();
}
```

**Enhancement**: Add Additional Resource support

```csharp
public interface IAdditionalResourceProvider
{
    ValueTask<IReadOnlyList<StructureDefinition>> GetAdditionalResourcesAsync(
        FhirVersion version,
        CancellationToken ct = default);

    ValueTask RegisterAdditionalResourceAsync(
        StructureDefinition definition,
        CancellationToken ct = default);

    bool IsAdditionalResource(string resourceType);
}
```

### 2. Extension Indexing

**Challenge**: Index extension values for searching

**Example**: Search by patient pet name

```http
GET /Patient?pet-name=Fluffy
```

**Implementation**:

```csharp
public class ExtensionSearchParameterIndexer
{
    private readonly ISearchParameterRepository _searchParamRepo;

    public async ValueTask<IReadOnlyList<SearchIndexValue>> ExtractExtensionValuesAsync(
        ITypedElement resource,
        SearchParameterInfo searchParam,
        CancellationToken ct)
    {
        // FHIRPath expression for extension-based search parameter
        // Example: Patient.extension.where(url='http://example.com/pet-name').value

        var expression = searchParam.Expression;
        var values = _fhirPathEvaluator.Evaluate(resource, expression);

        var indices = new List<SearchIndexValue>();

        foreach (var value in values)
        {
            switch (searchParam.Type)
            {
                case SearchParamType.String:
                    indices.Add(new StringSearchIndexValue
                    {
                        ParamName = searchParam.Code,
                        Value = value.ToString()
                    });
                    break;

                case SearchParamType.Token:
                    var coding = value as Coding;
                    indices.Add(new TokenSearchIndexValue
                    {
                        ParamName = searchParam.Code,
                        System = coding?.System,
                        Code = coding?.Code
                    });
                    break;

                // ... other types
            }
        }

        return indices;
    }
}
```

**SearchParameter Definition** (for extension):

```json
{
  "resourceType": "SearchParameter",
  "url": "http://example.com/SearchParameter/patient-pet-name",
  "code": "pet-name",
  "base": ["Patient"],
  "type": "string",
  "expression": "Patient.extension.where(url='http://example.com/fhir/StructureDefinition/patient-pet-name').value",
  "description": "Search by patient's pet name"
}
```

### 3. Dynamic Resource Registration

**Scenario**: R6 Additional Resource published after server deployment

**Solution**: Hot-reload capability

```csharp
public interface IImplementationGuideManager
{
    ValueTask LoadImplementationGuideAsync(
        string packageId,
        string version,
        CancellationToken ct = default);

    ValueTask UnloadImplementationGuideAsync(
        string packageId,
        CancellationToken ct = default);

    ValueTask<IReadOnlyList<string>> GetLoadedImplementationGuidesAsync(
        FhirVersion version,
        CancellationToken ct = default);
}

// POST /$load-ig
{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "package",
    "valueString": "hl7.fhir.additional.clinicaltrial#1.0.0"
  }]
}
```

**Effects**:
1. Download NPM package from packages.fhir.org
2. Load StructureDefinitions into `CompositeSchemaProvider`
3. Register new resource types (Additional Resources)
4. Load search parameters
5. Invalidate CapabilityStatement cache
6. Rebuild CapabilityStatement with new resource types

### 4. Validation Strategy

**Tier 1** (structural, <50ms):
- Validate standard FHIR resources
- Skip extension validation
- Skip profile validation

**Tier 2** (profile, <5s):
- Validate extensions against StructureDefinitions
- Validate profiles (US Core, etc.)
- Validate Additional Resources against definitions

**Example**:

```csharp
public async ValueTask<OperationOutcome> ValidateAsync(
    ITypedElement resource,
    FhirRequestContext context,
    ValidationLevel level,
    CancellationToken ct)
{
    var outcome = new OperationOutcome { Issue = new List<OperationOutcome.IssueComponent>() };

    // Tier 1: Structural
    if (level >= ValidationLevel.Structural)
    {
        var structuralIssues = await ValidateStructuralAsync(resource, context.SchemaProvider, ct);
        outcome.Issue.AddRange(structuralIssues);
    }

    // Tier 2: Profile
    if (level >= ValidationLevel.Profile)
    {
        // Validate extensions
        var extensionIssues = await ValidateExtensionsAsync(resource, context.SchemaProvider, ct);
        outcome.Issue.AddRange(extensionIssues);

        // Validate profile claims
        var profiles = resource.Get("meta.profile")?.Select(p => p.ToString()) ?? Enumerable.Empty<string>();
        foreach (var profileUrl in profiles)
        {
            var profileDefinition = context.SchemaProvider.Provide(profileUrl);
            if (profileDefinition != null)
            {
                var profileIssues = await ValidateAgainstProfileAsync(resource, profileDefinition, ct);
                outcome.Issue.AddRange(profileIssues);
            }
        }
    }

    return outcome;
}
```

## Use Case Examples

### Example 1: Japanese Insurance Extension

**Requirement**: Add Japanese insurance card details to Coverage resource

**Solution**: Complex extension

**StructureDefinition**: (shown above in Complex Extensions section)

**SearchParameter**:
```json
{
  "resourceType": "SearchParameter",
  "url": "http://example.jp/SearchParameter/coverage-insurer-number",
  "code": "jp-insurer-number",
  "base": ["Coverage"],
  "type": "string",
  "expression": "Coverage.extension.where(url='http://example.jp/fhir/StructureDefinition/jp-insurance-details').extension.where(url='insurerNumber').value"
}
```

**Search**:
```http
GET /Coverage?jp-insurer-number=12345678
```

### Example 2: Clinical Trial Protocol (R6 Additional Resource)

**Requirement**: Store detailed clinical trial protocols beyond ResearchStudy

**Solution**: R6 Additional Resource

**StructureDefinition**: (shown above in Additional Resources section)

**Package**: `hl7.fhir.additional.clinicaltrial#1.0.0`

**Load**:
```http
POST /$load-ig
{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "package",
    "valueString": "hl7.fhir.additional.clinicaltrial#1.0.0"
  }]
}
```

**Create**:
```http
POST /R6/ClinicalTrialProtocol
{
  "resourceType": "ClinicalTrialProtocol",
  "identifier": [{
    "system": "http://clinicaltrials.gov",
    "value": "NCT01234567"
  }],
  "title": "Phase III Trial of Drug X",
  "status": "active"
}
```

**CapabilityStatement Updated**:
```json
{
  "rest": [{
    "resource": [{
      "type": "ClinicalTrialProtocol",
      "profile": "http://hl7.org/fhir/additional/StructureDefinition/ClinicalTrialProtocol",
      "interaction": [{"code": "read"}, {"code": "create"}, {"code": "search-type"}]
    }]
  }]
}
```

### Example 3: Genomics Extensions (Real-World)

**FHIR Genomics IG**: http://hl7.org/fhir/uv/genomics-reporting/

**Extensions**:
- `obs-genetics-gene` (CodeableConcept): Gene studied
- `obs-genetics-dna-sequence-variant` (string): DNA sequence variant
- `obs-genetics-amino-acid-change` (string): Amino acid change

**Usage**:
```json
{
  "resourceType": "Observation",
  "meta": {
    "profile": ["http://hl7.org/fhir/uv/genomics-reporting/StructureDefinition/variant"]
  },
  "code": {
    "coding": [{
      "system": "http://loinc.org",
      "code": "69548-6",
      "display": "Genetic variant assessment"
    }]
  },
  "extension": [{
    "url": "http://hl7.org/fhir/uv/genomics-reporting/StructureDefinition/gene-studied",
    "valueCodeableConcept": {
      "coding": [{
        "system": "http://www.genenames.org/geneId",
        "code": "HGNC:1100",
        "display": "BRCA1"
      }]
    }
  }, {
    "url": "http://hl7.org/fhir/uv/genomics-reporting/StructureDefinition/dna-sequence-variant",
    "valueString": "NC_000017.11:g.43092919dup"
  }]
}
```

**Server Support**:
1. Load IG: `hl7.fhir.uv.genomics-reporting#2.0.0`
2. Register extensions in `CompositeSchemaProvider`
3. Index extension values using search parameters from IG
4. Validate against variant profile (Tier 2)

## Performance Considerations

### Extension Parsing Overhead

**Measurement**: Extension parsing adds ~5-10% overhead vs base resources

**Optimization**:
- Cache extension definitions
- Lazy-load extension values (don't parse until accessed)
- Use `ITypedElement` for efficient traversal

### Additional Resource Loading

**Concern**: Loading many IGs at startup

**Strategy**:
- Lazy-load IGs on first use
- Cache StructureDefinitions in memory
- Invalidate cache only when IG added/removed

**Benchmark**:
- Load US Core IG (~200 profiles): <1 second
- Load Genomics IG (~50 profiles): <500ms
- Parse extension in resource: <1ms (cached), <10ms (uncached)

### Search Index Size

**Concern**: Extensions increase index size

**Example**:
- Patient with 0 extensions: ~500 bytes index
- Patient with 10 extensions: ~1KB index (2x)

**Mitigation**:
- Only index searchable extensions (defined in SearchParameter)
- Use sparse indices (NULL for missing extensions)

## Phase Integration

### Phase 11: Implementation Guides (Weeks 50-55) - ADR-2514

**Deliverables**:
- NPM package loading from packages.fhir.org
- `CompositeSchemaProvider` (base + IG profiles)
- Extension definition loading
- Profile validation (Tier 2)
- Extension-based search parameters
- **NEW**: R6 Additional Resource support

**E2E Tests**:
- `ImplementationGuideTests.cs` (15+ tests)
  - Load US Core IG
  - Validate US Core Patient
  - Search by extension (race, ethnicity)
  - Load genomics IG
  - Validate variant Observation

### NEW Phase 19: Custom Structures and Extensions (Weeks 91-94)

**Goal**: Production-ready extension and custom structure support

**Deliverables**:
- Extension indexing for search
- Custom SearchParameter registration for extensions
- R6 Additional Resource hot-loading (`POST /$load-ig`)
- Dynamic resource type registration
- CapabilityStatement updates when IGs loaded
- Extension validation (Tier 2)

**E2E Tests**:
- `ExtensionSearchTests.cs` (10+ tests)
- `AdditionalResourceTests.cs` (15+ tests)
- `DynamicIgLoadingTests.cs` (8+ tests)

**Key Innovation**: Hot-reload IGs without server restart, enabling R6 Additional Resources

## Summary

### Key Decisions

1. ✅ **Extensions Preferred**: Use extensions over custom resources when possible
2. ✅ **Standard Extensions First**: Check HL7 standard extensions before creating custom
3. ✅ **R6 Additional Resources**: Support via `IAdditionalResourceProvider` and dynamic loading
4. ✅ **CompositeSchemaProvider**: Unify base FHIR + IGs + Additional Resources
5. ✅ **Extension Search**: Index extension values using SearchParameter definitions
6. ✅ **Hot-Reload IGs**: `POST /$load-ig` for dynamic IG loading
7. ✅ **Tier 2 Validation**: Validate extensions and profiles opt-in

### Industry Alignment

**Extension Support**: ✅ Universal (all FHIR servers)
**Custom Resources**: ⚠️ HAPI FHIR only (not recommended)
**R6 Additional Resources**: 🔜 Emerging (R6 ballot 2024-2025)
**Dynamic IG Loading**: ✅ Smile CDR, Firely Server (via API)

### Recommended Approach

**For FHIR Server v2**:

✅ **DO**:
- Support standard and custom extensions
- Load IGs from NPM packages
- Index extension values for search
- Validate extensions in Tier 2
- Support R6 Additional Resources via dynamic loading
- Use `ITypedElement` for schema-agnostic parsing
- Expose extensions in CapabilityStatement

❌ **DON'T**:
- Create custom resource types (use R6 Additional Resources instead)
- Skip extension validation (at least in Tier 2)
- Hardcode extension definitions (load from IGs)
- Ignore R6 Additional Resources (future is here)

## References

- FHIR Extensions: https://build.fhir.org/extensibility.html
- Extension Registry: http://hl7.org/fhir/extensions/extensions-registry.html
- StructureDefinition: https://build.fhir.org/structuredefinition.html
- HAPI FHIR Custom Structures: https://hapifhir.io/hapi-fhir/docs/model/custom_structures.html
- Firely SDK Extensions: https://docs.fire.ly/projects/Firely-NET-SDK/en/latest/model/working-with-extensions.html
- R6 Additional Resources: https://build.fhir.org/versions.html#extensions-examples-complex
- US Core IG: http://hl7.org/fhir/us/core/
- Genomics IG: http://hl7.org/fhir/uv/genomics-reporting/

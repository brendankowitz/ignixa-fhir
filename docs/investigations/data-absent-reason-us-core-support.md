# US Core Data-Absent-Reason Extension Support

**Status**: Research
**Date**: 2025-11-11
**Related**: US Core compliance, FHIR validation

## Executive Summary

This document researches how to support the US Core requirement for adding `data-absent-reason` extensions to mandatory elements that have missing data. The recommended approach is to implement a **Medino pipeline behavior** that intercepts resource creation/update operations and automatically injects the extension when US Core profiles are active.

## Problem Statement

### US Core Requirement

US Core profiles define mandatory elements (min > 0) that must be present in conformant resources. However, real-world systems often lack complete data. US Core addresses this with the following rule:

> **For mandatory elements with missing data and unknown reason**: Include the element with a `data-absent-reason` extension using code `"unknown"`.

**Example**: US Core Patient requires a name (min=1), but if the name is truly unavailable:

```json
{
  "resourceType": "Patient",
  "name": [
    {
      "extension": [
        {
          "url": "http://hl7.org/fhir/StructureDefinition/data-absent-reason",
          "valueCode": "unknown"
        }
      ]
    }
  ]
}
```

### Current System Behavior

The Ignixa FHIR server currently:
1. **Validates** incoming resources using `ValidationBehavior` (Medino pipeline)
2. **Rejects** resources with missing mandatory elements via `CardinalityCheck`
3. **Does NOT** automatically inject `data-absent-reason` extensions

This means clients must manually add these extensions, or validation fails for US Core profiles.

## FHIR Specification Details

### data-absent-reason Extension

- **URL**: `http://hl7.org/fhir/StructureDefinition/data-absent-reason`
- **Type**: Extension with `valueCode`
- **Allowed Values**:
  - `unknown` - Value expected but not known
  - `asked-unknown` - Recipient asked but doesn't know
  - `temp-unknown` - Temporarily unavailable
  - `not-asked` - Not asked (workflow limitation)
  - `asked-declined` - Asked but declined to answer
  - `masked` - Privacy/security restriction
  - `not-applicable` - Question is not applicable
  - `unsupported` - Source system cannot store this type
  - `as-text` - Value is in narrative text
  - `error` - Invalid entry or system error
  - `not-a-number` - NaN (special numeric value)
  - `negative-infinity` - Negative infinity
  - `positive-infinity` - Positive infinity
  - `not-performed` - Event/action was not performed

### US Core Specific Guidance

1. **Optional elements (min=0)**: Omit entirely if data is missing
2. **Mandatory elements (min>0)**:
   - **If data missing with unknown reason**: Add element with `data-absent-reason` extension (code: `unknown`)
   - **If coded element with extensible/example binding**: Provide text-only or use "unknown" code from ValueSet
   - **If required binding without unknown code**: Return HTTP 404 for read, exclude from search results

### Example US Core Mandatory Elements

**US Core Patient**:
- `identifier` (1..*) - At least one identifier required
- `name` (1..*) - At least one name required (with constraint: family and/or given, or data-absent-reason)

**US Core Observation**:
- `status` (1..1) - Required binding, no data-absent-reason allowed (must have actual status)
- `category` (1..*) - Slice with required category codes
- `code` (1..1) - What was observed
- `subject` (1..1) - Reference to patient

## Architecture Analysis

### Current Medino Pipeline

```
HTTP Request
    ↓
TenantResolutionMiddleware (sets TenantId in HttpContext)
    ↓
Endpoint Handler (Minimal API)
    ↓
Mediator.SendAsync(CreateOrUpdateResourceCommand)
    ↓
┌─────────────────────────────────────────────────┐
│ Medino Pipeline Behaviors (Sequential)          │
├─────────────────────────────────────────────────┤
│ 1. CapabilityEnforcementBehavior               │
│    - Checks CapabilityStatement for operation   │
│    - Evaluates FHIRPath expressions             │
│                                                  │
│ 2. ValidationBehavior                           │
│    - Gets tenant validation tier                │
│    - Resolves validation schema (base + IGs)    │
│    - Runs CardinalityCheck for min/max          │
│    - Runs TypeCheck, etc.                       │
│                                                  │
│ [PROPOSED: DataAbsentReasonBehavior]            │
│    - Detects US Core profiles                   │
│    - Injects data-absent-reason for missing     │
│      mandatory elements                         │
│                                                  │
│ 3. CreateOrUpdateResourceHandler                │
│    - Persists resource to database              │
└─────────────────────────────────────────────────┘
    ↓
HTTP Response (201 Created / 200 OK)
```

### Key System Components

**1. Package Loading & Detection**
- `LoadPackageCommand` / `LoadPackageHandler` - Loads FHIR packages (IGs) from NPM
- `IPackageLoaded` event → `PackageLoadedNotificationHandler` - Invalidates caches when packages load
- Can detect when `hl7.fhir.us.core` is loaded

**2. Validation Infrastructure**
- `ValidationBehavior` - Medino pipeline behavior for resource validation
- `ValidationSchema` - Tier-aware validation (Fast/Spec/Profile)
- `CardinalityCheck` - Checks min..max cardinality, **fails if min > actual count**
- `IValidationSchemaResolver` - Resolves schemas including loaded IG profiles

**3. Schema/Profile Information**
- `IStructureDefinitionSummaryProvider` - Provides base FHIR structure definitions
- `CompositeStructureDefinitionSummaryProvider` - Merges base + loaded packages
- `IElementDefinitionSummary` - Contains `IsRequired` (min > 0) flag
- `PackageResourceProvider` - Reads StructureDefinitions from loaded packages

**4. Resource Serialization**
- `ResourceElementsSerializer` - Streaming JSON filter for `_elements` parameter
- Uses `Utf8JsonReader` / `Utf8JsonWriter` for zero-copy filtering
- Already has pattern for injecting mandatory elements (id, meta, resourceType)

## Solution Approaches

### Option 1: Pre-Validation Medino Behavior (RECOMMENDED)

**Concept**: Add a new `DataAbsentReasonBehavior` that runs **before** `ValidationBehavior` in the Medino pipeline.

#### Architecture

```csharp
public class DataAbsentReasonBehavior : IPipelineBehavior<CreateOrUpdateResourceCommand, ResourceKey>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IValidationSchemaResolver _schemaResolver;
    private readonly ILogger<DataAbsentReasonBehavior> _logger;

    public async Task<ResourceKey> HandleAsync(
        CreateOrUpdateResourceCommand request,
        RequestHandlerDelegate<ResourceKey> next,
        CancellationToken cancellationToken)
    {
        // 1. Get tenant and FHIR version from HttpContext
        var tenantId = _httpContextAccessor.HttpContext?.Items["TenantId"] as int?;
        var fhirVersion = FhirVersionExtractor.ExtractFhirVersion(_httpContextAccessor.HttpContext);

        // 2. Resolve validation schema for this resource
        var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{request.ResourceType}";
        var schema = _schemaResolver.GetSchema(canonicalUrl);

        // 3. Check if US Core profile is active (heuristic or explicit check)
        if (!IsUSCoreProfile(schema))
        {
            return await next(); // Not US Core, skip
        }

        // 4. Walk the resource JsonNode and inject data-absent-reason for missing mandatory elements
        InjectDataAbsentReason(request.JsonNode.MutableNode, schema);

        // 5. Continue to validation (which should now pass)
        return await next();
    }

    private bool IsUSCoreProfile(ValidationSchema schema)
    {
        // Heuristic 1: Check canonical URL for "us.core"
        // Heuristic 2: Check if schema.CanonicalUrl starts with "http://hl7.org/fhir/us/core/"
        // Heuristic 3: Query tenant packages for "hl7.fhir.us.core"
    }

    private void InjectDataAbsentReason(JsonNode resourceNode, ValidationSchema schema)
    {
        // Walk elements marked as IsRequired
        // For each missing element:
        //   1. Add empty element/array
        //   2. Add extension with data-absent-reason = "unknown"
    }
}
```

#### Registration (Program.cs)

```csharp
// Register AFTER ValidationBehavior but BEFORE handler
containerBuilder.RegisterType<DataAbsentReasonBehavior>()
    .As<IPipelineBehavior<CreateOrUpdateResourceCommand, ResourceKey>>()
    .InstancePerLifetimeScope();
```

#### Pros
✅ **Automatic**: Clients don't need to manually add extensions
✅ **Transparent**: Works with existing validation pipeline
✅ **Medino pattern**: Consistent with `CapabilityEnforcementBehavior` and `ValidationBehavior`
✅ **Tenant-aware**: Can enable/disable per tenant
✅ **Profile-aware**: Only activates for US Core profiles
✅ **Testable**: Can unit test behavior in isolation

#### Cons
❌ **Mutates request**: Modifies incoming resource before persistence
❌ **Heuristic detection**: Need to reliably detect when US Core is active
❌ **Complexity**: Requires walking JsonNode tree and understanding element structure

---

### Option 2: Extend ResourceElementsSerializer

**Concept**: Add a `DataAbsentReasonSerializer` subclass or decorator that wraps `ResourceElementsSerializer`.

#### Architecture

```csharp
public class DataAbsentReasonSerializer : ResourceElementsSerializer
{
    public static void WriteResourceWithDataAbsentReason(
        FhirJsonWriter writer,
        ReadOnlyMemory<byte> resourceBytes,
        IStructureDefinitionSummaryProvider schemaProvider,
        string resourceType,
        bool enableUSCoreMode)
    {
        if (!enableUSCoreMode)
        {
            // Fallback to normal serialization
            return;
        }

        // 1. Parse JSON to detect missing mandatory elements
        // 2. Re-serialize with data-absent-reason extensions injected
    }
}
```

#### Pros
✅ **Reuses existing pattern**: Similar to element filtering
✅ **Streaming**: Can leverage `Utf8JsonReader` / `Utf8JsonWriter`

#### Cons
❌ **Wrong layer**: `ResourceElementsSerializer` is for **output** (Bundle serialization with `_elements`)
❌ **Not for input**: This needs to run on **input** (create/update), not output (read/search)
❌ **Architectural mismatch**: Serializers shouldn't mutate incoming data

**Verdict**: ❌ Not recommended - wrong architectural layer

---

### Option 3: Custom Validation Check (DataAbsentReasonCheck)

**Concept**: Add a new `IValidationCheck` that automatically fixes missing mandatory elements during validation.

#### Architecture

```csharp
public class DataAbsentReasonCheck : IValidationCheck
{
    private readonly string _elementName;
    private readonly int _min;

    public ValidationResult Validate(ISourceNode node, ValidationSettings settings, ValidationState state)
    {
        var children = node.Children(_elementName).ToList();

        if (children.Count < _min)
        {
            // Instead of failing, inject data-absent-reason
            // Problem: ISourceNode is read-only, can't mutate
            return ValidationResult.Failure(...); // Still fails
        }

        return ValidationResult.Success();
    }
}
```

#### Pros
✅ **Integrated with validation**: Runs during validation tier
✅ **Schema-driven**: Uses existing element metadata

#### Cons
❌ **Read-only source**: `ISourceNode` is immutable, can't inject extensions
❌ **Validation shouldn't mutate**: Anti-pattern for validators to modify input
❌ **Architectural violation**: Validation is for **checking**, not **fixing**

**Verdict**: ❌ Not recommended - violates validation principles

---

### Option 4: Package-Aware Event Handler

**Concept**: Subscribe to `IPackageLoaded` events and enable a global flag when US Core is detected.

#### Architecture

```csharp
public class USCoreDetectionHandler : INotificationHandler<IPackageLoaded>
{
    private static readonly ConcurrentDictionary<int, bool> _uscoreEnabledByTenant = new();

    public Task HandleAsync(IPackageLoaded evt, CancellationToken cancellationToken)
    {
        if (evt.PackageId.StartsWith("hl7.fhir.us.core", StringComparison.OrdinalIgnoreCase))
        {
            _uscoreEnabledByTenant[evt.TenantId] = true;
            _logger.LogInformation("US Core mode enabled for tenant {TenantId}", evt.TenantId);
        }
        return Task.CompletedTask;
    }

    public static bool IsUSCoreEnabled(int tenantId) => _uscoreEnabledByTenant.GetValueOrDefault(tenantId);
}
```

Then use this flag in `DataAbsentReasonBehavior`:

```csharp
if (!USCoreDetectionHandler.IsUSCoreEnabled(tenantId))
{
    return await next(); // US Core not loaded, skip
}
```

#### Pros
✅ **Explicit detection**: Clear signal when US Core is loaded
✅ **Event-driven**: Leverages existing package loading infrastructure
✅ **Tenant-scoped**: Per-tenant US Core enablement

#### Cons
❌ **Global state**: Static dictionary for tenant flags
❌ **Tightly coupled**: Hardcodes US Core package name
❌ **Doesn't scale**: What about other IGs with similar requirements?

**Verdict**: ⚠️ Useful as a **helper** for Option 1, but not standalone

---

## RECOMMENDED APPROACH

### Hybrid: Medino Behavior + Package Detection

Combine **Option 1** (Medino Behavior) with **Option 4** (Package Detection) for a robust solution.

#### Implementation Plan

**Phase 1: Detection Infrastructure**

1. **Create US Core Detection Service**

```csharp
// File: src/Ignixa.Application/Infrastructure/Services/ProfileDetectionService.cs
public interface IProfileDetectionService
{
    bool IsProfileActive(int tenantId, string profilePattern);
    bool IsUSCoreActive(int tenantId);
}

public class ProfileDetectionService : IProfileDetectionService
{
    private readonly IPackageResourceRepository _packageRepo;

    public bool IsUSCoreActive(int tenantId)
    {
        return IsProfileActive(tenantId, "hl7.fhir.us.core");
    }

    public bool IsProfileActive(int tenantId, string profilePattern)
    {
        // Query PackageResourceEntity table for loaded packages matching pattern
        // Cache results for performance
    }
}
```

2. **Register in Program.cs**

```csharp
containerBuilder.RegisterType<ProfileDetectionService>()
    .As<IProfileDetectionService>()
    .SingleInstance();
```

**Phase 2: Data Absent Reason Injection Behavior**

3. **Create Medino Behavior**

```csharp
// File: src/Ignixa.Application/Infrastructure/Behaviors/DataAbsentReasonBehavior.cs
public class DataAbsentReasonBehavior : IPipelineBehavior<CreateOrUpdateResourceCommand, ResourceKey>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IProfileDetectionService _profileDetection;
    private readonly Func<FhirSpecification, IValidationSchemaResolver> _schemaResolverFactory;
    private readonly ILogger<DataAbsentReasonBehavior> _logger;

    public async Task<ResourceKey> HandleAsync(
        CreateOrUpdateResourceCommand request,
        RequestHandlerDelegate<ResourceKey> next,
        CancellationToken cancellationToken)
    {
        // 1. Get tenant and FHIR version
        var httpContext = _httpContextAccessor.HttpContext;
        var tenantId = httpContext?.Items["TenantId"] as int?;
        var fhirVersion = FhirVersionExtractor.ExtractFhirVersion(httpContext);

        // 2. Check if US Core is active for this tenant
        if (!tenantId.HasValue || !_profileDetection.IsUSCoreActive(tenantId.Value))
        {
            return await next(); // Skip if not US Core tenant
        }

        // 3. Resolve validation schema (includes US Core profiles)
        var schemaResolver = _schemaResolverFactory(fhirVersion);
        var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{request.ResourceType}";
        var schema = schemaResolver.GetSchema(canonicalUrl);

        if (schema == null)
        {
            return await next(); // No schema, skip
        }

        // 4. Inject data-absent-reason for missing mandatory elements
        bool modified = InjectDataAbsentReasonExtensions(
            request.JsonNode.MutableNode,
            schema,
            request.ResourceType);

        if (modified)
        {
            _logger.LogInformation(
                "Injected data-absent-reason for missing mandatory elements in {ResourceType}/{Id}",
                request.ResourceType,
                request.Id);
        }

        // 5. Continue to validation (should now pass CardinalityCheck)
        return await next();
    }

    private bool InjectDataAbsentReasonExtensions(
        JsonNode resourceNode,
        ValidationSchema schema,
        string resourceType)
    {
        bool modified = false;

        // Get all mandatory elements from schema
        var mandatoryElements = GetMandatoryElements(schema, resourceType);

        foreach (var elementDef in mandatoryElements)
        {
            // Check if element exists in resource
            if (!resourceNode.AsObject().ContainsKey(elementDef.ElementName))
            {
                // Element is missing - inject empty element with data-absent-reason
                InjectEmptyElementWithExtension(resourceNode, elementDef);
                modified = true;
            }
            // TODO: Handle arrays with min > 0 but actualCount == 0
        }

        return modified;
    }

    private IEnumerable<IElementDefinitionSummary> GetMandatoryElements(
        ValidationSchema schema,
        string resourceType)
    {
        // Access underlying StructureDefinition from schema
        // Filter to elements where IsRequired == true (min > 0)
        // Skip elements that CANNOT have data-absent-reason (e.g., status with required binding)
    }

    private void InjectEmptyElementWithExtension(JsonNode resourceNode, IElementDefinitionSummary elementDef)
    {
        var elementName = elementDef.ElementName;

        if (elementDef.IsCollection)
        {
            // Array element: name: [{ extension: [...] }]
            var arrayNode = new JsonArray();
            var emptyElement = new JsonObject();
            AddDataAbsentReasonExtension(emptyElement);
            arrayNode.Add(emptyElement);
            resourceNode[elementName] = arrayNode;
        }
        else
        {
            // Single element: name: { extension: [...] }
            var emptyElement = new JsonObject();
            AddDataAbsentReasonExtension(emptyElement);
            resourceNode[elementName] = emptyElement;
        }
    }

    private void AddDataAbsentReasonExtension(JsonObject element)
    {
        var extension = new JsonObject
        {
            ["url"] = "http://hl7.org/fhir/StructureDefinition/data-absent-reason",
            ["valueCode"] = "unknown"
        };

        if (!element.ContainsKey("extension"))
        {
            element["extension"] = new JsonArray();
        }

        ((JsonArray)element["extension"]!).Add(extension);
    }
}
```

4. **Register Behavior (Program.cs)**

```csharp
// Register BEFORE ValidationBehavior so it runs first
// Order: CapabilityEnforcement → DataAbsentReason → Validation → Handler
containerBuilder.RegisterType<DataAbsentReasonBehavior>()
    .As<IPipelineBehavior<CreateOrUpdateResourceCommand, ResourceKey>>()
    .InstancePerLifetimeScope();
```

**Phase 3: Configuration & Testing**

5. **Add Tenant Configuration Option**

```csharp
// Allow tenants to opt-in/opt-out
public class TenantConfiguration
{
    public bool EnableAutoDataAbsentReason { get; set; } = true; // Default on for US Core tenants
}
```

6. **Unit Tests**

```csharp
// File: test/Ignixa.Application.Tests/Infrastructure/Behaviors/DataAbsentReasonBehaviorTests.cs
public class DataAbsentReasonBehaviorTests
{
    [Fact]
    public async Task GivenUSCorePatientWithMissingName_WhenHandling_ThenInjectsDataAbsentReason()
    {
        // Arrange: Patient without name (required in US Core)
        var command = new CreateOrUpdateResourceCommand(...)
        {
            JsonNode = new ResourceJsonNode
            {
                ResourceType = "Patient",
                Id = "123",
                MutableNode = JsonNode.Parse("""
                {
                  "resourceType": "Patient",
                  "id": "123",
                  "identifier": [{"system": "http://example.com", "value": "MRN123"}]
                  // name is missing!
                }
                """)
            }
        };

        // Act
        var result = await _behavior.HandleAsync(command, _next, CancellationToken.None);

        // Assert
        var nameArray = command.JsonNode.MutableNode["name"] as JsonArray;
        Assert.NotNull(nameArray);
        Assert.Single(nameArray);

        var nameObject = nameArray[0] as JsonObject;
        var extensions = nameObject["extension"] as JsonArray;
        Assert.NotNull(extensions);

        var darExtension = extensions.Single();
        Assert.Equal("http://hl7.org/fhir/StructureDefinition/data-absent-reason",
            darExtension["url"].GetValue<string>());
        Assert.Equal("unknown", darExtension["valueCode"].GetValue<string>());
    }
}
```

7. **Integration Tests**

```csharp
[Fact]
public async Task GivenUSCoreTenant_WhenCreatingPatientWithoutName_ThenSucceeds()
{
    // Load US Core package
    await LoadPackage("hl7.fhir.us.core", "5.0.1");

    // POST Patient without name (should auto-inject data-absent-reason)
    var response = await _client.PostAsync("/Patient", ...);

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);

    // GET and verify extension was added
    var patient = await _client.GetAsync("/Patient/123");
    var json = await patient.Content.ReadAsStringAsync();
    Assert.Contains("data-absent-reason", json);
}
```

## Implementation Considerations

### 1. Element Detection Rules

**Which elements need data-absent-reason?**

- ✅ **Non-coded elements** with min > 0 (e.g., Patient.name)
- ✅ **Coded elements** with extensible/example/preferred binding
- ❌ **Required binding without unknown code** (e.g., status fields) - Cannot use data-absent-reason

**Implementation**: Query `ElementDefinition.binding.strength` from StructureDefinition

### 2. Complex Element Structures

**Patient.name constraint** (US Core):
```
us-core-6: "At least name.given and/or name.family are present
            or Data Absent Reason Extension is present"
```

This requires **FHIRPath evaluation** or **constraint-aware logic**:

```csharp
// Check if name array exists but all entries are empty
var nameArray = resourceNode["name"] as JsonArray;
if (nameArray != null && nameArray.Count > 0)
{
    foreach (var nameObj in nameArray.Cast<JsonObject>())
    {
        bool hasGiven = nameObj.ContainsKey("given");
        bool hasFamily = nameObj.ContainsKey("family");
        bool hasExtension = nameObj.ContainsKey("extension") &&
            HasDataAbsentReasonExtension((JsonArray)nameObj["extension"]);

        if (!hasGiven && !hasFamily && !hasExtension)
        {
            // Inject data-absent-reason
            AddDataAbsentReasonExtension(nameObj);
        }
    }
}
```

### 3. Performance

**Concern**: Walking JsonNode tree on every create/update

**Mitigation**:
- **Early exit**: Check US Core active before processing
- **Cache schema metadata**: Pre-compute mandatory elements per resource type
- **Selective processing**: Only process if CardinalityCheck would fail
- **Async**: Process in pipeline without blocking

**Benchmark target**: < 5ms overhead per request

### 4. Tenant Opt-In/Opt-Out

```csharp
// Check tenant configuration
var tenantConfig = httpContext.Items["TenantConfiguration"] as TenantConfiguration;
if (tenantConfig?.EnableAutoDataAbsentReason == false)
{
    return await next(); // Tenant opted out
}
```

### 5. Error Handling

**Scenario**: Injection fails (e.g., malformed JSON)

**Strategy**: Log warning and continue to validation, which will fail with clear error

```csharp
try
{
    InjectDataAbsentReasonExtensions(...);
}
catch (Exception ex)
{
    _logger.LogWarning(ex,
        "Failed to inject data-absent-reason for {ResourceType}/{Id}",
        request.ResourceType, request.Id);
    // Continue to validation - will fail with original error
}
```

## Testing Strategy

### Unit Tests
- ✅ Behavior runs only when US Core active
- ✅ Injects extension for missing mandatory element
- ✅ Skips optional elements (min = 0)
- ✅ Handles array elements (min > 0)
- ✅ Preserves existing extensions
- ✅ Skips required binding elements (status)

### Integration Tests
- ✅ Load US Core package → Auto-enable behavior
- ✅ POST Patient without name → 201 Created (with extension)
- ✅ GET Patient → Contains data-absent-reason extension
- ✅ POST Patient to non-US-Core tenant → Validation fails (expected)
- ✅ Tenant opt-out → Validation fails (expected)

### Performance Tests
- ✅ Benchmark overhead: < 5ms per request
- ✅ Load test with 1000 req/s → No degradation

## Future Enhancements

### 1. Multi-Profile Support

Extend beyond US Core to other IGs with similar requirements:

```csharp
public interface IProfileBehaviorRegistry
{
    void RegisterBehavior(string profilePattern, IResourceMutationBehavior behavior);
}

// Register US Core behavior
registry.RegisterBehavior("hl7.fhir.us.core", new DataAbsentReasonBehavior());

// Register other IGs
registry.RegisterBehavior("hl7.fhir.au.base", new AustralianCoreBehavior());
```

### 2. Intelligent Code Selection

Instead of always using `"unknown"`, select appropriate code based on context:

- `"temp-unknown"` - If resource has recent timestamp
- `"not-asked"` - If form didn't include field
- `"masked"` - If restricted by security labels

Requires additional metadata or HTTP headers to convey reason.

### 3. Client-Provided Preference

Allow clients to opt-in/out per request via HTTP header:

```
POST /Patient
Prefer: handling=strict-us-core
```

### 4. Validation-Time Injection

Move injection into `ValidationBehavior` instead of separate behavior:

```csharp
// In ValidationBehavior, before running checks:
if (settings.Tier == ValidationTier.Profile && IsUSCoreProfile(schema))
{
    InjectDataAbsentReasonExtensions(request.JsonNode, schema);
}
```

**Trade-off**: Couples validation to mutation (less clean separation of concerns)

## Conclusion

The **recommended approach** is to implement a **Medino pipeline behavior** (`DataAbsentReasonBehavior`) that:

1. **Detects** when US Core profiles are active (via package detection)
2. **Intercepts** `CreateOrUpdateResourceCommand` requests
3. **Injects** `data-absent-reason` extensions for missing mandatory elements
4. **Runs before** `ValidationBehavior` to avoid validation failures

This approach:
- ✅ Aligns with existing architecture (Medino behaviors)
- ✅ Is tenant-aware and profile-aware
- ✅ Reduces client burden (automatic compliance)
- ✅ Maintains separation of concerns (validation doesn't mutate)
- ✅ Is testable and extensible

### Implementation Files

**New Files**:
- `src/Ignixa.Application/Infrastructure/Services/ProfileDetectionService.cs`
- `src/Ignixa.Application/Infrastructure/Behaviors/DataAbsentReasonBehavior.cs`
- `test/Ignixa.Application.Tests/Infrastructure/Behaviors/DataAbsentReasonBehaviorTests.cs`

**Modified Files**:
- `src/Ignixa.Api/Program.cs` - Register behavior and service
- `src/Ignixa.Domain/Models/TenantConfiguration.cs` - Add opt-in/opt-out flag

**Estimated Effort**: 3-5 days (2 days implementation, 1-2 days testing, 1 day integration)

## References

- [FHIR data-absent-reason Extension](http://hl7.org/fhir/StructureDefinition/data-absent-reason)
- [US Core Missing Data Guidance](https://hl7.org/fhir/us/core/general-requirements.html#missing-data)
- [US Core Patient Profile](https://hl7.org/fhir/us/core/StructureDefinition-us-core-patient.html)
- [Medino Pipeline Behaviors](https://github.com/jjrdk/medino) (Ignixa uses Medino for CQRS)
- Existing: `CapabilityEnforcementBehavior.cs:51`
- Existing: `ValidationBehavior.cs:22`
- Existing: `ResourceElementsSerializer.cs:20` (pattern reference)

# ADR 2524: CapabilityStatement SourceNode Model (No Firely SDK)

## Status

Proposed

## Context

### Problem Statement

The initial CapabilityStatement implementation (from coding-agent) violated a critical architectural constraint:

**NO FIRELY SDK DEPENDENCY** in application/business logic layers.

**Violations**:
1. ❌ Added `Hl7.Fhir.R4` package reference to `Ignixa.Application.csproj`
2. ❌ Used `Hl7.Fhir.R4.Model.CapabilityStatement` type directly
3. ❌ Created dependency on Firely SDK for business logic
4. ❌ Breaks clean architecture separation

**Why No Firely SDK?**:
- Ignixa uses **custom FHIR models** based on SourceNode pattern
- Firely SDK is heavyweight and version-specific
- We need fine-grained control over serialization and multi-version support
- Custom models proven successful: `BundleJsonNode`, `OperationOutcomeJsonNode`, `ResourceJsonNode`

### Current Architecture

**Proven SourceNode Pattern** (`Ignixa.SourceNodeSerialization.SourceNodes.Models/`):

```csharp
// Base class for all FHIR resources
public class ResourceJsonNode : IExtensionData, IResourceNode
{
    [JsonPropertyName("resourceType")]
    public string ResourceType { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("meta")]
    public MetaJsonNode Meta { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; }

    // Converts to ISourceNode for FhirPath/validation
    public ISourceNode ToSourceNode() { ... }

    // Converts to ITypedElement with schema validation
    public ITypedElement ToTypedElement(IStructureDefinitionSummaryProvider provider) { ... }
}

// Example: BundleJsonNode (complex nested types)
public class BundleJsonNode : ResourceJsonNode
{
    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BundleType? Type { get; set; }

    [JsonPropertyName("entry")]
    public IList<BundleComponentJsonNode> Entry { get; set; }

    public enum BundleType
    {
        [EnumLiteral("transaction")]
        Transaction,

        [EnumLiteral("searchset")]
        Searchset,
        // ...
    }
}

// Example: OperationOutcomeJsonNode (nested components)
public class OperationOutcomeJsonNode : ResourceJsonNode
{
    [JsonPropertyName("issue")]
    public IList<IssueComponent> Issue { get; set; }

    public class IssueComponent
    {
        [JsonPropertyName("severity")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public IssueSeverity? Severity { get; set; }

        [JsonPropertyName("code")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public IssueType? Code { get; set; }
    }
}
```

**Key Patterns**:
- ✅ Inherit from `ResourceJsonNode` (gets `ToSourceNode()` / `ToTypedElement()` support)
- ✅ Use `[JsonPropertyName]` for FHIR property names
- ✅ Use `[JsonExtensionData]` for unknown properties
- ✅ Use `[EnumLiteral]` attribute for FHIR value sets
- ✅ Use `System.Text.Json` (not Newtonsoft.Json)
- ✅ Support multi-version FHIR (version-specific models via builder)

### Microsoft FHIR Server Conformance Pattern

**Reference Implementation** (`Microsoft.Health.Fhir.Core/Features/Conformance/`):

```csharp
// Microsoft's custom POCO model (not Firely SDK)
public class ListedCapabilityStatement
{
    public string ResourceType { get; } = "CapabilityStatement";
    public Uri Url { get; set; }
    public string FhirVersion { get; set; }
    public ICollection<string> Status { get; }  // DefaultOptionHashSet
    public SoftwareComponent Software { get; set; }
    public ICollection<ListedRestComponent> Rest { get; }
    // Uses Newtonsoft.Json [JsonExtensionData] for unknown properties
}

// Version-specific serialization logic
internal class ReferenceComponentConverter : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (_modelInfoProvider.Version.Equals(FhirSpecification.Stu3))
        {
            // STU3: Serialize as object with properties
            token.WriteTo(writer);
        }
        else
        {
            // R4+: Serialize as simple string
            serializer.Serialize(writer, obj.Reference);
        }
    }
}
```

**Key Insights**:
1. Microsoft uses **custom POCOs**, not Firely SDK
2. Version-specific serialization handled via custom `JsonConverter` classes
3. Properties change between FHIR versions (e.g., `profile` in STU3 vs `supportedProfile` in R4+)
4. Uses `DefaultOptionHashSet` pattern for value sets with defaults
5. Final `Build()` method converts to `ITypedElement` using `IStructureDefinitionSummaryProvider`

## Decision

Implement CapabilityStatement using the **proven SourceNode pattern** with custom POCO models.

### Architecture

**Location**: `Ignixa.Application/Features/Metadata/Models/`

**Rationale**: Application layer is correct for business logic (building capability statements). Only HTTP concerns (controllers) belong in API layer.

### Core Models

#### 1. CapabilityStatementJsonNode

```csharp
namespace Ignixa.Application.Features.Metadata.Models;

public class CapabilityStatementJsonNode : ResourceJsonNode
{
    public CapabilityStatementJsonNode()
    {
        ResourceType = "CapabilityStatement";
    }

    [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PublicationStatus Status { get; set; }

    [JsonPropertyName("experimental")]
    public bool? Experimental { get; set; }

    [JsonPropertyName("date")]
    public string Date { get; set; }  // ISO 8601 string

    [JsonPropertyName("publisher")]
    public string Publisher { get; set; }

    [JsonPropertyName("kind")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CapabilityStatementKind Kind { get; set; }

    [JsonPropertyName("software")]
    public SoftwareComponentJsonNode Software { get; set; }

    [JsonPropertyName("fhirVersion")]
    public string FhirVersion { get; set; }

    [JsonPropertyName("format")]
    public IList<string> Format { get; set; }

    [JsonPropertyName("patchFormat")]
    public IList<string> PatchFormat { get; set; }

    [JsonPropertyName("rest")]
    public IList<RestComponentJsonNode> Rest { get; set; }

    // Enums
    public enum PublicationStatus
    {
        [EnumLiteral("draft")]
        Draft,

        [EnumLiteral("active")]
        Active,

        [EnumLiteral("retired")]
        Retired,

        [EnumLiteral("unknown")]
        Unknown
    }

    public enum CapabilityStatementKind
    {
        [EnumLiteral("instance")]
        Instance,

        [EnumLiteral("capability")]
        Capability,

        [EnumLiteral("requirements")]
        Requirements
    }
}
```

#### 2. RestComponentJsonNode

```csharp
namespace Ignixa.Application.Features.Metadata.Models;

[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class RestComponentJsonNode
{
    [JsonPropertyName("mode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RestfulCapabilityMode Mode { get; set; }

    [JsonPropertyName("documentation")]
    public string Documentation { get; set; }

    [JsonPropertyName("security")]
    public SecurityComponentJsonNode Security { get; set; }

    [JsonPropertyName("resource")]
    public IList<ResourceComponentJsonNode> Resource { get; set; }

    [JsonPropertyName("interaction")]
    public IList<SystemInteractionJsonNode> Interaction { get; set; }

    [JsonPropertyName("searchParam")]
    public IList<SearchParamJsonNode> SearchParam { get; set; }

    [JsonPropertyName("operation")]
    public IList<OperationJsonNode> Operation { get; set; }

    public enum RestfulCapabilityMode
    {
        [EnumLiteral("client")]
        Client,

        [EnumLiteral("server")]
        Server
    }
}
```

#### 3. ResourceComponentJsonNode

```csharp
namespace Ignixa.Application.Features.Metadata.Models;

[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class ResourceComponentJsonNode
{
    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("profile")]
    public string Profile { get; set; }

    [JsonPropertyName("supportedProfile")]
    public IList<string> SupportedProfile { get; set; }

    [JsonPropertyName("documentation")]
    public string Documentation { get; set; }

    [JsonPropertyName("interaction")]
    public IList<ResourceInteractionJsonNode> Interaction { get; set; }

    [JsonPropertyName("versioning")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ResourceVersionPolicy? Versioning { get; set; }

    [JsonPropertyName("readHistory")]
    public bool? ReadHistory { get; set; }

    [JsonPropertyName("updateCreate")]
    public bool? UpdateCreate { get; set; }

    [JsonPropertyName("conditionalCreate")]
    public bool? ConditionalCreate { get; set; }

    [JsonPropertyName("conditionalUpdate")]
    public bool? ConditionalUpdate { get; set; }

    [JsonPropertyName("conditionalDelete")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ConditionalDeleteStatus? ConditionalDelete { get; set; }

    [JsonPropertyName("searchInclude")]
    public IList<string> SearchInclude { get; set; }

    [JsonPropertyName("searchRevInclude")]
    public IList<string> SearchRevInclude { get; set; }

    [JsonPropertyName("searchParam")]
    public IList<SearchParamJsonNode> SearchParam { get; set; }

    public enum ResourceVersionPolicy
    {
        [EnumLiteral("no-version")]
        NoVersion,

        [EnumLiteral("versioned")]
        Versioned,

        [EnumLiteral("versioned-update")]
        VersionedUpdate
    }

    public enum ConditionalDeleteStatus
    {
        [EnumLiteral("not-supported")]
        NotSupported,

        [EnumLiteral("single")]
        Single,

        [EnumLiteral("multiple")]
        Multiple
    }
}
```

#### 4. ResourceInteractionJsonNode

```csharp
namespace Ignixa.Application.Features.Metadata.Models;

public class ResourceInteractionJsonNode
{
    [JsonPropertyName("code")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TypeRestfulInteraction Code { get; set; }

    [JsonPropertyName("documentation")]
    public string Documentation { get; set; }

    public enum TypeRestfulInteraction
    {
        [EnumLiteral("read")]
        Read,

        [EnumLiteral("vread")]
        Vread,

        [EnumLiteral("update")]
        Update,

        [EnumLiteral("patch")]
        Patch,

        [EnumLiteral("delete")]
        Delete,

        [EnumLiteral("history-instance")]
        HistoryInstance,

        [EnumLiteral("history-type")]
        HistoryType,

        [EnumLiteral("create")]
        Create,

        [EnumLiteral("search-type")]
        SearchType
    }
}
```

#### 5. SearchParamJsonNode

```csharp
namespace Ignixa.Application.Features.Metadata.Models;

public class SearchParamJsonNode
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("definition")]
    public string Definition { get; set; }

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SearchParamType Type { get; set; }

    [JsonPropertyName("documentation")]
    public string Documentation { get; set; }

    public enum SearchParamType
    {
        [EnumLiteral("number")]
        Number,

        [EnumLiteral("date")]
        Date,

        [EnumLiteral("string")]
        String,

        [EnumLiteral("token")]
        Token,

        [EnumLiteral("reference")]
        Reference,

        [EnumLiteral("composite")]
        Composite,

        [EnumLiteral("quantity")]
        Quantity,

        [EnumLiteral("uri")]
        Uri,

        [EnumLiteral("special")]
        Special
    }
}
```

#### 6. SoftwareComponentJsonNode

```csharp
namespace Ignixa.Application.Features.Metadata.Models;

public class SoftwareComponentJsonNode
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; }

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; }
}
```

### Builder Implementation

**File**: `Ignixa.Application/Features/Metadata/CapabilityStatementBuilder.cs`

```csharp
namespace Ignixa.Application.Features.Metadata;

public class CapabilityStatementBuilder
{
    private readonly ITenantConfigurationStore _tenantConfigStore;
    private readonly VersionAwareSearchParameterDefinitionManager _searchParamManager;
    private readonly ILogger<CapabilityStatementBuilder> _logger;

    public CapabilityStatementBuilder(
        ITenantConfigurationStore tenantConfigStore,
        VersionAwareSearchParameterDefinitionManager searchParamManager,
        ILogger<CapabilityStatementBuilder> logger)
    {
        _tenantConfigStore = tenantConfigStore;
        _searchParamManager = searchParamManager;
        _logger = logger;
    }

    public async Task<CapabilityStatementJsonNode> BuildAsync(
        int? tenantId,
        CancellationToken ct = default)
    {
        // Get tenant configuration for FHIR version
        TenantConfiguration tenantConfig = null;
        string fhirVersion = "4.0.1";  // Default R4

        if (tenantId.HasValue)
        {
            tenantConfig = await _tenantConfigStore.GetTenantConfigurationAsync(
                tenantId.Value,
                ct);

            if (tenantConfig != null)
            {
                fhirVersion = tenantConfig.FhirVersion;
            }
        }

        // Create base capability statement
        var capability = new CapabilityStatementJsonNode
        {
            Url = "http://sparky.example.com/fhir/CapabilityStatement",
            Version = "0.1.0",
            Name = "IgnixaFhirServer",
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Experimental = false,
            Date = DateTimeOffset.UtcNow.ToString("O"),
            Publisher = "Microsoft Corporation",
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
            FhirVersion = fhirVersion,
            Format = new List<string> { "application/fhir+json" },
            PatchFormat = new List<string> { "application/json-patch+json" },
            Software = new SoftwareComponentJsonNode
            {
                Name = "Ignixa FHIR Server",
                Version = "0.1.0",
                ReleaseDate = "2025-10-16"
            }
        };

        // Add REST component
        var restComponent = new RestComponentJsonNode
        {
            Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
            Resource = new List<ResourceComponentJsonNode>(),
            Interaction = new List<SystemInteractionJsonNode>
            {
                new SystemInteractionJsonNode
                {
                    Code = SystemInteractionJsonNode.SystemRestfulInteraction.Transaction
                },
                new SystemInteractionJsonNode
                {
                    Code = SystemInteractionJsonNode.SystemRestfulInteraction.Batch
                }
            }
        };

        // Discover resource types from search parameters
        var searchParams = _searchParamManager.GetSearchParametersForVersion(fhirVersion);
        var resourceTypes = searchParams
            .Select(sp => sp.ResourceType)
            .Distinct()
            .OrderBy(rt => rt)
            .ToList();

        foreach (var resourceType in resourceTypes)
        {
            var resourceComponent = new ResourceComponentJsonNode
            {
                Type = resourceType,
                Profile = $"http://hl7.org/fhir/StructureDefinition/{resourceType}",
                Interaction = new List<ResourceInteractionJsonNode>
                {
                    new() { Code = ResourceInteractionJsonNode.TypeRestfulInteraction.Read },
                    new() { Code = ResourceInteractionJsonNode.TypeRestfulInteraction.Create },
                    new() { Code = ResourceInteractionJsonNode.TypeRestfulInteraction.Update },
                    new() { Code = ResourceInteractionJsonNode.TypeRestfulInteraction.Delete },
                    new() { Code = ResourceInteractionJsonNode.TypeRestfulInteraction.SearchType }
                },
                SearchParam = new List<SearchParamJsonNode>()
            };

            // Add search parameters for this resource
            var resourceSearchParams = searchParams
                .Where(sp => sp.ResourceType == resourceType)
                .ToList();

            foreach (var sp in resourceSearchParams)
            {
                resourceComponent.SearchParam.Add(new SearchParamJsonNode
                {
                    Name = sp.Name,
                    Definition = sp.Url,
                    Type = MapSearchParamType(sp.Type),
                    Documentation = sp.Description
                });
            }

            restComponent.Resource.Add(resourceComponent);
        }

        capability.Rest = new List<RestComponentJsonNode> { restComponent };

        return capability;
    }

    private static SearchParamJsonNode.SearchParamType MapSearchParamType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "number" => SearchParamJsonNode.SearchParamType.Number,
            "date" => SearchParamJsonNode.SearchParamType.Date,
            "string" => SearchParamJsonNode.SearchParamType.String,
            "token" => SearchParamJsonNode.SearchParamType.Token,
            "reference" => SearchParamJsonNode.SearchParamType.Reference,
            "composite" => SearchParamJsonNode.SearchParamType.Composite,
            "quantity" => SearchParamJsonNode.SearchParamType.Quantity,
            "uri" => SearchParamJsonNode.SearchParamType.Uri,
            _ => SearchParamJsonNode.SearchParamType.Special
        };
    }
}
```

### Handler Implementation

**File**: `Ignixa.Application/Features/Metadata/GetCapabilityStatementHandler.cs`

```csharp
namespace Ignixa.Application.Features.Metadata;

public class GetCapabilityStatementHandler
    : IRequestHandler<GetCapabilityStatementQuery, CapabilityStatementJsonNode>
{
    private readonly CapabilityStatementBuilder _builder;
    private readonly ILogger<GetCapabilityStatementHandler> _logger;

    public GetCapabilityStatementHandler(
        CapabilityStatementBuilder builder,
        ILogger<GetCapabilityStatementHandler> logger)
    {
        _builder = builder;
        _logger = logger;
    }

    public async Task<CapabilityStatementJsonNode> HandleAsync(
        GetCapabilityStatementQuery query,
        CancellationToken cancellationToken)
    {
        var capability = await _builder.BuildAsync(query.TenantId, cancellationToken);

        _logger.LogDebug(
            "Built CapabilityStatement for tenant {TenantId} with FHIR version {FhirVersion}",
            query.TenantId,
            capability.FhirVersion);

        return capability;
    }
}
```

### Query Definition

**File**: `Ignixa.Application/Features/Metadata/GetCapabilityStatementQuery.cs`

```csharp
namespace Ignixa.Application.Features.Metadata;

public record GetCapabilityStatementQuery(int? TenantId)
    : IRequest<CapabilityStatementJsonNode>;
```

### Controller Integration

**File**: `Ignixa.Api/Features/Metadata/Api/MetadataController.cs`

```csharp
namespace Ignixa.Api.Features.Metadata.Api;

[ApiController]
public class MetadataController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<MetadataController> _logger;

    public MetadataController(
        IMediator mediator,
        ILogger<MetadataController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [HttpGet("metadata")]
    public async Task<IActionResult> GetCapabilityStatement()
    {
        var query = new GetCapabilityStatementQuery(TenantId: null);
        var capability = await _mediator.SendAsync(query, HttpContext.RequestAborted);

        // ASP.NET Core automatically serializes CapabilityStatementJsonNode with System.Text.Json
        return new JsonResult(capability)
        {
            ContentType = "application/fhir+json"
        };
    }

    [HttpGet("tenant/{tenantId:int}/metadata")]
    public async Task<IActionResult> GetTenantCapabilityStatement(int tenantId)
    {
        var query = new GetCapabilityStatementQuery(TenantId: tenantId);
        var capability = await _mediator.SendAsync(query, HttpContext.RequestAborted);

        return new JsonResult(capability)
        {
            ContentType = "application/fhir+json"
        };
    }
}
```

### DI Registration

**File**: `Ignixa.Api/Program.cs`

```csharp
// Register CapabilityStatementBuilder as singleton (cacheable)
containerBuilder.RegisterType<CapabilityStatementBuilder>()
    .AsSelf()
    .SingleInstance();

// Register handler
containerBuilder.RegisterType<GetCapabilityStatementHandler>()
    .AsImplementedInterfaces()
    .InstancePerDependency();
```

## Implementation Plan

### Phase 1: Create Models (4 hours)

1. Create `Ignixa.Application/Features/Metadata/Models/` directory
2. Implement 6 model files:
   - `CapabilityStatementJsonNode.cs`
   - `RestComponentJsonNode.cs`
   - `ResourceComponentJsonNode.cs`
   - `ResourceInteractionJsonNode.cs`
   - `SearchParamJsonNode.cs`
   - `SoftwareComponentJsonNode.cs`
3. Add missing supporting models (SecurityComponentJsonNode, SystemInteractionJsonNode, OperationJsonNode)
4. Verify all models compile with proper null handling

### Phase 2: Implement Builder (3 hours)

1. Update `CapabilityStatementBuilder.cs` to return `CapabilityStatementJsonNode`
2. Implement dynamic resource discovery from search parameters
3. Add multi-version support (R4, R4B, R5, STU3)
4. Add unit tests for builder logic

### Phase 3: Update Handler/Query (1 hour)

1. Update `GetCapabilityStatementHandler.cs` return type
2. Update `GetCapabilityStatementQuery.cs` IRequest type
3. Remove `ICapabilityStatementBuilder` interface (builder is concrete class)
4. Verify handler tests pass

### Phase 4: Update Controller (1 hour)

1. Update `MetadataController.cs` to use Medino query
2. Ensure proper `application/fhir+json` content type
3. Test both routes (`/metadata` and `/tenant/{id}/metadata`)

### Phase 5: Remove Firely SDK (0.5 hours)

1. Remove `<PackageReference Include="Hl7.Fhir.R4" />` from `Ignixa.Application.csproj`
2. Build solution and verify 0 errors
3. Run tests and verify all pass

### Phase 6: Integration Testing (2 hours)

1. Test `GET /metadata` - returns valid JSON
2. Test `GET /tenant/1/metadata` - tenant-specific capabilities
3. Verify multi-version support (different tenants with R4, R4B, R5)
4. Validate JSON against FHIR CapabilityStatement schema
5. Performance test (< 100ms response time)

**Total Estimated Time**: 11.5 hours

## Consequences

### Positive

1. ✅ **No Firely SDK Dependency**: Clean separation from third-party SDKs
2. ✅ **Proven Pattern**: Same approach as Bundle, OperationOutcome (reliability)
3. ✅ **Multi-Version Support**: Works with R4, R4B, R5, STU3 without code changes
4. ✅ **System.Text.Json**: Modern .NET serialization (better performance)
5. ✅ **Clean Architecture**: Application layer stays pure (no HTTP dependencies)
6. ✅ **Easy to Extend**: Add new properties without breaking changes
7. ✅ **Version-Aware**: Builder can adapt based on tenant's FHIR version
8. ✅ **Dynamic Discovery**: Resource types and search params auto-discovered
9. ✅ **ISourceNode Support**: Models inherit `ToSourceNode()` / `ToTypedElement()` capabilities
10. ✅ **Testable**: POCOs are easy to mock and test

### Negative

1. ❌ **Custom Models**: Must maintain FHIR CapabilityStatement structure ourselves
2. ❌ **More Code**: ~300-400 lines of model definitions vs using Firely SDK
3. ❌ **Version Drift Risk**: FHIR spec changes require manual model updates
4. ❌ **No Type Safety**: String-based property names vs Firely SDK strongly-typed

### Mitigation

1. **Codegen Option**: Can generate models from FHIR StructureDefinition if needed
   - Extend `codegen/Ignixa.Specification.Generators/` with `CSharpCapabilityStatementLanguage.cs`
   - Same pattern as IStructureDefinitionSummaryProvider generation
   - Still produces POCOs, not Firely SDK types

2. **Schema Validation**: Use IStructureDefinitionSummaryProvider to validate generated JSON
   - Call `capabilityNode.ToTypedElement(provider)` to verify structure
   - Catches missing required properties at runtime

3. **Unit Tests**: Comprehensive tests ensure models stay in sync with FHIR spec
   - Test JSON serialization round-trip
   - Validate against known good CapabilityStatement examples
   - Verify enum literal values match FHIR value sets

### Trade-offs

| Decision | Alternative | Rationale |
|----------|------------|-----------|
| Custom POCOs | Use Firely SDK | No third-party dependency, better control, proven pattern |
| System.Text.Json | Newtonsoft.Json | Modern .NET, better performance, already used in project |
| Builder pattern | Static JSON file | Dynamic discovery, tenant-aware, version-specific |
| Application layer | API layer | Business logic belongs in Application, HTTP concerns in API |

## Validation

### JSON Output Example (R4)

```json
{
  "resourceType": "CapabilityStatement",
  "id": "sparky-r4",
  "url": "http://sparky.example.com/fhir/CapabilityStatement",
  "version": "0.1.0",
  "name": "IgnixaFhirServer",
  "status": "active",
  "experimental": false,
  "date": "2025-10-16T12:00:00Z",
  "publisher": "Microsoft Corporation",
  "kind": "instance",
  "software": {
    "name": "Ignixa FHIR Server",
    "version": "0.1.0",
    "releaseDate": "2025-10-16"
  },
  "fhirVersion": "4.0.1",
  "format": ["application/fhir+json"],
  "patchFormat": ["application/json-patch+json"],
  "rest": [
    {
      "mode": "server",
      "resource": [
        {
          "type": "Patient",
          "profile": "http://hl7.org/fhir/StructureDefinition/Patient",
          "interaction": [
            { "code": "read" },
            { "code": "create" },
            { "code": "update" },
            { "code": "delete" },
            { "code": "search-type" }
          ],
          "searchParam": [
            {
              "name": "name",
              "definition": "http://hl7.org/fhir/SearchParameter/Patient-name",
              "type": "string",
              "documentation": "A server defined search that may match any of the string fields in the HumanName"
            },
            {
              "name": "birthdate",
              "definition": "http://hl7.org/fhir/SearchParameter/Patient-birthdate",
              "type": "date"
            }
          ]
        }
      ],
      "interaction": [
        { "code": "transaction" },
        { "code": "batch" }
      ]
    }
  ]
}
```

### Testing Checklist

- [ ] ✅ All models serialize to valid JSON
- [ ] ✅ Enum literals match FHIR value sets
- [ ] ✅ GET /metadata returns 200 OK
- [ ] ✅ GET /tenant/1/metadata returns 200 OK
- [ ] ✅ JSON validates against FHIR CapabilityStatement schema
- [ ] ✅ Multi-version support works (R4, R4B, R5)
- [ ] ✅ No Firely SDK references in `Ignixa.Application.csproj`
- [ ] ✅ Build succeeds with 0 warnings, 0 errors
- [ ] ✅ All tests pass
- [ ] ✅ Response time < 100ms (P95)

## References

- **Microsoft FHIR Server**: `ThirdParty/Microsoft.Health.Fhir.Core/Features/Conformance/`
- **SourceNode Models**: `Ignixa.SourceNodeSerialization/SourceNodes/Models/`
- **FHIR R4 CapabilityStatement**: http://hl7.org/fhir/R4/capabilitystatement.html
- **FHIR R5 CapabilityStatement**: http://hl7.org/fhir/R5/capabilitystatement.html
- **ADR-2503**: Phase 1.2 - Search Implementation (mentions metadata endpoint)
- **Investigation**: `docs/investigations/dynamic-capability-statement-generation.md`

## Next Steps

1. ✅ Review this ADR with team
2. ⏳ Create model files in `Ignixa.Application/Features/Metadata/Models/`
3. ⏳ Update `CapabilityStatementBuilder.cs` to return `CapabilityStatementJsonNode`
4. ⏳ Update handler and query to use new return type
5. ⏳ Remove `Hl7.Fhir.R4` from `Ignixa.Application.csproj`
6. ⏳ Test both `/metadata` and `/tenant/{id}/metadata` endpoints
7. ⏳ Validate JSON output against FHIR schema
8. ⏳ Performance testing and optimization

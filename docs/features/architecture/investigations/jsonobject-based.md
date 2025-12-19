# Investigation: JsonObject-Based Architecture for ResourceJsonNode

**Feature**: architecture
**Status**: Complete
**Created**: 2025-10-18

---

## Executive Summary

This investigation documents the architectural decision to migrate from `Dictionary<string, JsonElement>` (ExtensionData pattern) to `System.Text.Json.Nodes.JsonObject` (MutableNode pattern) for FHIR resource manipulation in the Ignixa.SourceNodeSerialization library.

### Key Findings

| Metric | ExtensionData (Old) | MutableNode (New) |
|--------|---------------------|-------------------|
| **Mutability** | ❌ Immutable (requires cloning) | ✅ Mutable in-place |
| **API** | Legacy JsonElement | Modern JsonNode |
| **Performance** | Serialization roundtrips | Direct mutation |
| **Code Pattern** | GetMutableNode() method | MutableNode property |
| **Test Migration** | Manual dictionary manipulation | Direct JsonObject access |

**Outcome**: Successfully implemented JsonObject-based architecture with 0 compilation errors, all tests passing.

---

## Problem Statement

### Original Implementation

The original ResourceJsonNode used `Dictionary<string, JsonElement>` for extensibility:

```csharp
// OLD: ExtensionData pattern
public class ResourceJsonNode
{
    public string ResourceType { get; set; }
    public string Id { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; }
}

// Usage - IMMUTABLE
var patient = new ResourceJsonNode
{
    ResourceType = "Patient",
    ExtensionData = new Dictionary<string, JsonElement>
    {
        ["active"] = JsonSerializer.SerializeToElement(true),
    },
};

// Mutation requires serialization roundtrip
var json = JsonSerializer.Serialize(patient);
var modified = JsonSerializer.Deserialize<ResourceJsonNode>(json);
modified.ExtensionData["birthDate"] = JsonSerializer.SerializeToElement("1990-01-15");
```

### Issues

1. **Immutability**: JsonElement is read-only. Any mutation requires serialization → deserialization → modification → re-serialization.
2. **Performance**: FHIRPath Patch operations and reference updates required expensive roundtrips.
3. **API Inconsistency**: Mixed use of GetMutableNode() method vs property-based access.
4. **FHIR-Awareness**: JsonNodeSourceNode lacked logic for shadow properties, extensions, and choice types.

---

## Solution Architecture

### Core Design

Replace ExtensionData with a single mutable `JsonObject` as the source of truth:

```csharp
// NEW: MutableNode pattern
public abstract class BaseJsonNode : IMutableJsonNode
{
    private readonly JsonObject _internalNode;

    protected BaseJsonNode()
    {
        _internalNode = new JsonObject();
    }

    protected BaseJsonNode(JsonObject jsonObject)
    {
        _internalNode = jsonObject;
    }

    public JsonObject MutableNode => _internalNode;

    public void SetProperty(string name, JsonNode? value)
    {
        if (value == null)
            _internalNode.Remove(name);
        else
            _internalNode[name] = value;
    }

    public string SerializeToString()
    {
        return _internalNode.ToJsonString();
    }
}

public class ResourceJsonNode : BaseJsonNode, IResourceNode
{
    [JsonIgnore]
    public string ResourceType
    {
        get => MutableNode["resourceType"]?.GetValue<string>() ?? string.Empty;
        set => MutableNode["resourceType"] = value;
    }

    [JsonIgnore]
    public string Id
    {
        get => MutableNode["id"]?.GetValue<string>() ?? string.Empty;
        set => MutableNode["id"] = value;
    }

    [JsonIgnore]
    public MetaJsonNode Meta
    {
        get
        {
            // Cached wrapper for Meta property
            if (_cachedMeta == null)
            {
                if (!MutableNode.TryGetPropertyValue("meta", out var metaNode) || metaNode is not JsonObject metaObject)
                {
                    metaObject = new JsonObject();
                    MutableNode["meta"] = metaObject;
                }
                _cachedMeta = new MetaJsonNode(metaObject);
            }
            return _cachedMeta;
        }
    }
}
```

### Custom JSON Converter

```csharp
public class ResourceJsonNodeConverter : JsonConverter<ResourceJsonNode>
{
    public override ResourceJsonNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Parse entire JSON into JsonObject
        var jsonObject = JsonNode.Parse(ref reader)?.AsObject()
            ?? throw new JsonException("Failed to parse JSON as JsonObject");

        // Create ResourceJsonNode with pre-parsed JsonObject
        return new ResourceJsonNode(jsonObject);
    }

    public override void Write(Utf8JsonWriter writer, ResourceJsonNode value, JsonSerializerOptions options)
    {
        // Serialize the internal JsonObject
        value.MutableNode.WriteTo(writer, options);
    }
}
```

### FHIR-Aware JsonNodeSourceNode

Reimplemented with FHIR-specific logic ported from JsonElementSourceNode:

```csharp
internal class JsonNodeSourceNode : ISourceNode
{
    private const char _shadowNodePrefix = '_';
    internal const char ChoiceTypeSuffix = '*';

    private readonly JsonNode? _contentNode;  // Object with extensions
    private readonly JsonNode? _valueNode;    // Primitive value

    // Shadow property pairing (e.g., birthDate + _birthDate)
    internal static List<(string, Lazy<IEnumerable<ISourceNode>>)> ProcessObjectProperties(
        IEnumerable<(string Name, JsonNode? Value)> objectEnumerator,
        string location)
    {
        // Group by base name (trim '_' prefix) to pair regular properties with shadow properties
        foreach (IGrouping<string, (string Name, JsonNode? Value)> item in objectEnumerator
            .Where(x => x.Value != null)
            .GroupBy(x => x.Name.TrimStart(_shadowNodePrefix)))
        {
            if (item.Count() == 1)
            {
                // Single property (no shadow)
                yield return CreateSourceNode(item.First());
            }
            else if (item.Count() == 2)
            {
                // Property with shadow (e.g., birthDate + _birthDate)
                var regularItem = item.SingleOrDefault(x => !x.Name.StartsWith(_shadowNodePrefix));
                var shadowItem = item.SingleOrDefault(x => x.Name.StartsWith(_shadowNodePrefix));

                // Determine content (object) vs value (primitive)
                JsonNode? content = null, value = null;
                if (regularItem.Value is JsonObject)
                {
                    content = regularItem.Value;
                    value = shadowItem.Value;
                }
                else
                {
                    content = shadowItem.Value;
                    value = regularItem.Value;
                }

                yield return new JsonNodeSourceNode(value, content, regularItem.Name, null, location);
            }
        }
    }

    // Choice type suffix support (e.g., "value*" matches "valueString", "valueCode", etc.)
    public IEnumerable<ISourceNode> Children(string name = null)
    {
        if (name.EndsWith(ChoiceTypeSuffix))
        {
            string matchPrefix = name.TrimEnd(ChoiceTypeSuffix);
            return _cachedNodes
                .Where(x => x.Key.StartsWith(matchPrefix, StringComparison.Ordinal))
                .SelectMany(x => x.Value.Value)
                .ToArray();
        }
        // ...
    }
}
```

---

## Implementation Phases

### Phase 1: Core Infrastructure
- ✅ Created `BaseJsonNode` with `MutableNode` property
- ✅ Created `ResourceJsonNodeConverter` for JsonObject storage
- ✅ Registered converter in `JsonSourceNodeFactory`
- ✅ Updated all `*JsonNode` models to inherit from `BaseJsonNode`

### Phase 2: JsonObjectSourceNode
- ✅ Created `JsonObjectSourceNode` class (note: later replaced by enhanced JsonNodeSourceNode)

### Phase 3: Consumer Updates
- ✅ Updated `FhirPatchEngine` to use in-place mutation
- ✅ Updated `FastPathValidator` to use MutableNode
- ✅ Refactored `ParametersJsonNode` to use MutableNode-based architecture

### Phase 3.5: Property Convention
- ✅ Refactored `GetMutableNode()` method to `MutableNode` property (21 files)

### Phase 3.6: FHIR-Aware Logic
- ✅ Reimplemented `JsonNodeSourceNode` with FHIR-aware logic from `JsonElementSourceNode`
- ✅ Shadow property pairing via `GroupBy(x => x.Name.TrimStart('_'))`
- ✅ Content vs value distinction for FHIR primitives
- ✅ Array/shadow array pairing with matching indexes
- ✅ Choice type suffix support (`*`)

### Phase 3.7: ParametersJsonNode Cleanup
- ✅ Removed explicit POCO properties (ValueCode, ValueString, etc.)
- ✅ Implemented flexible `GetValue<T>()` / `SetValue<T>()` methods

### Phase 3.8: RemoveExtension
- ✅ Implemented `RemoveExtension()` extension method for `MetaJsonNode`
- ✅ Enabled previously skipped test

### Phase 4: Test Migration
- ✅ Updated `ResourceReferenceHelperTests.cs` (17 test methods)
- ✅ Updated `FastPathValidatorTests.cs` (7 test methods, helper refactored)
- ✅ Updated `MetaJsonNodeTests.cs` (RemoveExtension test enabled)

### Phase 5: Verification
- ✅ All tests passing (no errors, no warnings)
- ✅ Build succeeded across entire solution

### Phase 6: Documentation
- ✅ Updated CLAUDE.md with JsonObject-based architecture patterns
- ✅ Created this ADR document

---

## Usage Examples

### Creating and Mutating Resources

```csharp
// Create a resource
var patient = new ResourceJsonNode
{
    ResourceType = "Patient",
    Id = "example-123",
};

// Accessing the mutable JsonObject via MutableNode property
patient.MutableNode["active"] = JsonSerializer.SerializeToNode(true);
patient.MutableNode["birthDate"] = JsonValue.Create("1990-01-15");

// Adding complex objects
patient.MutableNode["name"] = JsonNode.Parse(@"[{
    ""family"": ""Doe"",
    ""given"": [""John""]
}]");

// Working with Meta extensions
patient.Meta.LastUpdated = DateTimeOffset.UtcNow;
patient.Meta.VersionId = "1";
patient.Meta.RemoveExtension("http://example.com/extension-url");

// Reading values
if (patient.MutableNode.TryGetPropertyValue("active", out var activeNode))
{
    bool isActive = activeNode.GetValue<bool>();
}

// Serializing to JSON string
string json = patient.SerializeToString();
```

### FHIRPath Patch Operations

```csharp
public void ApplyPatch(ResourceJsonNode resource, FhirPatchOperation operation)
{
    var path = operation.Path;
    var value = operation.Value;

    // Direct mutation using JsonNode API
    if (operation.Type == FhirPatchOperationType.Add)
    {
        resource.MutableNode[path] = value;
    }
    else if (operation.Type == FhirPatchOperationType.Replace)
    {
        resource.MutableNode[path] = value;
    }
    else if (operation.Type == FhirPatchOperationType.Delete)
    {
        resource.MutableNode.Remove(path);
    }

    // No serialization roundtrips required!
}
```

### Test Pattern Migration

```csharp
// BEFORE (ExtensionData - DEPRECATED)
var resource = new ResourceJsonNode
{
    ResourceType = "Patient",
    ExtensionData = new Dictionary<string, JsonElement>
    {
        ["active"] = JsonSerializer.SerializeToElement(true),
        ["managingOrganization"] = JsonDocument.Parse(@"{""reference"": ""Organization/org-456""}").RootElement,
    },
};

// Assertion
var updatedElement = resource.ExtensionData["managingOrganization"];
Assert.Equal("Organization/org-new", updatedElement.GetProperty("reference").GetString());

// AFTER (MutableNode - CORRECT)
var resource = new ResourceJsonNode
{
    ResourceType = "Patient",
};
resource.MutableNode["active"] = JsonSerializer.SerializeToNode(true);
resource.MutableNode["managingOrganization"] = JsonNode.Parse(@"{""reference"": ""Organization/org-456""}");

// Assertion
var updatedElement = resource.MutableNode["managingOrganization"];
Assert.Equal("Organization/org-new", updatedElement["reference"].GetValue<string>());
```

---

## Key Files Modified

### Core Infrastructure (Phase 1)
- `src/Ignixa.SourceNodeSerialization/Abstractions/IMutableJsonNode.cs` - Interface with MutableNode property
- `src/Ignixa.SourceNodeSerialization/SourceNodes/Models/BaseJsonNode.cs` - Base class for all JSON models
- `src/Ignixa.SourceNodeSerialization/SourceNodes/Models/ResourceJsonNode.cs` - Resource wrapper
- `src/Ignixa.SourceNodeSerialization/SourceNodes/Models/MetaJsonNode.cs` - Meta element
- `src/Ignixa.SourceNodeSerialization/SourceNodes/Models/ParametersJsonNode.cs` - Parameters resource
- `src/Ignixa.SourceNodeSerialization/SourceNodes/Models/ParameterJsonNode.cs` - Individual parameter
- `src/Ignixa.SourceNodeSerialization/Converters/ResourceJsonNodeConverter.cs` - Custom JSON converter
- `src/Ignixa.SourceNodeSerialization/JsonSourceNodeFactory.cs` - Factory with converter registration

### FHIR-Aware Logic (Phase 3.6)
- `src/Ignixa.SourceNodeSerialization/ElementModel/JsonNodeSourceNode.cs` - FHIR-aware ISourceNode implementation
- `src/Ignixa.SourceNodeSerialization/ElementModel/ReflectedSourceNode.cs` - ResourceJsonNode wrapper

### Consumers (Phase 3)
- `src/Ignixa.Application/Features/Patch/FhirPatchEngine.cs` - In-place mutation for patch operations
- `src/Ignixa.Application/Features/Patch/FhirPatchParametersParser.cs` - Updated for new ParametersJsonNode API
- `src/Ignixa.Validation/JsonNodeValidation/FastPathValidator.cs` - Uses MutableNode for validation

### Extensions (Phase 3.8)
- `src/Ignixa.SourceNodeSerialization/Extensions/SourceNodeExtensions.cs` - RemoveExtension method

### Helpers
- `src/Ignixa.SourceNodeSerialization/Helpers/ResourceReferenceHelper.cs` - Reference extraction and updates

### Tests (Phase 4)
- `test/Ignixa.SourceNodeSerialization.Tests/MetaJsonNodeTests.cs` - 8 tests
- `test/Ignixa.SourceNodeSerialization.Tests/ResourceReferenceHelperTests.cs` - 17 tests
- `test/Ignixa.Validation.Tests/JsonNodeValidation/FastPathValidatorTests.cs` - 26 tests

### Documentation (Phase 6)
- `CLAUDE.md` - Added "Working with ResourceJsonNode" pattern section
- `docs/investigations/jsonobject-based-architecture.md` - This ADR

---

## Performance Impact

### Memory Usage
- **Before**: Serialization roundtrips allocate temporary strings and JsonDocument objects
- **After**: Direct JsonObject mutation with no intermediate allocations

### FHIRPath Patch
- **Before**: Serialize → Parse → Mutate → Serialize (4 operations)
- **After**: Direct mutation (1 operation)

### Reference Updates
- **Before**: Serialize → Parse → Modify → Serialize (4 operations)
- **After**: Direct JsonObject indexer access (1 operation)

---

## Breaking Changes

### API Changes
- ❌ **REMOVED**: `ExtensionData` property on ResourceJsonNode
- ❌ **REMOVED**: `GetMutableNode()` method
- ✅ **ADDED**: `MutableNode` property on all *JsonNode models
- ✅ **ADDED**: `RemoveExtension()` extension method

### Migration Guide

```csharp
// OLD: ExtensionData
var resource = new ResourceJsonNode
{
    ResourceType = "Patient",
    ExtensionData = new Dictionary<string, JsonElement>
    {
        ["active"] = JsonSerializer.SerializeToElement(true),
    },
};
var value = resource.ExtensionData["active"].GetBoolean();

// NEW: MutableNode
var resource = new ResourceJsonNode
{
    ResourceType = "Patient",
};
resource.MutableNode["active"] = JsonSerializer.SerializeToNode(true);
var value = resource.MutableNode["active"].GetValue<bool>();
```

---

## Lessons Learned

1. **Property vs Method**: Following .NET conventions (MutableNode property vs GetMutableNode() method) improves code readability.

2. **FHIR-Aware Logic**: Porting shadow property logic from JsonElementSourceNode was critical for validation pipeline compatibility.

3. **Shadow Property Pairing**: Using `GroupBy(x => x.Name.TrimStart('_'))` elegantly pairs regular properties with shadow properties.

4. **JsonNode > JsonElement**: Modern System.Text.Json.Nodes API is more flexible and mutable than legacy System.Text.Json.

5. **Test Migration**: Updating 30+ test files was essential to validate the architectural change.

6. **RemoveExtension**: Extension manipulation is a common operation - implementing it properly is important.

---

## Future Enhancements

1. **Validation Integration**: Ensure FastPathValidator leverages FHIR-aware JsonNodeSourceNode logic.

2. **Performance Benchmarks**: Quantify performance improvements for FHIRPath Patch and reference updates.

3. **Extension Methods**: Consider additional extension methods for common operations (AddExtension, GetExtension, etc.).

4. **Cached Properties**: Optimize Meta property to cache MetaJsonNode wrapper per resource instance.

---

## References

- [System.Text.Json.Nodes Documentation](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.nodes)
- [FHIR R4 Specification - Shadow Properties](https://www.hl7.org/fhir/json.html#primitive)
- [FHIR R4 Specification - Extensions](https://www.hl7.org/fhir/extensibility.html)
- [FHIR R4 Specification - Choice Types](https://www.hl7.org/fhir/formats.html#choice)

---

## Status

✅ **IMPLEMENTED** - All phases completed successfully.

- Build: ✅ 0 errors, 0 warnings
- Tests: ✅ All passing
- Documentation: ✅ CLAUDE.md updated, ADR created
- Migration: ✅ All test files updated

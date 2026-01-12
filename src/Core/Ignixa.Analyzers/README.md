# Ignixa.Analyzers

Roslyn analyzers to prevent common misuse patterns across the Ignixa codebase.

## Analyzers

### IGNIXA001: Do not use JsonSerializer.Deserialize with ResourceJsonNode

**Severity**: Error

`ResourceJsonNode` and its derived types require special initialization that sets up internal navigation structures for FHIRPath evaluation. Using `JsonSerializer.Deserialize<ResourceJsonNode>()` creates an uninitialized object with no children, causing FHIRPath queries to return empty results.

**Incorrect:**
```csharp
var json = "{ \"resourceType\": \"Patient\", ... }";
var patient = JsonSerializer.Deserialize<ResourceJsonNode>(json); // ❌ Error IGNIXA001
```

**Correct:**
```csharp
var json = "{ \"resourceType\": \"Patient\", ... }";
var patient = ResourceJsonNode.Parse(json); // ✅ Correct
```

**Code Fix**: The analyzer provides an automatic code fix that replaces `JsonSerializer.Deserialize<T>()` with `T.Parse()`.

---

### IGNIXA002: Do not use JsonSerializer.Serialize with ResourceJsonNode

**Severity**: Warning

`ResourceJsonNode` types are already JSON-backed (internally using `JsonObject`) and don't require `JsonSerializer.Serialize()`. Serializing them through `JsonSerializer` is redundant and may not produce the expected output.

**Incorrect:**
```csharp
var patient = ResourceJsonNode.Parse(json);
var output = JsonSerializer.Serialize(patient); // ⚠ Warning IGNIXA002
```

**Correct:**
```csharp
var patient = ResourceJsonNode.Parse(json);

// Option 1: Use ToJson() method (if available)
var output = patient.ToJson();

// Option 2: Use ToString()
var output = patient.ToString();

// Option 3: Access the underlying JsonObject
var output = patient.MutableNode.ToJsonString();
```

## Installation

The analyzer is automatically included when you reference `Ignixa.Serialization` (or any other Ignixa library that includes it):

```xml
<PackageReference Include="Ignixa.Serialization" Version="..." />
```

No additional configuration required - the analyzer runs automatically during compilation.

For standalone use:

```xml
<PackageReference Include="Ignixa.Analyzers" Version="..." />
```

## Testing

> **Note**: The analyzer test project (`test/Ignixa.Analyzers.Tests/`) is currently excluded from the solution due to incompatibilities between Microsoft.CodeAnalysis.Testing packages and Roslyn 4.8. The analyzer has been manually verified and catches real issues in the codebase.

The analyzer has been validated by:
1. Successfully catching `JsonSerializer.Deserialize<ResourceJsonNode>()` in `ResourceConverter.cs`
2. Providing correct error messages and suggested fixes
3. Building and packaging correctly

## Why These Patterns Are Problematic

### The Deserialization Issue

When you use `JsonSerializer.Deserialize<ResourceJsonNode>()`, the JSON deserializer:
1. Creates a new `ResourceJsonNode` instance
2. Populates its properties via property setters
3. Does NOT call the `Parse()` method or initialize internal navigation structures

This results in an object that:
- Has no children accessible via `.Children()`
- Returns empty results for all FHIRPath queries
- Cannot be used with FHIRPath-based validation or search indexing

**Real-world impact**: This exact issue caused a performance test to fail because FHIRPath evaluation returned 0 results instead of the expected data, making it appear that Ignixa wasn't working when it was just incorrectly initialized.

### The Serialization Issue

`ResourceJsonNode` already wraps a `JsonObject` internally. Calling `JsonSerializer.Serialize()` on it may:
- Serialize the wrapper object instead of the underlying JSON
- Include internal state that shouldn't be serialized
- Miss custom serialization logic in the type

The correct approach is to use the type's own serialization methods or access the underlying `JsonObject` directly.

## See Also

- [Ignixa Documentation](https://brendankowitz.github.io/ignixa-fhir/docs/serialization)
- [Roslyn Analyzer Documentation](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/)

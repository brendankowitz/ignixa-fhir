---
sidebar_position: 2
title: Abstractions
description: Core interfaces and types for FHIR data
---

# Ignixa.Abstractions

The Abstractions package provides the foundational interfaces and types used throughout the Ignixa ecosystem.

## Installation

```bash
dotnet add package Ignixa.Abstractions
```

## Core Interfaces

### ISourceNavigator

The primary abstraction for navigating FHIR data:

```csharp
public interface ISourceNavigator
{
    /// <summary>
    /// Name of the element (property name in JSON)
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Primitive value as string, if any
    /// </summary>
    string Text { get; }

    /// <summary>
    /// Location of this node within the tree of data
    /// </summary>
    string Location { get; }

    /// <summary>
    /// Resource type for resources, null otherwise
    /// </summary>
    string ResourceType { get; }

    /// <summary>
    /// Navigate to child elements
    /// </summary>
    IEnumerable<ISourceNavigator> Children(string? name = null);

    /// <summary>
    /// Retrieve attached metadata (e.g., source JsonNode)
    /// </summary>
    T? Meta<T>() where T : class;
}
```

### IElement

Typed element interface for FHIRPath evaluation and validation:

```csharp
public interface IElement
{
    /// <summary>
    /// Element name (e.g., "name", "birthDate", "valueQuantity")
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Primitive value for primitive types, null for complex types.
    /// boolean/integer/decimal map to bool/int/decimal; date/dateTime/instant/time map to
    /// FhirTemporal (falling back to the wire string if unparseable); every other primitive
    /// is its FHIR wire-format string.
    /// </summary>
    object? Value { get; }

    /// <summary>
    /// Runtime type name (e.g., "HumanName", "string", "Patient")
    /// </summary>
    string InstanceType { get; }

    /// <summary>
    /// Dotted location for error reporting (e.g., "Patient.name[0].family")
    /// </summary>
    string Location { get; }

    /// <summary>
    /// Type metadata from StructureDefinition
    /// </summary>
    IType? Type { get; }

    /// <summary>
    /// Child elements (supports choice element semantics)
    /// </summary>
    IReadOnlyList<IElement> Children(string? name = null);

    /// <summary>
    /// Retrieve attached metadata (e.g., source JsonNode)
    /// </summary>
    T? Meta<T>() where T : class;
}
```

:::note ISourceNavigator vs IElement
`ISourceNavigator` is for raw JSON navigation (parsing). `IElement` is for typed operations (FHIRPath, validation). Convert with `sourceNavigator.ToElement(schema)`.
:::

#### The `Value` contract is a union

`IElement.Value` is `object?`, and callers need to handle more than one shape:

- `bool`, `int`, `decimal` for `boolean`, `integer`/`unsignedInt`/`positiveInt`, and `decimal`.
- [`FhirTemporal`](#fhirtemporal) for `date`, `dateTime`, `instant`, and `time` — the parsed value and
  the original wire literal together, not a bare `string`. If the literal fails to parse, the value
  falls back to the raw wire `string` instead of dropping the element.
- The FHIR wire-format `string` for every other primitive (including `integer64` and `base64Binary`,
  which are not yet promoted to `long`/`byte[]`).

Third-party `IElement` implementations may also hand back a bare `DateTimeOffset` or `DateTime` for a
temporal instead of `FhirTemporal`. Code that needs to work across implementations should not assume
`FhirTemporal` is the only typed shape a temporal can arrive in.

Calling `.ToString()` on any of these — including `FhirTemporal` — returns the value's wire literal
verbatim, not a culture-formatted or re-rendered string. For a temporal specifically, `FhirTemporal.ToString()`
always returns `FhirTemporal.Literal`, so a partial-precision value like `"1974"` round-trips exactly rather
than being expanded into a full timestamp.

### IType

Marker interface for typed FHIR elements:

```csharp
public interface IType
{
    string TypeName { get; }
}
```

## Navigation Patterns

### Child Navigation

```csharp
// Get all children
foreach (var child in sourceNavigator.Children())
{
    Console.WriteLine($"{child.Name}: {child.Text}");
}

// Get specific child by name
var name = sourceNavigator.Children("name").FirstOrDefault();

// Indexer shorthand
var family = sourceNavigator["name"][0]["family"];
```

### Deep Navigation

```csharp
// Navigate multiple levels
var givenName = sourceNavigator["name"][0]["given"][0].Text;

// Handle missing paths safely
var telecom = sourceNavigator["telecom"]?.FirstOrDefault();
```

### Resource Type Detection

```csharp
var resourceType = sourceNavigator.ResourceType;

switch (resourceType)
{
    case "Patient":
        ProcessPatient(sourceNavigator);
        break;
    case "Observation":
        ProcessObservation(sourceNavigator);
        break;
}
```

## Extension Methods

### ToElement

Convert `ISourceNavigator` to `IElement` for typed operations (requires `Ignixa.Serialization`):

```csharp
using Ignixa.Serialization.SourceNodes;

// Convert to typed element for FHIRPath and validation
var element = sourceNavigator.ToElement(schemaProvider);

// Now you can use FHIRPath
var names = element.Select("name.given");
```

### Working with Primitive Values

Get values directly from `ISourceNavigator.Text` or use FHIRPath type conversions:

```csharp
// Direct text access
var birthDateText = sourceNavigator.Children("birthDate").FirstOrDefault()?.Text;

// Or use FHIRPath for type conversion
var element = sourceNavigator.ToElement(schemaProvider);
var birthDate = element.Select("birthDate.toDateTime()").FirstOrDefault();
```

## Value Objects

### FhirTemporal

The typed value `IElement.Value` returns for `date`, `dateTime`, `instant`, and `time` primitives.
Carries the wire literal and the parsed precision together, so it is typed without losing partial-precision
fidelity (`"1974"` is not forced into a full `DateTimeOffset`):

```csharp
public sealed class FhirTemporal : IEquatable<FhirTemporal>, IComparable<FhirTemporal>
{
    public string Literal { get; }              // Wire text verbatim, "@" sigil stripped
    public FhirTemporalPrecision Precision { get; }
    public FhirPrimitive Kind { get; }           // Date, DateTime, Instant, or Time
    public DateTimeOffset? Value { get; }        // null at Year/Month precision, and for Time
    public bool HasTimezone { get; }

    public static bool TryParse(string? literal, FhirPrimitive kind, out FhirTemporal? result);
    public static int? Compare(FhirTemporal? left, FhirTemporal? right); // FHIRPath tri-state ordering
}
```

`Value` is `null` whenever materializing a `DateTimeOffset` would fabricate data the source didn't
supply — year/month precision, and every `time` (a time of day is not a point on the calendar). Use
`Literal` for the source text and `Precision` to know how much of `Value` (when non-null) to trust.

`Compare` returns `null` for an indeterminate FHIRPath ordering (e.g. `@2012 > @2012-01`, or comparing
a timezone-bearing value against a timezone-less one) rather than an arbitrary `true`/`false` — use it
instead of `CompareTo`, which is a total order for collections and does not carry FHIRPath semantics.

### ResourceKey

Identifies a FHIR resource by type, ID, and optional version/tenant (`Ignixa.Abstractions`):

```csharp
public record ResourceKey(
    string ResourceType,
    string Id,
    string? VersionId = null,
    int? TenantId = null)
{
    public override string ToString(); // "Patient/123", "Patient/123/_history/2", or "1/Patient/123" (tenant-scoped)
}
```

### ResourceReference

Represents a FHIR reference found while walking a resource — the element it was found at, the raw
reference value, and (when parseable) the resource type/ID it points to. Declared in namespace
`Ignixa.Serialization.Models` despite living in the Abstractions project:

```csharp
public sealed class ResourceReference
{
    public required string ElementPath { get; init; }         // e.g. "subject", "generalPractitioner"
    public required string Value { get; init; }                // e.g. "Patient/123", "urn:uuid:..."
    public required IReadOnlyList<string> TargetResourceTypes { get; init; } // empty = any type allowed
    public bool IsCollection { get; init; }
    public ReferenceType Type { get; init; }                   // Relative, Absolute, or Logical
    public string? ResourceType { get; init; }                 // null for logical/absolute references
    public string? ResourceId { get; init; }                   // null if unparseable
}
```

### CodeableConcept Handling

```csharp
var coding = sourceNavigator["code"]["coding"][0];
var system = coding["system"].Text;
var code = coding["code"].Text;
var display = coding["display"].Text;
```

## Custom ISourceNavigator Implementation

Implement `ISourceNavigator` for custom data sources:

```csharp
public class MySourceNavigator : ISourceNavigator
{
    private readonly JsonElement _element;

    public string Name { get; }

    public string Text => _element.ValueKind == JsonValueKind.String
        ? _element.GetString()
        : string.Empty;

    public string Location { get; }

    public string ResourceType =>
        _element.TryGetProperty("resourceType", out var rt)
            ? rt.GetString() ?? string.Empty
            : string.Empty;

    public IEnumerable<ISourceNavigator> Children(string? name = null)
    {
        if (_element.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var prop in _element.EnumerateObject())
        {
            if (name is null || prop.Name == name)
                yield return new MySourceNavigator(prop.Name, prop.Value);
        }
    }

    public T? Meta<T>() where T : class
    {
        // Return attached metadata if any
        return null;
    }
}
```

## Best Practices

### 1. Prefer FHIRPath and ISourceNavigator over Direct JSON Access

FHIRPath and `ISourceNavigator` understand FHIR's business logic:
- **Choice types**: `value[x]` elements correctly resolve to `valueQuantity`, `valueString`, etc.
- **Extensions**: Navigate through shadow properties and extensions
- **Polymorphism**: Handles contained resources and references properly

```csharp
// ✅ Good: FHIRPath with full FHIR semantics
var display = element.Select("code.coding.first().display").FirstOrDefault();

// ✅ Also Good: ISourceNavigator understands FHIR structure
var display = sourceNavigator["code"]["coding"][0]["display"].Text;

// ❌ Avoid: Direct MutableNode access bypasses FHIR semantics
var node = resourceJsonNode.MutableNode;
var display = node["code"]?["coding"]?[0]?["display"]?.GetValue<string>();
```

:::warning Direct JSON Access
Accessing `JsonSourceNode.MutableNode` or raw `System.Text.Json.Nodes.JsonNode` directly bypasses FHIR-specific handling. Use `ISourceNavigator` or FHIRPath for correct FHIR semantics.
:::

### 2. Use Type Information

```csharp
var element = sourceNavigator.ToElement(schemaProvider);

// Now you have type information
var instanceType = element.InstanceType;  // e.g., "Patient", "HumanName"
var typeInfo = element.Type;              // Type metadata from StructureDefinition
```

### 3. Handle Missing Data Gracefully

```csharp
// Use null-conditional operators
var birthDate = sourceNavigator["birthDate"]?.Text;

// Or provide defaults
var active = sourceNavigator["active"]?.Text ?? "true";
```

## Related Documentation

- [Serialization](/docs/core-sdk/serialization)
- [FHIRPath](/docs/core-sdk/fhirpath)

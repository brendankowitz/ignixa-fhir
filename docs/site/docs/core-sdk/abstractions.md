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
    /// Primitive value (typed: bool, int, decimal, string, DateTimeOffset)
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

### ResourceIdentifier

```csharp
public record ResourceIdentifier(string ResourceType, string Id)
{
    public static ResourceIdentifier Parse(string reference);
    public string ToReference(); // "Patient/123"
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

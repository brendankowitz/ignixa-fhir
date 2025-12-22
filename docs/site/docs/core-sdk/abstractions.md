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

### ISourceNode

The primary abstraction for navigating FHIR data:

```csharp
public interface ISourceNode
{
    /// <summary>
    /// Name of the element (property name in JSON)
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Primitive value as string, if any
    /// </summary>
    string? Text { get; }

    /// <summary>
    /// Resource type for resources, null otherwise
    /// </summary>
    string? ResourceType { get; }

    /// <summary>
    /// Navigate to child elements
    /// </summary>
    IEnumerable<ISourceNode> Children(string? name = null);
}
```

### IElement

Extended interface with type information:

```csharp
public interface IElement : ISourceNode
{
    /// <summary>
    /// FHIR type name (e.g., "string", "dateTime", "Patient")
    /// </summary>
    string InstanceType { get; }

    /// <summary>
    /// Schema information for this element
    /// </summary>
    IElementDefinitionSummary? Definition { get; }
}
```

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
foreach (var child in sourceNode.Children())
{
    Console.WriteLine($"{child.Name}: {child.Text}");
}

// Get specific child by name
var name = sourceNode.Children("name").FirstOrDefault();

// Indexer shorthand
var family = sourceNode["name"][0]["family"];
```

### Deep Navigation

```csharp
// Navigate multiple levels
var givenName = sourceNode["name"][0]["given"][0].Text;

// Handle missing paths safely
var telecom = sourceNode["telecom"]?.FirstOrDefault();
```

### Resource Type Detection

```csharp
var resourceType = sourceNode.ResourceType;

switch (resourceType)
{
    case "Patient":
        ProcessPatient(sourceNode);
        break;
    case "Observation":
        ProcessObservation(sourceNode);
        break;
}
```

## Extension Methods

### Common Extensions

```csharp
using Ignixa.Abstractions.Extensions;

// Get text value with default
var text = sourceNode.GetText("default");

// Check if has children
var hasChildren = sourceNode.HasChildren();

// Get all descendant nodes
var descendants = sourceNode.Descendants();
```

### Type Conversion

```csharp
// Convert to typed element
var element = sourceNode.ToElement(schema);

// Get typed value
var dateTime = sourceNode.ToDateTime();
var boolean = sourceNode.ToBoolean();
var integer = sourceNode.ToInteger();
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
var coding = sourceNode["code"]["coding"][0];
var system = coding["system"].Text;
var code = coding["code"].Text;
var display = coding["display"].Text;
```

## Custom ISourceNode Implementation

Implement `ISourceNode` for custom data sources:

```csharp
public class MySourceNode : ISourceNode
{
    private readonly JsonElement _element;

    public string Name { get; }
    
    public string? Text => _element.ValueKind == JsonValueKind.String 
        ? _element.GetString() 
        : null;

    public string? ResourceType => 
        _element.TryGetProperty("resourceType", out var rt) 
            ? rt.GetString() 
            : null;

    public IEnumerable<ISourceNode> Children(string? name = null)
    {
        if (_element.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var prop in _element.EnumerateObject())
        {
            if (name is null || prop.Name == name)
                yield return new MySourceNode(prop.Name, prop.Value);
        }
    }
}
```

## Best Practices

### 1. Prefer FHIRPath over Manual Navigation

```csharp
// ✅ Good: Declarative, handles missing elements
var display = element.Select("code.coding.first().display").FirstOrDefault();

// ❌ Avoid: Verbose, error-prone
var display = sourceNode["code"]?["coding"]?[0]?["display"]?.Text;
```

### 2. Use Type Information

```csharp
var element = sourceNode.ToElement(schema);

// Now you have type information
var type = element.InstanceType;
var definition = element.Definition;
```

### 3. Handle Missing Data Gracefully

```csharp
// Use null-conditional operators
var birthDate = sourceNode["birthDate"]?.Text;

// Or provide defaults
var active = sourceNode["active"]?.Text ?? "true";
```

## Related Documentation

- [Serialization](/docs/core-sdk/serialization)
- [FHIRPath](/docs/core-sdk/fhirpath)

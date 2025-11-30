# Ignixa.Abstractions

Core interfaces and abstractions for the Ignixa FHIR Server ecosystem. This package contains the foundational types used across all Ignixa packages.

## Why Use This Package?

You typically **don't need to install this directly** - it's automatically included as a dependency when you use other Ignixa packages like:

- Ignixa.FhirPath
- Ignixa.Search
- Ignixa.Validation
- Ignixa.Specification
- Ignixa.Serialization

This package provides the common abstractions that allow these packages to work together seamlessly.

## Installation

```bash
dotnet add package Ignixa.Abstractions
```

## What's Included

### Core Interfaces

- **IElement**: Represents a node in a FHIR resource tree
- **IType/ITypeExtended**: FHIR type definitions and metadata
- **ISchema**: Access to FHIR structure definitions
- **ISourceNavigator**: Low-level resource navigation
- **IBinding**: Value set binding information
- **IConstraint**: FHIRPath constraint definitions

## For Library Authors

If you're building your own FHIR tools, use these interfaces to ensure compatibility with the Ignixa ecosystem:

```csharp
using Ignixa.Abstractions;

// Accept IElement to work with any FHIR data source
public void ProcessResource(IElement resource)
{
    foreach (var child in resource.Children)
    {
        Console.WriteLine($"{child.Name}: {child.Value}");
    }
}

// Use ISchema for type information
public void ValidateType(ISchema schema, string typeName)
{
    var type = schema.GetTypeByName(typeName);
    // Work with type metadata
}
```

## License

MIT License - see LICENSE file in repository root

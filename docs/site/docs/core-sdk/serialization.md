---
sidebar_position: 3
title: Serialization
description: High-performance FHIR JSON serialization
---

# Ignixa.Serialization

High-performance FHIR JSON serialization using `System.Text.Json` with streaming support.

## Installation

```bash
dotnet add package Ignixa.Serialization
```

## JSON Parsing

### Parse from String

```csharp
using Ignixa.Serialization;

var json = """
{
  "resourceType": "Patient",
  "id": "123",
  "name": [{ "family": "Smith" }]
}
""";

var resource = JsonSourceNodeFactory.Parse(json);
```

### Parse from Stream

```csharp
await using var stream = File.OpenRead("patient.json");
var resource = await JsonSourceNodeFactory.ParseAsync(stream, cancellationToken);
```

### Parse from JsonNode

```csharp
using System.Text.Json.Nodes;

JsonNode jsonNode = JsonNode.Parse(json);
var resource = JsonSourceNodeFactory.Parse(jsonNode);
```

## JSON Serialization

### Write to String

```csharp
var json = resource.SerializeToString();

// Pretty-print with indentation
var prettyJson = resource.SerializeToString(pretty: true);
```

### Write to Stream

```csharp
await using var stream = File.Create("output.json");
resource.SerializeToStream(stream);

// Pretty-print with indentation
resource.SerializeToStream(stream, pretty: true);
```

### Write to Bytes

```csharp
ReadOnlyMemory<byte> bytes = resource.SerializeToBytes();

// Pretty-print with indentation
var prettyBytes = resource.SerializeToBytes(pretty: true);
```

## Mutable Nodes

Resources are backed by `System.Text.Json.Nodes.JsonObject` for direct manipulation:

```csharp
using Ignixa.Serialization;
using System.Text.Json.Nodes;

// Parse resource
var patient = JsonSourceNodeFactory.Parse(json);

// Access the underlying JsonObject via MutableNode property
JsonObject mutableNode = patient.MutableNode;

// Modify directly
mutableNode["active"] = false;
mutableNode["meta"]!["lastUpdated"] = DateTime.UtcNow.ToString("O");

// Add complex elements
var nameArray = new JsonArray
{
    new JsonObject
    {
        ["family"] = "Smith",
        ["given"] = new JsonArray { "John", "William" }
    }
};
mutableNode["name"] = nameArray;

// Serialize the modified resource
var updatedJson = patient.SerializeToString();
```

### Accessing Mutable Node from IElement

When working with `IElement` (for FHIRPath or validation), access the underlying `JsonNode` via `Meta<T>()`. Useful for PATCH operations after typed navigation:

```csharp
using Ignixa.Serialization;
using Ignixa.Specification;
using System.Text.Json.Nodes;

var schemaProvider = FhirVersion.R4.GetSchemaProvider();
var sourceNode = JsonSourceNodeFactory.Parse(json);
var element = sourceNode.ToElement(schemaProvider);

// Navigate using FHIRPath
var nameElement = element.Select("name.first()").FirstOrDefault();

// Get the underlying JsonNode for direct modification
var jsonNode = nameElement?.Meta<JsonNode>();
if (jsonNode is JsonObject nameObject)
{
    nameObject["family"] = "NewFamilyName";
}

// Changes are reflected in the source
var updatedJson = sourceNode.SerializeToString();
```

### Creating New Resources

```csharp
using Ignixa.Serialization.SourceNodes;
using System.Text.Json.Nodes;

// Create a new JsonObject
var jsonObject = new JsonObject
{
    ["resourceType"] = "Patient",
    ["id"] = "123",
    ["active"] = true
};

// Wrap in ResourceJsonNode
var patient = JsonSourceNodeFactory.Parse(jsonObject);
```

## Performance Features

### Efficient JSON Parsing

The parser uses `System.Text.Json` for high-performance deserialization:

- Zero-allocation streaming from `Stream` with `ParseAsync()`
- Direct `JsonNode` wrapping for in-memory JSON trees
- Lazy property access via `MutableNode` - only parsed when accessed
- Reusable serializer options for consistent behavior

## Bundle Handling

Use the typed `BundleJsonNode` and `BundleComponentJsonNode` models for type-safe bundle operations.

### Parse Bundle

```csharp
using Ignixa.Serialization.Models;

// Parse JSON into typed BundleJsonNode
var bundle = JsonSourceNodeFactory.Parse<BundleJsonNode>(bundleJson);

// Access typed properties
var bundleType = bundle.Type;    // BundleType enum
var total = bundle.Total;        // int?

// Iterate entries with typed access
foreach (var entry in bundle.Entry)
{
    var fullUrl = entry.FullUrl;           // string
    var resource = entry.Resource;          // ResourceJsonNode
    var resourceType = resource.ResourceType;

    // Access request/response for transaction bundles
    var request = entry.Request;            // BundleComponentRequestJsonNode
    var response = entry.Response;          // BundleComponentResponseJsonNode
}
```

### Create Bundle

```csharp
using Ignixa.Serialization.Models;

// Create typed bundle
var bundle = new BundleJsonNode
{
    Type = BundleJsonNode.BundleType.Searchset,
    Total = patients.Count
};

// Add entries with typed components
foreach (var patient in patients)
{
    var entry = new BundleComponentJsonNode
    {
        FullUrl = $"urn:uuid:{Guid.NewGuid()}",
        Resource = patient
    };

    bundle.Entry.Add(entry);
}

// Serialize
var json = bundle.SerializeToString();
```

### Transaction Bundle

```csharp
using Ignixa.Serialization.Models;

var bundle = new BundleJsonNode
{
    Type = BundleJsonNode.BundleType.Transaction
};

var entry = new BundleComponentJsonNode
{
    FullUrl = "urn:uuid:patient-1",
    Resource = patient,
    Request = new BundleComponentRequestJsonNode
    {
        Method = "POST",
        Url = "Patient"
    }
};

bundle.Entry.Add(entry);
```

## Error Handling

### Parse Errors

```csharp
using System.Text.Json;

try
{
    var resource = JsonSourceNodeFactory.Parse(json);
}
catch (JsonException ex)
{
    Console.WriteLine($"Parse error: {ex.Message}");
    Console.WriteLine($"Line: {ex.LineNumber}, Position: {ex.BytePositionInLine}");
}
```

### Null Handling

```csharp
// ArgumentNullException thrown for null inputs
ArgumentNullException.ThrowIfNull(json);
var resource = JsonSourceNodeFactory.Parse(json);

// InvalidOperationException for malformed JSON
try
{
    var resource = JsonSourceNodeFactory.Parse(invalidJson);
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Failed to deserialize: {ex.Message}");
}
```

## Related Documentation

- [Abstractions](/docs/core-sdk/abstractions)
- [FHIRPath](/docs/core-sdk/fhirpath)

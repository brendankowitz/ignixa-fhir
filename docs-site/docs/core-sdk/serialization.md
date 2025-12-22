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

var sourceNode = JsonSourceNavigator.Parse(json);
```

### Parse from Stream

```csharp
await using var stream = File.OpenRead("patient.json");
var sourceNode = await JsonSourceNavigator.ParseAsync(stream);
```

### Parse from JsonDocument

```csharp
using var doc = JsonDocument.Parse(json);
var sourceNode = JsonSourceNavigator.FromJsonElement(doc.RootElement);
```

## JSON Serialization

### Write to String

```csharp
var json = JsonSourceNavigator.Serialize(sourceNode);
```

### Write to Stream

```csharp
await using var stream = File.Create("output.json");
await JsonSourceNavigator.SerializeAsync(sourceNode, stream);
```

### Write with Options

```csharp
var options = new FhirSerializationOptions
{
    Indent = true,
    IncludeNullValues = false,
    SummaryMode = SummaryMode.Data
};

var json = JsonSourceNavigator.Serialize(sourceNode, options);
```

## Streaming Serialization

For large datasets, use streaming:

```csharp
await using var writer = new FhirJsonWriter(stream);

// Write resources one at a time
foreach (var resource in resources)
{
    await writer.WriteResourceAsync(resource);
}
```

### NDJSON (Newline Delimited JSON)

For bulk data:

```csharp
await using var writer = new NdjsonWriter(stream);

foreach (var patient in patients)
{
    await writer.WriteLineAsync(patient);
}
```

## Mutable Nodes

For building or modifying FHIR data:

```csharp
using Ignixa.Serialization.Mutable;

// Create a new resource
var patient = new MutableNode("Patient");
patient["id"] = "123";
patient["active"] = true;

// Add complex elements
var name = patient.AddChild("name");
name["family"] = "Smith";
name["given"].Add("John");
name["given"].Add("William");

// Serialize
var json = patient.ToJson();
```

### Modify Existing Data

```csharp
// Parse to mutable
var mutable = MutableNode.FromJson(json);

// Modify
mutable["active"] = false;
mutable["meta"]["lastUpdated"] = DateTime.UtcNow.ToString("O");

// Serialize back
var updated = mutable.ToJson();
```

## Performance Features

### Zero-Copy Reading

The parser uses `JsonDocument` internally for minimal allocations:

```csharp
// Efficient: no string allocations for navigation
var value = sourceNode["name"][0]["family"].Text;
```

### Pooled Writers

For high-throughput scenarios:

```csharp
var pool = new FhirWriterPool(poolSize: 10);

// Borrow a writer
using var writer = pool.Rent();
await writer.WriteAsync(sourceNode, stream);
// Automatically returned to pool
```

## Bundle Handling

### Parse Bundle

```csharp
var bundle = JsonSourceNavigator.Parse(bundleJson);

foreach (var entry in bundle["entry"].Children())
{
    var resource = entry["resource"];
    var resourceType = resource.ResourceType;
    // Process each resource
}
```

### Create Bundle

```csharp
var bundle = new MutableNode("Bundle");
bundle["type"] = "searchset";
bundle["total"] = patients.Count;

foreach (var patient in patients)
{
    var entry = bundle["entry"].Add();
    entry["fullUrl"] = $"urn:uuid:{Guid.NewGuid()}";
    entry["resource"] = patient;
}
```

## Content Types

| Content Type | Format |
|--------------|--------|
| `application/fhir+json` | Standard FHIR JSON |
| `application/json` | JSON (fallback) |
| `application/fhir+ndjson` | Newline Delimited JSON |

## Error Handling

### Parse Errors

```csharp
try
{
    var sourceNode = JsonSourceNavigator.Parse(json);
}
catch (FhirParseException ex)
{
    Console.WriteLine($"Parse error at line {ex.LineNumber}: {ex.Message}");
}
```

### Validation During Parse

```csharp
var options = new FhirParseOptions
{
    ValidateStructure = true,
    StrictMode = false
};

var sourceNode = JsonSourceNavigator.Parse(json, options);
```

## Related Documentation

- [Abstractions](/docs/core-sdk/abstractions)
- [FHIRPath](/docs/core-sdk/fhirpath)

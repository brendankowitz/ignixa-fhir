# Ignixa.Extensions.ProfileBehaviors

Extensible profile-specific behaviors for FHIR Implementation Guides (US Core, AU Base, etc.).

## Overview

This library provides infrastructure for implementing profile-specific transformations and behaviors that run in the Medino pipeline. It enables:

- **Automatic compliance** with Implementation Guide requirements
- **Extensible visitor pattern** for walking and transforming FHIR resources
- **Profile detection** to conditionally activate behaviors
- **Zero-impact** on non-profiled resources

## Architecture

### Core Abstractions

```
Ignixa.Extensions.ProfileBehaviors/
├── Abstractions/
│   ├── IResourcePropertyVisitor.cs       # Visitor interface for property visitation
│   ├── PropertyVisitResult.cs            # Result actions (Include/Skip/Mutate/Inject)
│   ├── ElementMetadata.cs                # Lightweight element metadata wrapper
│   └── VisitorContext.cs                 # Context for visitor operations
├── Infrastructure/
│   └── ExtensibleJsonNodeVisitor.cs      # Visits JsonNode trees with schema metadata
└── Features/
    └── UsCore/
        ├── DataAbsentReasonBehavior.cs   # Medino behavior for US Core compliance
        ├── DataAbsentReasonVisitor.cs    # Visitor that injects data-absent-reason
        └── ProfileDetectionService.cs    # Detects loaded profiles by tenant
```

### Design Pattern

The library uses a **visitor pattern** to visit FHIR resources:

1. **ExtensibleJsonNodeVisitor**: Infrastructure for visiting JsonNode trees
   - Takes an `IStructureDefinitionSummaryProvider` for schema metadata
   - Takes an `IResourcePropertyVisitor` for transformation logic
   - Visits existing properties → calls `VisitProperty()`
   - Detects missing mandatory properties → calls `VisitMissingProperty()`

2. **IResourcePropertyVisitor**: Interface for custom transformations
   - `VisitProperty()`: Handle existing properties (Include/Skip/Mutate)
   - `VisitMissingProperty()`: Handle missing mandatory properties (Skip/Inject)

3. **Medino Pipeline Behaviors**: Apply transformations before validation
   - Run BEFORE `ValidationBehavior` to fix up resources
   - Conditionally activate based on loaded profiles
   - Fail gracefully if transformation fails

## US Core Data-Absent-Reason

### Problem

US Core profiles require mandatory elements (min > 0) to be present. When data is missing with unknown reason, the requirement is:

> "Include the element with a data-absent-reason extension using code 'unknown'."

Example:
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

### Solution

`DataAbsentReasonBehavior` automatically injects these extensions:

1. **Detects** when US Core is loaded (via `ProfileDetectionService`)
2. **Visits** the resource JsonNode with `ExtensibleJsonNodeVisitor`
3. **Injects** data-absent-reason for missing mandatory elements
4. **Continues** to validation (which now passes)

### Pipeline Order

```
HTTP Request
    ↓
CapabilityEnforcementBehavior   # Check operation is supported
    ↓
DataAbsentReasonBehavior        # ← Inject missing mandatory elements
    ↓
ValidationBehavior              # Validate (now passes CardinalityCheck)
    ↓
CreateOrUpdateResourceHandler   # Persist to database
    ↓
HTTP Response
```

## Usage

### Registration (in Ignixa.Application or Ignixa.Api)

```csharp
// Program.cs or DI container setup

// 1. Register ProfileDetectionService
containerBuilder.RegisterType<ProfileDetectionService>()
    .As<IProfileDetectionService>()
    .SingleInstance();

// 2. Register DataAbsentReasonBehavior (generic, applies to all requests)
containerBuilder.RegisterGeneric(typeof(DataAbsentReasonBehavior<,>))
    .As(typeof(IPipelineBehavior<,>))
    .InstancePerLifetimeScope();

// Ensure it runs BEFORE ValidationBehavior by registering in correct order
```

### Profile Detection

```csharp
// Check if US Core is loaded for a tenant
var isActive = await _profileDetection.IsUSCoreActiveAsync(tenantId);
```

### Custom Visitors

Create custom visitors for other profiles:

```csharp
public class MyCustomVisitor : IResourcePropertyVisitor
{
    public PropertyVisitResult VisitProperty(
        string propertyName,
        ElementMetadata? metadata,
        int depth,
        WalkingContext context)
    {
        // Custom logic for existing properties
        if (propertyName == "sensitive")
        {
            return PropertyVisitResult.Mutate(node => RedactValue(node));
        }

        return PropertyVisitResult.Include();
    }

    public PropertyVisitResult VisitMissingProperty(
        string propertyName,
        ElementMetadata metadata,
        int depth,
        WalkingContext context)
    {
        // Custom logic for missing properties
        return PropertyVisitResult.Skip();
    }
}
```

## Future Extensions

### Streaming Serialization

For output transformations (e.g., element filtering in Bundles), create an `ExtensibleStreamingSerializer`:

```csharp
public class ExtensibleStreamingSerializer
{
    public void Serialize(
        Utf8JsonWriter writer,
        ReadOnlyMemory<byte> resourceBytes,
        IStructureDefinitionSummaryProvider schemaProvider,
        IResourcePropertyVisitor visitor)
    {
        // Use Utf8JsonReader/Writer for zero-copy streaming
        // Call visitor.VisitProperty() for each property
        // Apply Include/Skip/Mutate based on visitor result
    }
}
```

This would unify the pattern for both:
- **Input transformation** (JsonNode walker) - e.g., DataAbsentReasonBehavior
- **Output transformation** (streaming serializer) - e.g., ResourceElementsSerializer

### Additional Profiles

Extend beyond US Core:

- **AU Base** (Australian Core)
- **UK Core**
- **IPA** (International Patient Access)
- Custom organization profiles

Each profile gets its own visitor and behavior:

```
Features/
├── UsCore/
│   ├── DataAbsentReasonBehavior.cs
│   └── DataAbsentReasonVisitor.cs
├── AuBase/
│   ├── IndigenousStatusBehavior.cs
│   └── IndigenousStatusVisitor.cs
└── UkCore/
    └── ...
```

## Testing

### Unit Tests

```csharp
[Fact]
public void GivenMissingMandatoryElement_WhenVisiting_ThenInjectsDataAbsentReason()
{
    // Arrange
    var visitor = new DataAbsentReasonVisitor();
    var metadata = new ElementMetadata
    {
        ElementName = "name",
        IsRequired = true,
        IsCollection = true
    };

    // Act
    var result = visitor.VisitMissingProperty("name", metadata, depth: 0, context);

    // Assert
    Assert.Equal(PropertyAction.Inject, result.Action);
    var injected = result.InjectionFunc!();
    // Verify structure has data-absent-reason extension
}
```

### Integration Tests

```csharp
[Fact]
public async Task GivenUSCoreTenant_WhenCreatingPatientWithoutName_ThenAutoInjectsExtension()
{
    // Arrange: Load US Core package
    await LoadPackageAsync("hl7.fhir.us.core", "5.0.1");

    // Act: POST Patient without name
    var response = await _client.PostAsync("/Patient", CreatePatientWithoutName());

    // Assert: Should succeed (201 Created)
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);

    // Verify: GET Patient and check for data-absent-reason extension
    var patient = await _client.GetAsync(response.Headers.Location);
    var json = await patient.Content.ReadAsStringAsync();
    Assert.Contains("data-absent-reason", json);
    Assert.Contains("\"valueCode\":\"unknown\"", json);
}
```

## Dependencies

- `Ignixa.Abstractions` - Core FHIR abstractions (IStructureDefinitionSummary, etc.)
- `Ignixa.Domain` - Domain models (FhirSpecification, IPackageResourceRepository)
- `Ignixa.Validation` - Validation infrastructure (though not directly used)
- `Ignixa.Serialization` - Resource serialization (ResourceJsonNode)
- `Ignixa.Search` - FHIR version context (IFhirVersionContext)
- `Ignixa.Specification` - Schema providers (IFhirSchemaProvider)
- `Medino` - CQRS/pipeline behavior framework
- `Microsoft.AspNetCore.Http.Abstractions` - HTTP context access
- `Microsoft.Extensions.Logging.Abstractions` - Logging

## License

MIT License - Same as Ignixa FHIR Server

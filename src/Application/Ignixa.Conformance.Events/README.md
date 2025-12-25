# Ignixa.Conformance.Events

Event sourcing infrastructure for FHIR conformance resource management. This package provides the core event types and abstractions for tracking changes to FHIR packages, structure definitions, and search parameters.

## Purpose

This library enables event-driven management of FHIR conformance resources:

- **Package lifecycle**: Track package uploads, activations, and deactivations
- **Structure definitions**: Monitor profile and extension changes
- **Search parameters**: Manage custom search parameter registration and status

## Installation

```bash
dotnet add package Ignixa.Conformance.Events
```

## Core Types

### Event Store Abstraction

```csharp
public interface ISourceEventStore
{
    Task<IReadOnlyList<SourceEvent>> AppendAsync(
        IEnumerable<NewSourceEvent> events,
        CancellationToken cancellationToken);

    IAsyncEnumerable<SourceEvent> ReadAllAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<SourceEvent> ReadFromAsync(long afterEventId, CancellationToken cancellationToken);
    IAsyncEnumerable<SourceEvent> ReadStreamAsync(string streamId, CancellationToken cancellationToken);
}
```

### Package Events

| Event | Description |
|-------|-------------|
| `PackageUploaded` | FHIR package uploaded with resource manifest |
| `PackageActivated` | Package activated with selected resources |
| `PackageDeactivated` | Package deactivated with reason |

### Search Parameter Events

| Event | Description |
|-------|-------------|
| `SearchParameterRegistered` | New search parameter registered |
| `SearchParameterStatusChanged` | Search parameter status updated |

### Structure Definition Events

| Event | Description |
|-------|-------------|
| `StructureDefinitionRegistered` | Profile or extension registered |
| `StructureDefinitionStatusChanged` | Definition status updated |

## Dependencies

This package references:

- `Ignixa.Specification` - FHIR structure definitions and type providers

## License

MIT License - see LICENSE file in repository root

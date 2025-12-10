# Ignixa.Application

Business logic and request handlers for Ignixa FHIR Server. This package contains the application layer that orchestrates FHIR operations using the Medino request/handler pattern.

## Why Use This Package?

- **Request/Handler pattern**: Clean separation using Medino (similar to MediatR)
- **Multi-tenant support**: Built-in tenant context and partition handling
- **FHIR operations**: Complete CRUD, search, history, patch, and bundle support
- **Validation pipeline**: Configurable validation tiers (None, Spec, Profile, Full)

## Installation

```bash
dotnet add package Ignixa.Application
```

## Quick Start

### Sending Requests via Mediator

```csharp
using Ignixa.Application.Features.Resource;
using Medino;

// Inject IMediator
public class MyService(IMediator mediator)
{
    public async Task<SearchEntryResult?> GetPatientAsync(string id, CancellationToken ct)
    {
        var query = new GetResourceQuery("Patient", id);
        return await mediator.SendAsync(query, ct);
    }

    public async Task<UpdateResult> SavePatientAsync(
        ResourceJsonNode patient,
        CancellationToken ct)
    {
        var command = new CreateOrUpdateResourceCommand(
            "Patient",
            patient.Id,
            patient,
            HttpMethod.Put,
            IfMatch: null);

        return await mediator.SendAsync(command, ct);
    }
}
```

### DI Registration (Autofac)

```csharp
using Autofac;
using Medino;

// Register Medino
containerBuilder.RegisterType<Mediator>().As<IMediator>().SingleInstance();

// Register all handlers from assembly
containerBuilder.RegisterAssemblyTypes(typeof(GetResourceHandler).Assembly)
    .AsClosedTypesOf(typeof(IRequestHandler<,>))
    .InstancePerLifetimeScope();
```

## Available Features

| Feature | Query/Command | Description |
|---------|---------------|-------------|
| **Read** | `GetResourceQuery` | Get resource by ID |
| **Create/Update** | `CreateOrUpdateResourceCommand` | Create or update a resource |
| **Delete** | `DeleteResourceCommand` | Soft delete a resource |
| **Search** | `SearchResourcesQuery` | FHIR search with parameters |
| **History** | `GetResourceHistoryQuery` | Instance/type/system history |
| **Patch** | `PatchResourceCommand` | FHIRPath and JSON Patch |
| **Bundle** | `ProcessBundleCommand` | Batch/transaction bundles |
| **Metadata** | `GetCapabilityStatementQuery` | CapabilityStatement generation |
| **Terminology** | `ValueSetExpandQuery` | $expand, $lookup, $translate |

## Dependencies

- `Ignixa.Domain` - Repository interfaces and domain models
- `Ignixa.Search` - Search parameter indexing
- `Ignixa.Validation` - Resource validation
- `Ignixa.FhirPath` - FHIRPath evaluation
- `Medino` - Request/response pipeline

## License

MIT License - see LICENSE file in repository root

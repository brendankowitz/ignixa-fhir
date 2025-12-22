---
sidebar_position: 2
title: Architecture
description: Internal architecture and design patterns
---

# Architecture

Ignixa follows Clean Architecture principles with strict layer separation, ensuring maintainability and testability.

## Layer Overview

```
┌─────────────────────────────────────────────────────────────┐
│                        API Layer                             │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │  Endpoints  │  │ Middleware  │  │  Request Pipeline   │  │
│  └─────────────┘  └─────────────┘  └─────────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│                    Application Layer                         │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │   Queries   │  │  Commands   │  │     Handlers        │  │
│  └─────────────┘  └─────────────┘  └─────────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│                      Domain Layer                            │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │  Entities   │  │ Interfaces  │  │   Value Objects     │  │
│  └─────────────┘  └─────────────┘  └─────────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│                      Data Layer                              │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │ SQL Server  │  │ FileSystem  │  │   Blob Storage      │  │
│  └─────────────┘  └─────────────┘  └─────────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│                     Core SDK (Shared)                        │
│  ┌──────────────┐ ┌─────────────┐ ┌──────────┐ ┌─────────┐  │
│  │ Abstractions │ │Serialization│ │ FHIRPath │ │Validation│  │
│  └──────────────┘ └─────────────┘ └──────────┘ └─────────┘  │
│  ┌──────────────┐ ┌─────────────┐ ┌──────────┐              │
│  │Specification │ │   Search    │ │FhirFakes │              │
│  └──────────────┘ └─────────────┘ └──────────┘              │
└─────────────────────────────────────────────────────────────┘
```

The **Core SDK** packages are shared building blocks used across all layers. They provide:
- **Abstractions**: Core interfaces (`ISourceNode`, `IElement`)
- **Serialization**: JSON parsing and writing
- **FHIRPath**: Expression evaluation
- **Validation**: Three-tier validation engine
- **Search**: Parameter indexing and extraction
- **Specification**: FHIR structure definitions

## CQRS Pattern

Ignixa uses Command Query Responsibility Segregation (CQRS) via the [Medino](https://github.com/brendankowitz/Medino) library.

### Query Example

```csharp
// Query definition
public record GetPatientQuery(string Id) : IRequest<FhirResponse>;

// Handler implementation
public class GetPatientHandler(IFhirRepository repository) 
    : IRequestHandler<GetPatientQuery, FhirResponse>
{
    public async Task<FhirResponse> HandleAsync(
        GetPatientQuery request, 
        CancellationToken cancellationToken)
    {
        var patient = await repository.ReadAsync(
            "Patient", 
            request.Id, 
            cancellationToken);
            
        return patient is not null
            ? FhirResponse.Ok(patient)
            : FhirResponse.NotFound();
    }
}
```

### Command Example

```csharp
// Command definition
public record CreatePatientCommand(ISourceNode Resource) : IRequest<FhirResponse>;

// Handler with validation
public class CreatePatientHandler(
    IFhirRepository repository,
    IValidator validator) 
    : IRequestHandler<CreatePatientCommand, FhirResponse>
{
    public async Task<FhirResponse> HandleAsync(
        CreatePatientCommand request, 
        CancellationToken cancellationToken)
    {
        var outcome = await validator.ValidateAsync(request.Resource);
        if (!outcome.Success)
        {
            return FhirResponse.BadRequest(outcome);
        }

        var result = await repository.CreateAsync(
            request.Resource, 
            cancellationToken);
            
        return FhirResponse.Created(result);
    }
}
```

## API Layer

The API layer uses ASP.NET Core Minimal APIs for low overhead:

```csharp
public static class PatientEndpoints
{
    public static IEndpointRouteBuilder MapPatientEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/Patient/{id}", GetPatient);
        endpoints.MapPost("/Patient", CreatePatient);
        endpoints.MapPut("/Patient/{id}", UpdatePatient);
        endpoints.MapDelete("/Patient/{id}", DeletePatient);
        
        return endpoints;
    }

    private static async Task<IResult> GetPatient(
        string id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new GetPatientQuery(id), 
            cancellationToken);
            
        return result.ToHttpResult();
    }
}
```

## Dependency Rules

Strict dependency rules ensure clean separation:

```
✅ API → Application → Domain
✅ DataLayer → Domain
❌ Domain → Application (violation)
❌ Application → DataLayer (use interfaces)
```

### Allowed Dependencies

| Layer | Can Reference |
|-------|---------------|
| API | Application, Domain |
| Application | Domain |
| Domain | Nothing (pure) |
| DataLayer | Domain (implements interfaces) |

## FHIR Data Flow

Request processing flow:

```
HTTP Request
     │
     ▼
┌─────────────┐
│  Endpoint   │ Parse request, extract parameters
└──────┬──────┘
       │
       ▼
┌─────────────┐
│   Query/    │ Create immutable request object
│   Command   │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│   Handler   │ Execute business logic
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ Repository  │ Data access via interface
└──────┬──────┘
       │
       ▼
┌─────────────┐
│  DataLayer  │ Concrete storage implementation
└─────────────┘
```

## Multi-Tenancy Architecture

See [Multi-Tenancy](/docs/server/multi-tenancy) for tenant isolation details.

```
┌──────────────────────────────────────────┐
│              Request Pipeline             │
├──────────────────────────────────────────┤
│         Tenant Resolution Middleware      │
│   Extract tenant from /tenant/{id}/...   │
├────────────────┬────────────────┬────────┤
│   Tenant 1     │   Tenant 2     │  ...   │
│  (Partition)   │  (Partition)   │        │
└────────────────┴────────────────┴────────┘
```

## Related Documentation

- [ADR: Vertical Slice Architecture](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2509-vertical-slice-architecture.md)
- [ADR: Multi-Tenancy](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2510-multi-tenancy.md)

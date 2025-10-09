# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a C# .NET 9.0 codebase for **FHIR Server v2** - a next-generation FHIR server implementation. The project implements a clean architecture with separate projects for each architectural layer, supporting multi-data-layer scenarios (Isolation vs Distributed modes).

## Current Status

**Phase**: Prototype Implementation (ADR-2501)
**SDK Version**: Firely SDK 6.0.0-rc1 (unified multi-version support)
**Build Status**: ✅ All 9 projects build successfully

## Solution Architecture

The solution follows a **layered architecture** with **separate projects** for each layer:

```
All.sln (9 projects)
├── 1. Sparky.Domain              - Domain models and abstractions (no dependencies)
├── 2. Sparky.Application         - Medino handlers and business logic (→ Domain)
├── 3. Sparky.DataLayer.*         - Data storage implementations (→ Domain)
│   ├── Sparky.DataLayer.FileSystem      - File-based repository (prototype)
│   └── Sparky.DataLayer.InMemoryIndex   - Resource location tracking
├── 4. Sparky.Api                 - ASP.NET Core API (→ all layers)
└── Supporting Libraries
    ├── Sparky.Extensions         - FHIR extensions and utilities
    ├── Sparky.Search             - Search functionality
    └── Sparky.SourceNodeSerialization - Serialization utilities
```

### Project Details

#### 1. **Sparky.Domain** (Domain Layer)
- **Purpose**: Core domain models and abstractions
- **Dependencies**: Hl7.Fhir.R4 only
- **Key Files**:
  - `Abstractions/IFhirRepository.cs` - Repository interface
  - `Models/ResourceKey.cs` - Resource identifier
  - `Models/ResourceWrapper.cs` - Resource + metadata container
  - `Models/ResourceRequest.cs` - HTTP request metadata
  - `Models/TransactionId.cs` - Transaction tracking

#### 2. **Sparky.Application** (Application Layer)
- **Purpose**: Business logic and Medino message handlers
- **Dependencies**: Sparky.Domain, Medino, Microsoft.Extensions.Logging.Abstractions
- **Pattern**: Feature folders (Features/Patient/)
- **Key Files**:
  - `Features/Patient/CreateOrUpdatePatientCommand.cs` - IRequest<ResourceKey>
  - `Features/Patient/CreateOrUpdatePatientHandler.cs` - IRequestHandler
  - `Features/Patient/GetPatientQuery.cs` - IRequest<ResourceWrapper?>
  - `Features/Patient/GetPatientHandler.cs` - IRequestHandler

#### 3. **Sparky.DataLayer.FileSystem** (Data Layer)
- **Purpose**: File-based FHIR repository implementation (prototype)
- **Dependencies**: Sparky.Domain, Hl7.Fhir.R4, Microsoft.Extensions.Logging.Abstractions
- **Storage Format**:
  - `{baseDir}/{resourceType}/{id}.json` - Resource JSON
  - `{baseDir}/{resourceType}/{id}.meta.json` - Metadata sidecar
- **Key Files**:
  - `FileSystem/FileBasedFhirRepository.cs` - IFhirRepository implementation

#### 4. **Sparky.DataLayer.InMemoryIndex** (Data Layer)
- **Purpose**: Tracks which data layer(s) contain each resource (for Distributed mode)
- **Dependencies**: Sparky.Domain
- **Key Files**:
  - `InMemoryIndex/IResourceLocationIndex.cs` - Interface
  - `InMemoryIndex/InMemoryResourceLocationIndex.cs` - ConcurrentDictionary implementation

#### 5. **Sparky.Api** (API Layer)
- **Purpose**: ASP.NET Core Web API endpoints
- **Dependencies**: All layers (Domain, Application, DataLayer.*)
- **Pattern**: Feature folders (Features/Patient/Api/)
- **Key Files**:
  - `Features/Patient/Api/PatientController.cs` - GET /Patient/{id}, PUT /Patient/{id}
  - `Program.cs` - Application startup

#### Supporting Libraries

- **Sparky.Extensions**: FHIR extensions, value sets, schema helpers
- **Sparky.Search**: Search parameter definitions, indexing, search values
- **Sparky.SourceNodeSerialization**: Custom serialization for FHIR ISourceNode

## Architecture Principles

### 1. Layer Separation
- **Domain** has no dependencies (pure models)
- **Application** depends only on Domain (business logic)
- **DataLayer** depends only on Domain (storage implementations)
- **API** depends on all layers (HTTP concerns)

### 2. Feature Folders
- Organize by feature/capability (Patient, Observation, etc.)
- Each feature contains Api, Application, and optional Domain folders
- Example: `Features/Patient/Api/`, `Features/Patient/Application/`

### 3. Separate DataLayer Projects
- Each storage implementation is its own project
- Easy to add: Sparky.DataLayer.SqlServer, Sparky.DataLayer.CosmosDB, etc.
- Supports multi-data-layer scenarios (Isolation vs Distributed modes)

### 4. Medino Messaging
- Use **IRequest<TResponse>** for commands/queries (not ICommand)
- Use **IRequestHandler<TRequest, TResponse>** for handlers
- Method name: `HandleAsync` (not Handle)
- Example: `public record GetPatientQuery(string Id) : IRequest<ResourceWrapper?>`

## Common Commands

### Build
```bash
# Build entire solution
dotnet build All.sln

# Build specific layer
dotnet build src/Sparky.Domain/Sparky.Domain.csproj
dotnet build src/Sparky.Application/Sparky.Application.csproj
dotnet build src/Sparky.DataLayer.FileSystem/Sparky.DataLayer.FileSystem.csproj
dotnet build src/Sparky.Api/Sparky.Api.csproj
```

### Test
```bash
# Run all tests
dotnet test All.sln

# Run specific test project
dotnet test test/Sparky.Api.Tests/Sparky.Api.Tests.csproj
```

### Run API
```bash
dotnet run --project src/Sparky.Api/Sparky.Api.csproj
```

## Code Standards

- **StyleCop**: Configured via `stylecop.json` with Microsoft Corporation copyright headers
- **Code Analysis**: Latest analysis level enabled with code style enforcement in build
- **Warnings as Errors**: Enabled with specific suppressions for SA (StyleCop) and CA (Code Analysis) rules
- **Indentation**: 4 spaces, no tabs
- **Using Directives**: System usings first, placed outside namespace
- **Nullable Reference Types**: Enabled in new projects (Domain, Application, DataLayer, Api)

## Key Dependencies

### Centralized Package Management
All package versions managed in `Directory.Packages.props`:

- **Firely SDK**: Hl7.Fhir.R4 (6.0.0-rc1) - Multi-version FHIR support
- **Messaging**: Medino (2.0.1) - In-process messaging
- **IoC Container**: Autofac (8.2.0) - Dependency injection
- **Logging**: Microsoft.Extensions.Logging.Abstractions
- **Memory**: Microsoft.IO.RecyclableMemoryStream (3.0.1)

### SDK 6.0 Changes
- **Unified Packages**: Just Hl7.Fhir.R4, R4B, R5, STU3 (no separate .Core/.Specification)
- **Serialization**: FhirJsonNode.ParseAsync, FhirJsonSerializer with Utf8JsonWriter
- **Nullable Context**: SDK 6.0 has nullable enabled, old code needs annotations

## FHIR Support

### Versions Supported
- **R4**: Primary implementation target
- **R4B, R5, STU3**: Supported via SDK 6.0 unified packages

### Search Parameters
Embedded JSON files in `Sparky.Search/Data/{Version}/`:
- `search-parameters.json` - FHIR search parameter definitions
- `unsupported-search-parameters.json` - Not implemented
- `BaseCapabilities.json` - Capability statement
- `compartment.json` - Compartment definitions
- `resourcepath-codesystem-mappings.json` - Code system mappings

## Development Guidelines

### Adding a New Feature (e.g., Observation)

1. **Application Layer** - Create handlers
   ```
   src/Sparky.Application/Features/Observation/
   ├── CreateObservationCommand.cs
   ├── CreateObservationHandler.cs
   ├── GetObservationQuery.cs
   └── GetObservationHandler.cs
   ```

2. **API Layer** - Create controller
   ```
   src/Sparky.Api/Features/Observation/Api/
   └── ObservationController.cs
   ```

3. **No changes needed** in Domain or DataLayer (already generic)

### Adding a New DataLayer Implementation (e.g., SQL Server)

1. **Create new project**
   ```bash
   dotnet new classlib -n Sparky.DataLayer.SqlServer -o src/Sparky.DataLayer.SqlServer
   dotnet add src/Sparky.DataLayer.SqlServer reference src/Sparky.Domain
   dotnet sln add src/Sparky.DataLayer.SqlServer
   ```

2. **Implement IFhirRepository**
   ```csharp
   namespace Sparky.DataLayer.SqlServer;

   public class SqlServerFhirRepository : IFhirRepository
   {
       // Implement GetAsync, CreateOrUpdateAsync
   }
   ```

3. **Register in Sparky.Api** (Autofac/DI)

### SDK 6.0 API Patterns

```csharp
// Parsing JSON to ISourceNode
ISourceNode node = await FhirJsonNode.ParseAsync(jsonString);

// Serializing (prototype uses RawJson property for simplicity)
string json = resourceWrapper.RawJson; // Stored during read
```

## Known Issues / Workarounds

### 1. Sparky.Search Nullable Compatibility
- **Issue**: Old code doesn't use nullable annotations
- **Workaround**: Nullable disabled (`<Nullable>disable</Nullable>`)
- **TODO**: Incrementally enable nullable and add annotations

### 2. Sparky.Specification JsonSchema.Net
- **Issue**: API changed in version 7.x
- **Status**: Temporarily removed from solution
- **TODO**: Migrate to new JsonSchema.Net API or replace

### 3. FhirEvaluationContext.ElementResolver
- **Issue**: SDK 6.0 signature changed
- **Workaround**: Commented out in TypedElementSearchIndexer.cs:66
- **TODO**: Determine correct SDK 6.0 signature

### 4. ISourceNode Serialization
- **Issue**: No direct ToJson() method in SDK 6.0
- **Workaround**: Store RawJson string in ResourceWrapper.RawJson
- **Production**: Use proper serialization via FhirJsonSerializer + Utf8JsonWriter

## Implementation Progress (ADR-2501: Prototype Phase)

**Current Phase**: Prototype Implementation (Weeks 1-8)
**Status**: 🟢 Core Architecture Complete, 🟡 Wiring In Progress

### ✅ Completed Tasks

1. **Project Structure** (Week 1)
   - ✅ Created layered architecture with separate projects
   - ✅ Sparky.Domain - Models and abstractions
   - ✅ Sparky.Application - Medino handlers
   - ✅ Sparky.DataLayer.FileSystem - File-based repository
   - ✅ Sparky.DataLayer.InMemoryIndex - Resource location tracking
   - ✅ Sparky.Api - ASP.NET Core controllers

2. **Domain Layer** (Week 1)
   - ✅ ResourceKey, ResourceWrapper, ResourceRequest models
   - ✅ IFhirRepository abstraction
   - ✅ Feature folder structure established

3. **Data Layer** (Week 2)
   - ✅ FileBasedFhirRepository with JSON + metadata sidecars
   - ✅ InMemoryResourceLocationIndex (ConcurrentDictionary)
   - ✅ Version tracking and metadata storage

4. **Application Layer** (Week 2)
   - ✅ Patient CreateOrUpdateCommand/Handler
   - ✅ Patient GetQuery/Handler
   - ✅ Medino IRequest<T>/IRequestHandler<T,R> patterns

5. **API Layer** (Week 3)
   - ✅ PatientController with GET /Patient/{id} and PUT /Patient/{id}
   - ✅ Feature folder organization

6. **SDK Migration** (Week 1)
   - ✅ Upgraded to Firely SDK 6.0.0-rc1
   - ✅ Fixed Sparky.Search nullable compatibility issues
   - ✅ Centralized package management

### 🟡 In Progress

7. **Dependency Injection & Wiring** (Week 3-4)
   - 🔲 Configure Autofac container
   - 🔲 Register IFhirRepository → FileBasedFhirRepository
   - 🔲 Register IMediator → Medino
   - 🔲 Configure logging
   - 🔲 Wire up Program.cs startup

8. **Testing** (Week 4-5)
   - 🔲 Unit tests for handlers (80% coverage target)
   - 🔲 Integration tests for PUT /Patient/{id}
   - 🔲 Integration tests for GET /Patient/{id}
   - 🔲 Repository tests (file operations)

### 🔲 Remaining Tasks

9. **Additional Endpoints** (Week 5-6)
   - 🔲 GET /metadata (static capability statement)
   - 🔲 Error handling middleware
   - 🔲 FHIR response formatting

10. **Documentation** (Week 7)
    - 🔲 API documentation
    - 🔲 Setup/deployment guide
    - 🔲 Architecture decision records

11. **Prototype Demo** (Week 8)
    - 🔲 End-to-end demo: Create and retrieve Patient
    - 🔲 Performance baseline metrics
    - 🔲 Lessons learned document

### Next Immediate Steps

1. Configure Autofac in Program.cs
2. Register FileBasedFhirRepository with base directory
3. Register Medino mediator
4. Test PUT /Patient/123 → GET /Patient/123 round-trip

## Related Documentation

- **ADR-2500**: Master implementation roadmap (112 weeks, 26 investigations)
- **ADR-2501**: Prototype phase details (Weeks 1-8, file-based storage, Medino)
- **ADR-2502+**: Multi-tenancy, data partitioning investigations

## Future Roadmap

### Planned DataLayer Projects
- ✅ Sparky.DataLayer.FileSystem (Prototype)
- ✅ Sparky.DataLayer.InMemoryIndex (Prototype)
- 🔲 Sparky.DataLayer.SqlServer.Legacy (Phase 8 - EF with legacy schema)
- 🔲 Sparky.DataLayer.SqlServer.Optimized (Phase 8a - Optimized schema)
- 🔲 Sparky.DataLayer.CosmosDB (Phase 9)

### Next Steps (Post-Prototype)
1. **Autofac Configuration** - Register services, configure DI
2. **Startup/Program.cs** - Wire up controllers, Medino, repositories
3. **Integration Tests** - PUT /Patient/{id}, GET /Patient/{id}
4. **Metadata Endpoint** - Static capability statement
5. **Unit Tests** - 80% coverage target

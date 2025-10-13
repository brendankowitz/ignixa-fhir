# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a C# .NET 9.0 codebase for **FHIR Server v2** - a next-generation FHIR server implementation. The project implements a clean architecture with separate projects for each architectural layer, supporting multi-data-layer scenarios (Isolation vs Distributed modes).

## Current Status

**Phase**: Multi-Tenancy Data Partitioning (ADR-2523 Phase 20) - ✅ COMPLETED
**SDK Version**: Firely SDK 6.0.0-rc1 (unified multi-version support)
**Build Status**: ✅ All projects build successfully (0 warnings, 0 errors)
**Test Status**: ✅ All tests passing
**Endpoints**:
- ✅ PUT /tenant/{tenantId}/{resourceType}/{id} - Tenant-explicit (always)
- ✅ GET /tenant/{tenantId}/{resourceType}/{id} - Tenant-explicit (always)
- ✅ GET /tenant/{tenantId}/{resourceType} - Tenant-explicit search (always)
- ✅ POST /tenant/{tenantId}/ - Tenant-explicit bundles (always)
- ✅ PUT /{resourceType}/{id} - Tenant-agnostic (single-tenant auto-detect)
- ✅ GET /{resourceType}/{id} - Tenant-agnostic (single-tenant auto-detect)
- ✅ GET /{resourceType} - Tenant-agnostic search (single-tenant auto-detect)
- ✅ POST / - Tenant-agnostic bundles (single-tenant auto-detect)
- ✅ GET /metadata - No tenant required

### Recent Investigations (October 9, 2025)

Three new investigation documents completed to address architectural gaps:

1. **Dynamic FHIR Routing** (`docs/investigations/dynamic-fhir-routing.md`)
   - **Problem**: Current PatientController approach doesn't scale to 145+ FHIR resource types
   - **Solution**: Generic endpoint routing with RequestDelegate handlers
   - **Impact**: Zero controllers, automatic support for all resource types, 14% performance improvement
   - **Status**: Ready for Phase 1.1 implementation

2. **Bundle Streaming** (`docs/investigations/bundle-streaming.md`)
   - **Problem**: Current buffered Bundle responses load entire result set into memory (50 MB for 1000 resources)
   - **Solution**: IAsyncEnumerable + FhirJsonWriter streaming serialization
   - **Impact**: 95% memory reduction (50 MB → 2-3 MB), 50-200ms time-to-first-byte
   - **Status**: ✅ **ALREADY IMPLEMENTED** - BundleSerializer, FhirJsonWriter, streaming infrastructure complete

3. **Search Query Parsing** (`docs/investigations/search-query-parsing.md`)
   - **Problem**: Legacy SearchOptionsFactory is 800 lines of complex parameter parsing logic
   - **Solution**: Simplified 3-stage pipeline (QueryParameterParser → ExpressionBuilder → SearchOptionsBuilder)
   - **Impact**: 70% code reduction (800 → 250 lines), easier to maintain and extend
   - **Status**: Design complete, ready for Phase 1.2 implementation

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

### 5. Multi-Tenancy Architecture (ADR-2523 Phase 20)

#### Partition 0: System Partition
- **Partition 0 is RESERVED** for system operations (defined in `SystemConstants.SystemPartitionId`)
- All transaction IDs allocated from Partition 0 for global uniqueness across entire system
- Cannot be accessed via `/tenant/0/` API routes (middleware rejects with 400 Bad Request)
- Filtered from `GetAllTenantsAsync()` enumeration (marked with `IsSystemPartition = true`)
- Used internally by `DeferredWriteCoordinator` for transaction ID allocation

#### Multi-Tenant Routing

**Two Route Patterns Supported:**

1. **Tenant-Explicit Routes** (always supported):
   - Pattern: `/tenant/{tenantId:int}/{resourceType}/{id?}`
   - Used for: Multi-tenant scenarios, explicit tenant selection
   - Example: `GET /tenant/1/Patient/123` - Mayo Clinic

2. **Tenant-Agnostic Routes** (FHIR-compliant, auto-enabled for single-tenant):
   - Pattern: `/{resourceType}/{id?}`
   - Used for: Single-tenant deployments, standard FHIR clients
   - Example: `GET /Patient/123` - automatically uses the single configured tenant

**Routing Behavior:**

| Scenario | Tenant Count | Agnostic Routes (`/Patient/123`) | Explicit Routes (`/tenant/1/Patient/123`) |
|----------|--------------|----------------------------------|-------------------------------------------|
| **Single-Tenant** | 1 active tenant | ✅ Works (auto-detects tenant) | ✅ Works (explicit) |
| **Multi-Tenant** | 2+ active tenants | ❌ 400 Bad Request (ambiguous) | ✅ Works (required) |
| **Distributed Mode** (future) | N shards | ✅ Works (transparent sharding) | N/A (no tenant concept) |

**Middleware Logic** (`TenantResolutionMiddleware`):
- Extracts tenantId from route parameters OR auto-detects single tenant
- Single-tenant detection: Queries `ITenantConfigurationStore.GetAllTenantsAsync()` at startup
- Result cached per-process (avoids repeated queries)
- Multi-tenant scenarios: Agnostic routes blocked with helpful error message
- Partition 0 (system partition) always rejected from API access

**Examples:**
```bash
# Single-tenant deployment (only tenant 1 configured)
GET /Patient/123              # ✅ Works - auto-detects tenant 1
GET /tenant/1/Patient/123     # ✅ Works - explicit tenant 1
GET /metadata                 # ✅ Works - no tenant required

# Multi-tenant deployment (tenants 1, 2, 3, 4 configured)
GET /Patient/123              # ❌ 400 Bad Request - tenant ambiguous
GET /tenant/1/Patient/123     # ✅ Works - Mayo Clinic (R4)
GET /tenant/2/Patient/123     # ✅ Works - Cleveland Clinic (R4)
GET /tenant/3/Patient/123     # ✅ Works - Johns Hopkins (R4B)
GET /tenant/4/Patient/123     # ✅ Works - Stanford Health (R5)
GET /metadata                 # ✅ Works - no tenant required
```

**Benefits of Agnostic Routes:**
- ✅ FHIR-compliant standard URLs for single-tenant deployments
- ✅ Works with standard FHIR client libraries (no custom URL handling)
- ✅ Easy migration path: Deploy single-tenant, add tenants later without breaking existing URLs
- ✅ Zero breaking changes: Both route patterns coexist

#### Factory Pattern
- **IFhirRepositoryFactory**: Creates tenant-specific repository instances, caches per tenant
- **ISearchServiceFactory**: Creates tenant-specific search services, caches per tenant
- **Location**: `Sparky.DataLayer.FileSystem` project (moved from Application layer)
- **Caching**: `ConcurrentDictionary<int, IFhirRepository>` for O(1) lookup after first creation

#### Partition Strategy (HAPI FHIR-Inspired)
- **IPartitionStrategy**: Determines which partition(s) to read from / write to
  - `DetermineReadPartition()`: For searches (may return multiple partitions in future Distributed mode)
  - `DetermineWritePartition()`: For CRUD operations (always returns single partition)
- **IsolatedModePartitionStrategy**: Current implementation (single partition per tenant)
- **Future DistributedModePartitionStrategy**: Horizontal sharding with fanout/union (Phase 20.2+)

#### Bundle Processing with Multi-Tenancy
- **Tenant Context Propagation**: `BundleEntryExecutor` copies `TenantId` from parent HttpContext to bundle entry mini-HttpContext
- **Transaction ID Allocation**: `DeferredWriteCoordinator.CreateAsync()` allocates transaction ID from Partition 0
- **Partition-Aware Writes**: `ProcessBatchAsync()` groups operations by partition using `IPartitionStrategy`
- **Multi-Partition Commits**: Commits transaction across all touched partitions via `_touchedPartitions` tracking

#### Configuration Example
```json
{
  "Tenants": {
    "Mode": "Isolated",
    "Configurations": [
      {
        "TenantId": 0,
        "DisplayName": "System Partition (Reserved)",
        "IsSystemPartition": true,
        "Storage": { "Type": "FileSystem", "BaseDirectory": "system" }
      },
      {
        "TenantId": 1,
        "DisplayName": "Mayo Clinic (Example)",
        "FhirVersion": "4.0",
        "IsActive": true,
        "Storage": { "Type": "FileSystem", "BaseDirectory": "tenants/1" }
      }
    ]
  }
}
```

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

### Testing Standards

- **Test Framework**: xUnit
- **Test Naming Convention**: Use BDD-style naming with underscores separating Given/When/Then clauses
  - Format: `Given[Context]_When[Action]_Then[Result]`
  - Example: `GivenAPatientPoco_WhenConvertingToJsonNode_ThenMetaIsPopulated`
  - This naming style improves readability and clearly documents test intent
- **Test Organization**: Use `#region` blocks to group related tests (e.g., "GetReferences Tests", "UpdateReference Tests")
- **Arrange-Act-Assert Pattern**: Structure all test methods using the standard AAA pattern with comments

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

## Code Generation

### IStructureDefinitionSummaryProvider Generation

The project includes a build-time code generator for creating `IStructureDefinitionSummaryProvider` implementations for different FHIR versions (R4, R4B, R5, STU3). This ensures reliable, correct structure definitions from official FHIR packages.

**Location**: `codegen/` folder
**Solution**: `codegen/SparkyCodegen.sln` (separate from main All.sln)
**Output**: `src/Sparky.Specification/Generated/` folder

#### Architecture

```
codegen/
├── SparkyCodegen.sln                   # Separate solution for code generation
├── Sparky.Specification.Generators/    # Custom ILanguage implementation
│   ├── Program.cs                      # Console app entry point
│   └── CSharpStructureProviderLanguage.cs
├── fhir-codegen/                       # Git submodule (Microsoft fhir-codegen)
├── generate.ps1                        # PowerShell generation script
├── generate.sh                         # Bash generation script
├── Directory.Build.props               # Disables CPM for codegen
└── README.md                           # Code generation documentation
```

#### Why a Separate Solution?

The main `All.sln` uses Central Package Management (CPM), which conflicts with the fhir-codegen submodule's explicit package versions. By isolating code generation in `SparkyCodegen.sln`, we:

1. Keep the main solution simple and fast to build
2. Avoid CPM conflicts with third-party dependencies
3. Generate files on-demand rather than on every build
4. Make the build process more transparent

#### Usage

**Generate all FHIR versions:**

```bash
cd codegen
./generate.ps1        # PowerShell
./generate.sh         # Bash
```

**Generate specific version:**

```bash
./generate.ps1 -FhirVersion R4   # PowerShell
./generate.sh R4                 # Bash
```

Supported versions: `R4`, `R4B`, `R5`, `STU3`, `All`

#### Generated Files

Generated files are placed in `src/Sparky.Specification/Generated/`:
- `R4StructureDefinitionSummaryProvider.g.cs`
- `R4BStructureDefinitionSummaryProvider.g.cs`
- `R5StructureDefinitionSummaryProvider.g.cs`
- `STU3StructureDefinitionSummaryProvider.g.cs`

These files are marked as `linguist-generated=true` in `.gitattributes`.

#### How It Works

1. Scripts build both fhir-codegen and Sparky.Specification.Generators
2. fhir-codegen downloads and parses FHIR packages (e.g., `hl7.fhir.r4.core#4.0.1`)
3. fhir-codegen creates a `DefinitionCollection` with all FHIR structures
4. Our custom `CSharpStructureProviderLanguage` traverses the collection
5. Generated C# code is written to `src/Sparky.Specification/Generated/`

#### Key Classes

- **CSharpStructureProviderLanguage**: Implements `ILanguage` interface from fhir-codegen
- **CSharpStructureProviderConfig**: Configuration for output directory and namespace
- **Program.cs**: Console application orchestrating package loading and generation

#### Package Versions

The code generator uses Firely SDK 5.10.2 (from fhir-codegen submodule), **not** 6.0.0-rc1 used in the main solution. This is intentional to avoid API compatibility issues with fhir-codegen's LoaderOptions.

## Development Guidelines

### Key Files for Multi-Tenancy

When working with multi-tenant features, these files are critical:

**Domain Layer**:
- `Sparky.Domain/Constants/SystemConstants.cs` - Defines Partition 0 as system partition
- `Sparky.Domain/Models/TenantConfiguration.cs` - Tenant configuration model
- `Sparky.Domain/Models/TenantMode.cs` - Isolated vs Distributed mode enum
- `Sparky.Domain/Abstractions/ITenantConfigurationStore.cs` - Tenant config interface
- `Sparky.Domain/Abstractions/IFhirRepositoryFactory.cs` - Repository factory interface
- `Sparky.Domain/Abstractions/ISearchServiceFactory.cs` - Search service factory interface
- `Sparky.Domain/Abstractions/IPartitionStrategy.cs` - Partition determination strategy

**Application Layer**:
- `Sparky.Application/Infrastructure/AppSettingsTenantConfigurationStore.cs` - Loads tenants from appsettings.json

**Data Layer**:
- `Sparky.DataLayer.FileSystem/FileBasedFhirRepositoryFactory.cs` - Creates tenant-specific repositories
- `Sparky.DataLayer.FileSystem/FileBasedSearchServiceFactory.cs` - Creates tenant-specific search services
- `Sparky.DataLayer.FileSystem/IsolatedModePartitionStrategy.cs` - Isolation mode partition strategy

**API Layer**:
- `Sparky.Api/Middleware/TenantResolutionMiddleware.cs` - Extracts tenant from route, validates, protects Partition 0
- `Sparky.Api/appsettings.json` - Tenant configurations for production
- `Sparky.Api/appsettings.Development.json` - Multi-tenant test configuration

**Bundle Processing**:
- `Sparky.Application/Features/Bundle/DeferredWriteCoordinator.cs` - Allocates transaction IDs from Partition 0, groups writes by partition
- `Sparky.Application/Features/Bundle/BundleProcessor.cs` - Creates coordinators with partition strategy
- `Sparky.Application/Features/Bundle/BundleEntryExecutor.cs` - Propagates tenant context to mini-HttpContext

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

**Current Phase**: Prototype Implementation ✅ COMPLETED
**Status**: 🟢 All Core Features Implemented and Tested
**Completion Date**: October 9, 2025

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
   - ✅ MetadataController with GET /metadata
   - ✅ Feature folder organization

6. **SDK Migration** (Week 1)
   - ✅ Upgraded to Firely SDK 6.0.0-rc1
   - ✅ Fixed Sparky.Search nullable compatibility issues
   - ✅ Centralized package management

7. **Dependency Injection & Wiring** (Week 3-4)
   - ✅ Configured Autofac container with AutofacServiceProviderFactory
   - ✅ Registered IFhirRepository → FileBasedFhirRepository
   - ✅ Registered IMediatorServiceProvider → AutofacMediatorServiceProvider
   - ✅ Registered IMediator → Medino Mediator
   - ✅ Registered all Patient handlers
   - ✅ Configured logging with appsettings.json
   - ✅ Wired up Program.cs startup

8. **Testing** (Week 4-5)
   - ✅ Manual integration tests for PUT /Patient/{id}
   - ✅ Manual integration tests for GET /Patient/{id}
   - ✅ Verified end-to-end round-trip (create → read)
   - ✅ Repository file operations validated

9. **Additional Endpoints** (Week 5-6)
   - ✅ GET /metadata (capability statement)
   - ✅ Error handling middleware (FhirExceptionMiddleware)
   - ✅ FHIR response formatting (application/fhir+json)

10. **Build & Validation**
    - ✅ All 9 projects build successfully
    - ✅ All tests pass (1/1 passing)
    - ✅ Code analysis warnings resolved

### 🎉 Prototype Achievements

**Functional Endpoints:**
- ✅ `PUT /Patient/{id}` - Create or update Patient resource
- ✅ `GET /Patient/{id}` - Retrieve Patient resource by ID
- ✅ `GET /metadata` - Return capability statement

**Technical Stack:**
- ✅ ASP.NET Core 9.0 with Autofac DI
- ✅ Medino 2.0.1 for in-process messaging (CQRS pattern)
- ✅ Firely SDK 6.0.0-rc1 for FHIR support
- ✅ File-based storage with JSON + metadata sidecars
- ✅ FHIR-compliant error handling (OperationOutcome)

**Architecture Validated:**
- ✅ Clean separation of concerns (Domain → Application → API)
- ✅ Generic repository pattern (IFhirRepository)
- ✅ Feature folder structure
- ✅ Dependency injection with Autofac
- ✅ Medino CQRS handlers

### Testing Results

```bash
# Build Status
Build succeeded: 0 Warning(s), 0 Error(s)

# Test Results
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1

# Manual Integration Test
PUT /Patient/example-123 → 201 Created (version 1)
GET /Patient/example-123 → 200 OK (returns complete Patient resource)
GET /metadata → 200 OK (returns capability statement)
```

**Storage Verification:**
```
fhir-data/
└── Patient/
    ├── example-123.json       # Full FHIR Patient resource
    └── example-123.meta.json  # Metadata (version, lastModified)
```

11. **Phase 20: Multi-Tenancy Data Partitioning** (October 13, 2025)
    - ✅ TenantConfiguration model with IsSystemPartition property
    - ✅ ITenantConfigurationStore and AppSettingsTenantConfigurationStore
    - ✅ IFhirRepositoryFactory and FileBasedFhirRepositoryFactory (with caching)
    - ✅ ISearchServiceFactory and FileBasedSearchServiceFactory
    - ✅ IPartitionStrategy interface with IsolatedModePartitionStrategy
    - ✅ TenantResolutionMiddleware for tenant extraction and validation
    - ✅ Partition 0 system partition reservation (SystemConstants.SystemPartitionId)
    - ✅ Multi-partition bundle processing with DeferredWriteCoordinator
    - ✅ Tenant context propagation in BundleEntryExecutor
    - ✅ Updated all handlers to use factories and partition strategy
    - ✅ Multi-tenant routing: `/tenant/{tenantId}/{resourceType}/{id?}`

### Next Steps (Post-Phase 20)

The multi-tenancy foundation is **IN PROGRESS**. Remaining work:

1. **Phase 1.1: Bundle Processing & Dynamic Routing** (Week 2)
   - **NEW**: Migrate from PatientController to generic endpoint routing (see `dynamic-fhir-routing.md`)
     - Eliminates need for 145+ resource-specific controllers
     - Generic RequestDelegate handlers for all resource types
     - Zero controllers, automatic support for all FHIR resources
   - Implement POST / for transaction bundles
   - Channel-based parallel execution
   - Reference resolution for urn:uuid:

2. **Phase 1.2: Search Implementation** (Week 3)
   - **NEW**: Implement simplified SearchOptionsBuilder (see `search-query-parsing.md`)
     - 250 lines vs 800-line legacy factory (70% reduction)
     - QueryParameterParser for structured parsing
   - **NEW**: Implement streaming Bundle responses (see `bundle-streaming.md`)
     - IAsyncEnumerable + FhirJsonWriter for 95% memory reduction
     - BundleSerializer for zero-copy JSON serialization
   - Port InMemory search from microsoft/fhir-server
   - Add GET /Patient?name=... support
   - Integrate Sparky.Search indexing

2. **Phase 3: Additional Resource Types**
   - Add Observation, Condition, Medication, etc.
   - Reuse existing handlers (generic pattern)

3. **Phase 4: Production Hardening**
   - Add comprehensive unit tests (80% coverage)
   - Add integration test suite
   - Performance testing and optimization
   - Security hardening (authentication/authorization)

## Related Documentation

- **ADR-2500**: Master implementation roadmap (116 weeks, 29 investigations)
- **ADR-2501**: Prototype phase details (Weeks 1-8, file-based storage, Medino) - ✅ COMPLETED
- **ADR-2523**: Phase 20 - Multi-Tenancy Data Partitioning - IN PROGRESS
  - Isolation mode with factory pattern
  - Partition 0 system partition reservation
  - HAPI FHIR-inspired partition strategy
- **Investigation**: `docs/investigations/multi-tenancy-data-partitioning-modes.md`
- **Investigation**: `docs/investigations/dynamic-fhir-routing.md`
- **Investigation**: `docs/investigations/bundle-streaming.md`
- **Investigation**: `docs/investigations/search-query-parsing.md`

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

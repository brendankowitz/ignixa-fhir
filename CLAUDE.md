# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a C# .NET 9.0 codebase for **FHIR Server v2** - a next-generation FHIR server implementation. The project implements a clean architecture with separate projects for each architectural layer, supporting multi-data-layer scenarios (Isolation vs Distributed modes).

## Current Status

**Phase**: Transaction Watcher (Phase 21) - ✅ COMPLETED (October 16, 2025)
**Previous Phase**: Multi-Tenancy Data Partitioning (ADR-2523 Phase 20) - ✅ COMPLETED (October 13, 2025)
**SDK Version**: Firely SDK 6.0.0 final (October 14, 2025 - unified multi-version support)
**Build Status**: ✅ All projects build successfully (0 warnings, 0 errors)
**Test Status**: ✅ All tests passing
**Background Services**:
- ✅ IndexLoaderService - Search index preloading on startup
- ✅ TransactionWatcherService - Automatic stalled transaction recovery
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
├── 1. Ignixa.Domain              - Domain models and abstractions (no dependencies)
├── 2. Ignixa.Application         - Medino handlers and business logic (→ Domain)
├── 3. Ignixa.DataLayer.*         - Data storage implementations (→ Domain)
│   ├── Ignixa.DataLayer.FileSystem      - File-based repository (prototype)
│   └── Ignixa.DataLayer.InMemoryIndex   - Resource location tracking
├── 4. Ignixa.Api                 - ASP.NET Core API (→ all layers)
└── Supporting Libraries
    ├── Ignixa.Extensions         - FHIR extensions and utilities
    ├── Ignixa.Search             - Search functionality
    └── Ignixa.SourceNodeSerialization - Serialization utilities
```

### Project Details

#### 1. **Ignixa.Domain** (Domain Layer)
- **Purpose**: Core domain models and abstractions
- **Dependencies**: Hl7.Fhir.R4 only
- **Key Files**:
  - `Abstractions/IFhirRepository.cs` - Repository interface
  - `Models/ResourceKey.cs` - Resource identifier
  - `Models/ResourceWrapper.cs` - Resource + metadata container
  - `Models/ResourceRequest.cs` - HTTP request metadata
  - `Models/TransactionId.cs` - Transaction tracking

#### 2. **Ignixa.Application** (Application Layer)
- **Purpose**: Business logic and Medino message handlers
- **Dependencies**: Ignixa.Domain, Medino, Microsoft.Extensions.Logging.Abstractions
- **Pattern**: Feature folders (Features/Patient/)
- **Key Files**:
  - `Features/Patient/CreateOrUpdatePatientCommand.cs` - IRequest<ResourceKey>
  - `Features/Patient/CreateOrUpdatePatientHandler.cs` - IRequestHandler
  - `Features/Patient/GetPatientQuery.cs` - IRequest<ResourceWrapper?>
  - `Features/Patient/GetPatientHandler.cs` - IRequestHandler

#### 3. **Ignixa.DataLayer.FileSystem** (Data Layer)
- **Purpose**: File-based FHIR repository implementation (prototype)
- **Dependencies**: Ignixa.Domain, Hl7.Fhir.R4, Microsoft.Extensions.Logging.Abstractions
- **Storage Format**:
  - `{baseDir}/{resourceType}/{id}.json` - Resource JSON
  - `{baseDir}/{resourceType}/{id}.meta.json` - Metadata sidecar
- **Key Files**:
  - `FileSystem/FileBasedFhirRepository.cs` - IFhirRepository implementation

#### 4. **Ignixa.DataLayer.InMemoryIndex** (Data Layer)
- **Purpose**: Tracks which data layer(s) contain each resource (for Distributed mode)
- **Dependencies**: Ignixa.Domain
- **Key Files**:
  - `InMemoryIndex/IResourceLocationIndex.cs` - Interface
  - `InMemoryIndex/InMemoryResourceLocationIndex.cs` - ConcurrentDictionary implementation

#### 5. **Ignixa.Api** (API Layer)
- **Purpose**: ASP.NET Core Web API endpoints
- **Dependencies**: All layers (Domain, Application, DataLayer.*)
- **Pattern**: Feature folders (Features/Patient/Api/)
- **Key Files**:
  - `Features/Patient/Api/PatientController.cs` - GET /Patient/{id}, PUT /Patient/{id}
  - `Program.cs` - Application startup

#### Supporting Libraries

- **Ignixa.Extensions**: FHIR extensions, value sets, schema helpers
- **Ignixa.Search**: Search parameter definitions, indexing, search values
- **Ignixa.SourceNodeSerialization**: Custom serialization for FHIR ISourceNode

## Architecture Principles

### 1. Layer Separation
- **Domain** has no dependencies (pure models)
- **Application** depends only on Domain (business logic)
- **DataLayer** depends only on Domain (storage implementations)
- **API** depends on all layers (HTTP concerns)

**IMPORTANT**: Do NOT add Firely SDK (`Hl7.Fhir.R4`, `Hl7.Fhir.R4B`, `Hl7.Fhir.R5`, `Hl7.Fhir.STU3`) package references to ANY layer. The codebase uses custom implementations in `Ignixa.*` projects:
- **ITypedElement**: `Ignixa.SourceNodeSerialization.ElementModel.ITypedElement` (not SDK's)
- **FHIRPath**: `Ignixa.FhirPath.Evaluation` (not SDK's `Hl7.FhirPath`)
- **Schema**: `Ignixa.Specification` (custom generated providers)

Only projects that explicitly need SDK types (e.g., `Ignixa.Domain` for POCO models) should reference it. If you encounter a missing type error, use Ignixa's equivalents, not the SDK.

### 2. Feature Folders
- Organize by feature/capability (Patient, Observation, etc.)
- Each feature contains Api, Application, and optional Domain folders
- Example: `Features/Patient/Api/`, `Features/Patient/Application/`

### 3. Separate DataLayer Projects
- Each storage implementation is its own project
- Easy to add: Ignixa.DataLayer.SqlServer, Ignixa.DataLayer.CosmosDB, etc.
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
- **Location**: `Ignixa.DataLayer.FileSystem` project (moved from Application layer)
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

#### Transaction Watcher (Background Recovery Service)

**Purpose**: Automatically detects and commits stalled transactions across all active tenants and storage implementations (FileSystem and SQL).

**Architecture:**
- **TransactionWatcherService** (Sparky.Api/BackgroundServices/TransactionWatcherService.cs)
  - Implements `IHostedService` for background execution
  - Timer-based periodic scanning (configurable interval, default: 60 seconds)
  - Multi-tenant aware: Scans all active tenants via `ITenantConfigurationStore`
  - Multi-storage support: Routes to correct repository via `IFhirRepositoryFactory`
  - Excludes system partition (Partition 0) from API-level scans

**Storage Implementations:**

1. **FileSystem** (FileBasedFhirRepository.cs:324-397)
   - Scans `_transactions/**/*.lock.ndjson` files recursively
   - Checks file modification time vs configured threshold
   - Extracts transaction IDs from filenames (`tx-{id}.lock.ndjson`)
   - Returns list of stalled transaction IDs

2. **SQL** (LegacySqlEfRepository.cs:234-269)
   - Queries `TransactionEntity` table via EF Core
   - Filters: `WHERE IsCompleted = false AND HeartbeatDate < threshold`
   - Returns transaction IDs via LINQ query

**Configuration:**
```json
{
  "TransactionWatcher": {
    "Enabled": true,
    "ScanInterval": "00:01:00",     // Scan every 60 seconds
    "StallThreshold": "00:05:00"    // 5 minutes without commit = stalled
  }
}
```

**Workflow:**
1. **Service Starts**: On application startup, service registers timer
2. **Periodic Scan**: Every `ScanInterval`:
   - Queries all active tenants (excluding Partition 0)
   - For each tenant, gets repository (FileSystem or SQL based on tenant config)
   - Calls `GetStalledTransactionsAsync(StallThreshold)`
   - For each stalled transaction, calls `CommitTransactionAsync()`
3. **Logging**: Comprehensive metrics logging (scan duration, stalled count, commit success/failure)
4. **Error Handling**: Retries on next scan if commit fails (non-blocking)

**Key Files:**
- `Sparky.Domain/Abstractions/IFhirRepository.cs:68` - `GetStalledTransactionsAsync()` interface
- `Sparky.Domain/Models/TransactionId.cs:24` - `TryParse()` method for parsing transaction IDs
- `Sparky.Api/Configuration/TransactionWatcherOptions.cs` - Configuration model
- `Sparky.Api/BackgroundServices/TransactionWatcherService.cs` - Background service implementation
- `Sparky.DataLayer.FileSystem/FileSystem/FileBasedFhirRepository.cs:324` - FileSystem implementation
- `Sparky.DataLayer.LegacySqlEF/LegacySqlEfRepository.cs:234` - SQL implementation

**Benefits:**
- ✅ Automatic recovery from failed bundle operations (server crash, network timeout)
- ✅ Multi-tenant support with isolated transaction tracking per tenant
- ✅ Storage-agnostic design (works with FileSystem, SQL, future implementations)
- ✅ Configurable scan interval and stall threshold
- ✅ Non-blocking background execution (doesn't impact API performance)
- ✅ Comprehensive logging for observability

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
dotnet build src/Ignixa.Domain/Ignixa.Domain.csproj
dotnet build src/Ignixa.Application/Ignixa.Application.csproj
dotnet build src/Ignixa.DataLayer.FileSystem/Ignixa.DataLayer.FileSystem.csproj
dotnet build src/Ignixa.Api/Ignixa.Api.csproj
```

### Test
```bash
# Run all tests
dotnet test All.sln

# Run specific test project
dotnet test test/Ignixa.Api.Tests/Ignixa.Api.Tests.csproj
```

### Run API
```bash
dotnet run --project src/Ignixa.Api/Ignixa.Api.csproj
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

- **Firely SDK**: Hl7.Fhir.R4 (6.0.0) - Multi-version FHIR support
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
Embedded JSON files in `Ignixa.Search/Data/{Version}/`:
- `search-parameters.json` - FHIR search parameter definitions
- `unsupported-search-parameters.json` - Not implemented
- `BaseCapabilities.json` - Capability statement
- `compartment.json` - Compartment definitions
- `resourcepath-codesystem-mappings.json` - Code system mappings

## Code Generation

### IStructureDefinitionSummaryProvider Generation

The project includes a build-time code generator for creating `IStructureDefinitionSummaryProvider` implementations for different FHIR versions (R4, R4B, R5, STU3). This ensures reliable, correct structure definitions from official FHIR packages.

**Location**: `codegen/` folder
**Solution**: `codegen/IgnixaCodegen.sln` (separate from main All.sln)
**Output**: `src/Ignixa.Specification/Generated/` folder

#### Architecture

```
codegen/
├── IgnixaCodegen.sln                   # Separate solution for code generation
├── Ignixa.Specification.Generators/    # Custom ILanguage implementation
│   ├── Program.cs                      # Console app entry point
│   └── CSharpStructureProviderLanguage.cs
├── fhir-codegen/                       # Git submodule (Microsoft fhir-codegen)
├── generate.ps1                        # PowerShell generation script
├── generate.sh                         # Bash generation script
├── Directory.Build.props               # Disables CPM for codegen
└── README.md                           # Code generation documentation
```

#### Why a Separate Solution?

The main `All.sln` uses Central Package Management (CPM), which conflicts with the fhir-codegen submodule's explicit package versions. By isolating code generation in `IgnixaCodegen.sln`, we:

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

Generated files are placed in `src/Ignixa.Specification/Generated/`:
- `R4StructureDefinitionSummaryProvider.g.cs`
- `R4BStructureDefinitionSummaryProvider.g.cs`
- `R5StructureDefinitionSummaryProvider.g.cs`
- `STU3StructureDefinitionSummaryProvider.g.cs`

These files are marked as `linguist-generated=true` in `.gitattributes`.

#### How It Works

1. Scripts build both fhir-codegen and Ignixa.Specification.Generators
2. fhir-codegen downloads and parses FHIR packages (e.g., `hl7.fhir.r4.core#4.0.1`)
3. fhir-codegen creates a `DefinitionCollection` with all FHIR structures
4. Our custom `CSharpStructureProviderLanguage` traverses the collection
5. Generated C# code is written to `src/Ignixa.Specification/Generated/`

#### Key Classes

- **CSharpStructureProviderLanguage**: Implements `ILanguage` interface from fhir-codegen
- **CSharpStructureProviderConfig**: Configuration for output directory and namespace
- **Program.cs**: Console application orchestrating package loading and generation

#### Package Versions

The code generator uses Firely SDK 5.10.2 (from fhir-codegen submodule), **not** 6.0.0 used in the main solution. This is intentional to avoid API compatibility issues with fhir-codegen's LoaderOptions.

## Development Guidelines

### Key Files for Multi-Tenancy

When working with multi-tenant features, these files are critical:

**Domain Layer**:
- `Ignixa.Domain/Constants/SystemConstants.cs` - Defines Partition 0 as system partition
- `Ignixa.Domain/Models/TenantConfiguration.cs` - Tenant configuration model
- `Ignixa.Domain/Models/TenantMode.cs` - Isolated vs Distributed mode enum
- `Ignixa.Domain/Abstractions/ITenantConfigurationStore.cs` - Tenant config interface
- `Ignixa.Domain/Abstractions/IFhirRepositoryFactory.cs` - Repository factory interface
- `Ignixa.Domain/Abstractions/ISearchServiceFactory.cs` - Search service factory interface
- `Ignixa.Domain/Abstractions/IPartitionStrategy.cs` - Partition determination strategy

**Application Layer**:
- `Ignixa.Application/Infrastructure/AppSettingsTenantConfigurationStore.cs` - Loads tenants from appsettings.json

**Data Layer**:
- `Ignixa.DataLayer.FileSystem/FileBasedFhirRepositoryFactory.cs` - Creates tenant-specific repositories
- `Ignixa.DataLayer.FileSystem/FileBasedSearchServiceFactory.cs` - Creates tenant-specific search services
- `Ignixa.DataLayer.FileSystem/IsolatedModePartitionStrategy.cs` - Isolation mode partition strategy

**API Layer**:
- `Ignixa.Api/Middleware/TenantResolutionMiddleware.cs` - Extracts tenant from route, validates, protects Partition 0
- `Ignixa.Api/appsettings.json` - Tenant configurations for production
- `Ignixa.Api/appsettings.Development.json` - Multi-tenant test configuration

**Bundle Processing**:
- `Ignixa.Application/Features/Bundle/DeferredWriteCoordinator.cs` - Allocates transaction IDs from Partition 0, groups writes by partition
- `Ignixa.Application/Features/Bundle/BundleProcessor.cs` - Creates coordinators with partition strategy
- `Ignixa.Application/Features/Bundle/BundleEntryExecutor.cs` - Propagates tenant context to mini-HttpContext

**Transaction Watcher** (Background Recovery):
- `Sparky.Api/BackgroundServices/TransactionWatcherService.cs` - Background service for automatic transaction recovery
- `Sparky.Api/Configuration/TransactionWatcherOptions.cs` - Configuration model (ScanInterval, StallThreshold)
- `Sparky.Domain/Abstractions/IFhirRepository.cs:68` - `GetStalledTransactionsAsync()` interface method
- `Sparky.DataLayer.FileSystem/FileSystem/FileBasedFhirRepository.cs:324` - FileSystem stalled transaction detection
- `Sparky.DataLayer.LegacySqlEF/LegacySqlEfRepository.cs:234` - SQL stalled transaction detection

### Adding a New Feature (e.g., Observation)

1. **Application Layer** - Create handlers
   ```
   src/Ignixa.Application/Features/Observation/
   ├── CreateObservationCommand.cs
   ├── CreateObservationHandler.cs
   ├── GetObservationQuery.cs
   └── GetObservationHandler.cs
   ```

2. **API Layer** - Create controller
   ```
   src/Ignixa.Api/Features/Observation/Api/
   └── ObservationController.cs
   ```

3. **No changes needed** in Domain or DataLayer (already generic)

### Adding a New DataLayer Implementation (e.g., SQL Server)

1. **Create new project**
   ```bash
   dotnet new classlib -n Ignixa.DataLayer.SqlServer -o src/Ignixa.DataLayer.SqlServer
   dotnet add src/Ignixa.DataLayer.SqlServer reference src/Ignixa.Domain
   dotnet sln add src/Ignixa.DataLayer.SqlServer
   ```

2. **Implement IFhirRepository**
   ```csharp
   namespace Ignixa.DataLayer.SqlServer;

   public class SqlServerFhirRepository : IFhirRepository
   {
       // Implement GetAsync, CreateOrUpdateAsync
   }
   ```

3. **Register in Ignixa.Api** (Autofac/DI)

### SDK 6.0 API Patterns

```csharp
// Parsing JSON to ISourceNode
ISourceNode node = await FhirJsonNode.ParseAsync(jsonString);

// Serializing (prototype uses RawJson property for simplicity)
string json = resourceWrapper.RawJson; // Stored during read
```

## Known Issues / Workarounds

### 1. Ignixa.Search Nullable Compatibility
- **Issue**: Old code doesn't use nullable annotations
- **Workaround**: Nullable disabled (`<Nullable>disable</Nullable>`)
- **TODO**: Incrementally enable nullable and add annotations

### 2. Ignixa.Specification JsonSchema.Net
- **Issue**: API changed in version 7.x
- **Status**: Temporarily removed from solution
- **TODO**: Migrate to new JsonSchema.Net API or replace

### 3. FhirEvaluationContext.ElementResolver / PocoNode Custom Provider Limitation
- **Issue**: PocoNode/ToPocoNode doesn't support custom `IStructureDefinitionSummaryProvider` implementations
- **Root Cause**:
  - `ElementResolver` requires `PocoNode` return type (not `ITypedElement`)
  - `ToPocoNode()` accepts `ModelInspector` (concrete class), not `IStructureDefinitionSummaryProvider` (interface)
  - Our `R4StructureDefinitionSummaryProvider` cannot be converted to `ModelInspector`
- **Impact**: Custom provider metadata is discarded when converting `ITypedElement` → `PocoNode`
- **Status**: **Known SDK 6.0.0 architectural limitation** - cannot be resolved without SDK changes
- **Workaround**: Use `ToTypedElement()` with custom provider where possible (works for search indexing)
- **Location**: `TypedElementSearchIndexer.cs:71` (documented with detailed comments)

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
   - ✅ Ignixa.Domain - Models and abstractions
   - ✅ Ignixa.Application - Medino handlers
   - ✅ Ignixa.DataLayer.FileSystem - File-based repository
   - ✅ Ignixa.DataLayer.InMemoryIndex - Resource location tracking
   - ✅ Ignixa.Api - ASP.NET Core controllers

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

6. **SDK Migration** (Week 1, October 14, 2025)
   - ✅ Upgraded to Firely SDK 6.0.0-rc1 (August 2025)
   - ✅ Upgraded to Firely SDK 6.0.0 final (October 14, 2025)
   - ✅ Fixed Ignixa.Search nullable compatibility issues
   - ✅ Centralized package management
   - ✅ Resolved PocoNode/FhirPath issues with SDK 6.0.0 final

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
- ✅ Firely SDK 6.0.0 final for FHIR support
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

12. **Phase 21: Transaction Watcher (Background Recovery)** (October 16, 2025)
    - ✅ Added `GetStalledTransactionsAsync()` to IFhirRepository interface
    - ✅ Added `TryParse()` to TransactionId model
    - ✅ FileSystem stalled transaction detection (scans .lock.ndjson files)
    - ✅ SQL stalled transaction detection (queries TransactionEntity table)
    - ✅ TransactionWatcherOptions configuration model
    - ✅ TransactionWatcherService background service (IHostedService)
    - ✅ Multi-tenant and multi-storage support
    - ✅ Configurable scan interval and stall threshold
    - ✅ Comprehensive logging and metrics
    - ✅ Registered in DI and appsettings.json configuration
    - ✅ Build succeeded (0 warnings, 0 errors)

### Next Steps (Post-Phase 21)

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
   - Integrate Ignixa.Search indexing

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
- ✅ Ignixa.DataLayer.FileSystem (Prototype)
- ✅ Ignixa.DataLayer.InMemoryIndex (Prototype)
- 🔲 Ignixa.DataLayer.SqlServer.Legacy (Phase 8 - EF with legacy schema)
- 🔲 Ignixa.DataLayer.SqlServer.Optimized (Phase 8a - Optimized schema)
- 🔲 Ignixa.DataLayer.CosmosDB (Phase 9)

### Next Steps (Post-Prototype)
1. **Autofac Configuration** - Register services, configure DI
2. **Startup/Program.cs** - Wire up controllers, Medino, repositories
3. **Integration Tests** - PUT /Patient/{id}, GET /Patient/{id}
4. **Metadata Endpoint** - Static capability statement
5. **Unit Tests** - 80% coverage target

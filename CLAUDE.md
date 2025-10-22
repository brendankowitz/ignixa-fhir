# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Quick Links
- [Current Status](#current-status) - Phase 22 (FHIR _history) completed
- [Architecture Principles](#architecture-principles) - Layer separation, minimal API pattern
- [Multi-Tenancy](#multi-tenancy-architecture) - Partition 0, routing, factories
- [Multi-Agent System](#multi-agent-development-system) - When to use which agent
- [Common Commands](#common-commands) - Build, test, run

## Project Overview

**FHIR Server v2** - A next-generation FHIR server implementation built with C# .NET 9.0. Clean architecture with separate projects for each layer, supporting multi-tenant data partitioning (Isolation mode currently, Distributed mode planned).

## Current Status

| Category | Status |
|----------|--------|
| **Phase** | Phase 22: FHIR _history Operations ✅ COMPLETED (Oct 17, 2025) |
| **Build** | ✅ All projects build (0 warnings, 0 errors) |
| **Tests** | ✅ All tests passing |
| **SDK** | Firely SDK 6.0.0 final (multi-version R4/R4B/R5/STU3) |

### Active Background Services
- ✅ **IndexLoaderService** - Preloads search indexes on startup
- ✅ **TransactionWatcherService** - Automatic stalled transaction recovery

### Production Endpoints (Minimal API Pattern)

**Tenant-Explicit Routes** (Always Available):
- `PUT/GET /tenant/{tenantId}/{resourceType}/{id}` - CRUD operations
- `GET /tenant/{tenantId}/{resourceType}` - Search
- `POST /tenant/{tenantId}/` - Transaction bundles
- `PATCH /tenant/{tenantId}/{resourceType}/{id}` - FHIRPath Patch operations
- `GET /tenant/{tenantId}/{resourceType}/{id}/_history` - Instance history
- `GET /tenant/{tenantId}/{resourceType}/_history` - Type-level history
- `GET /tenant/{tenantId}/_history` - System-level history

**Tenant-Agnostic Routes** (Single-Tenant Auto-Detect):
- `PUT/GET /{resourceType}/{id}` - CRUD operations
- `GET /{resourceType}` - Search
- `POST /` - Transaction bundles
- `PATCH /{resourceType}/{id}` - FHIRPath Patch operations
- `GET /{resourceType}/{id}/_history` - Instance history
- `GET /{resourceType}/_history` - Type-level history
- `GET /_history` - System-level history
- `GET /metadata` - Capability statement (no tenant required)

### Recent Investigations

| Investigation | Status | Key Insight |
|---------------|--------|-------------|
| **Dynamic FHIR Routing** | Ready for Phase 1.1 | Generic endpoints eliminate need for 145+ controllers, 14% faster |
| **Bundle Streaming** | ✅ IMPLEMENTED | IAsyncEnumerable + FhirJsonWriter = 95% memory reduction (50 MB → 2-3 MB) |
| **Search Query Parsing** | Ready for Phase 1.2 | Simplified 3-stage pipeline reduces code 70% (800 → 250 lines) |

See `docs/investigations/*.md` for details.

## Solution Architecture

```
All.sln (10 projects)
├── 1. Ignixa.Domain              - Domain models, abstractions (no dependencies)
├── 2. Ignixa.Application         - Medino handlers, business logic (→ Domain)
├── 3. Ignixa.Application.BackgroundOperations - DurableTask orchestrations/activities (→ Application, Domain)
├── 4. Ignixa.DataLayer.*         - Storage implementations (→ Domain)
│   ├── Ignixa.DataLayer.FileSystem      - File-based repository
│   ├── Ignixa.DataLayer.InMemoryIndex   - Resource location tracking
│   ├── Ignixa.DataLayer.BlobStorage     - Blob storage for exports/imports
│   └── Ignixa.DataLayer.SqlEntityFramework     - SQL Server (EF Core)
├── 5. Ignixa.Api                 - ASP.NET Core minimal API (→ all layers)
└── Supporting Libraries
    ├── Ignixa.FhirPath           - FHIRPath evaluation engine
    ├── Ignixa.Search             - Search parameters, indexing
    ├── Ignixa.Specification      - Generated structure providers
    ├── Ignixa.SourceNodeSerialization - Custom FHIR serialization
    └── Ignixa.Validation         - FHIR resource validation
```

### Key Project Dependencies

| Project | Purpose | Dependencies |
|---------|---------|--------------|
| **Ignixa.Domain** | Core models (ResourceKey, ResourceWrapper, IFhirRepository) | Hl7.Fhir.R4 only |
| **Ignixa.Application** | Medino handlers (CreateOrUpdateHandler, GetHandler, SearchHandler) | Domain, Medino, Logging |
| **Ignixa.Application.BackgroundOperations** | DurableTask orchestrations/activities for $import/$export | Application, Domain, Search, DurableTask.Core |
| **Ignixa.DataLayer.\*** | Storage (FileSystem, SQL, BlobStorage, future CosmosDB) | Domain only |
| **Ignixa.Api** | Minimal API endpoints, middleware | All layers |

## Architecture Principles

### 1. Layer Separation (Strict Dependency Rules)

```
API Layer (HTTP concerns, endpoints)
    ↓ depends on
Application.BackgroundOperations Layer (DurableTask orchestrations/activities)
    ↓ depends on
Application Layer (Business logic, Medino handlers)
    ↓ depends on
Domain Layer (Pure models, interfaces)
    ← implemented by
DataLayer Projects (Storage implementations)
```

**CRITICAL**: Do NOT add Firely SDK packages (`Hl7.Fhir.*`) to Application/DataLayer projects. Use custom implementations:
- **ITypedElement**: `Ignixa.SourceNodeSerialization.ElementModel.ITypedElement` (not SDK's)
- **FHIRPath**: `Ignixa.FhirPath.Evaluation` (not `Hl7.FhirPath`)
- **Schema**: `Ignixa.Specification` (generated providers)

### 2. Feature Folders
Organize by capability, not by layer type:
```
Features/Patient/
├── Api/PatientController.cs (if using controllers)
├── CreateOrUpdatePatientCommand.cs
├── CreateOrUpdatePatientHandler.cs
├── GetPatientQuery.cs
└── GetPatientHandler.cs
```

### 3. Medino Messaging Pattern

**Commands/Queries** (Application Layer):
```csharp
// Record-based request (immutable)
public record GetPatientQuery(string Id) : IRequest<ResourceWrapper?>;

// Handler with HandleAsync method
public class GetPatientHandler : IRequestHandler<GetPatientQuery, ResourceWrapper?>
{
    public async Task<ResourceWrapper?> HandleAsync(
        GetPatientQuery request,
        CancellationToken cancellationToken) // NOT 'ct' (CA1725 compliance)
    {
        // Implementation
    }
}
```

**Key Rules**:
- Use `IRequest<TResponse>` (not ICommand)
- Use `IRequestHandler<TRequest, TResponse>`
- Method name: `HandleAsync` (not Handle)
- Parameter: `cancellationToken` (full name)
- Return: `Task<T>` (not ValueTask)

### 4. Minimal API Endpoints (NOT Controllers!)

**CRITICAL**: This codebase uses **Minimal API**, NOT MVC Controllers.

**Pattern** - Static extension methods with `Map*Endpoints()`:

```csharp
// File: Ignixa.Api/Infrastructure/FhirEndpoints.cs
public static class FhirEndpoints
{
    public static IEndpointRouteBuilder MapFhirEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Tenant-explicit route
        endpoints.MapGet("/tenant/{tenantId:int}/{resourceType}/{id}", HandleGetResource)
            .WithName("GetResource")
            .Produces<object>(StatusCodes.Status200OK, "application/fhir+json");

        // Tenant-agnostic route (delegates to explicit handler)
        endpoints.MapGet("/{resourceType}/{id}", (HttpContext ctx, string resourceType, string id,
            [FromServices] IMediator mediator, CancellationToken ct) =>
            HandleGetResource(ctx, ExtractTenantId(ctx), resourceType, id, mediator, ct));

        return endpoints;
    }

    private static async Task<IResult> HandleGetResource(
        HttpContext context, int tenantId, string resourceType, string id,
        IMediator mediator, CancellationToken ct)
    {
        var query = new GetResourceQuery(tenantId, resourceType, id);
        var result = await mediator.SendAsync(query, ct);
        return result != null ? Results.Ok(result) : Results.NotFound();
    }
}

// Program.cs registration
app.MapFhirEndpoints();
app.MapFhirHistoryEndpoints();
```

**Why Minimal API vs Controllers?**
- ✅ 14% faster (no controller overhead)
- ✅ Composable (easy to mix tenant-explicit/agnostic routes)
- ✅ Explicit route definitions (not scattered via attributes)
- ✅ Modern .NET 9 best practice

**When Adding Endpoints**:
1. Create `*Endpoints.cs` in `Ignixa.Api/Infrastructure/`
2. Add `Map*Endpoints()` extension method
3. Define handlers as `private static async Task<IResult>`
4. Register in `Program.cs`: `app.Map*Endpoints()`
5. Register Medino handlers in Autofac

**Example Files**:
- `Ignixa.Api/Infrastructure/FhirEndpoints.cs` - CRUD for all resource types
- `Ignixa.Api/Infrastructure/HistoryEndpoints.cs` - _history operations
- `Ignixa.Api/Features/Export/Api/ExportEndpoints.cs` - Bulk $export

## Multi-Tenancy Architecture

### Partition 0: System Partition (Reserved)

**Critical Rules**:
- Partition 0 (`SystemConstants.SystemPartitionId`) is RESERVED for system operations
- All transaction IDs allocated from Partition 0 (global uniqueness)
- Cannot be accessed via `/tenant/0/` routes (middleware rejects with 400)
- Filtered from `GetAllTenantsAsync()` (marked `IsSystemPartition = true`)

### Multi-Tenant Routing Behavior

| Scenario | Tenant Count | Agnostic Routes (`/Patient/123`) | Explicit Routes (`/tenant/1/Patient/123`) |
|----------|--------------|----------------------------------|-------------------------------------------|
| **Single-Tenant** | 1 active | ✅ Auto-detects tenant | ✅ Works |
| **Multi-Tenant** | 2+ active | ❌ 400 Bad Request (ambiguous) | ✅ Required |
| **Distributed** (future) | N shards | ✅ Transparent sharding | N/A (no tenant concept) |

**Middleware**: `TenantResolutionMiddleware` extracts `tenantId` from route OR auto-detects single tenant, caches result per-process.

**Example**:
```bash
# Single-tenant (only tenant 1 configured)
GET /Patient/123              # ✅ Auto-detects tenant 1
GET /tenant/1/Patient/123     # ✅ Explicit

# Multi-tenant (tenants 1, 2, 3 configured)
GET /Patient/123              # ❌ 400 Bad Request - ambiguous
GET /tenant/1/Patient/123     # ✅ Mayo Clinic (R4)
GET /tenant/2/Patient/123     # ✅ Cleveland Clinic (R4B)
```

### Factory Pattern (Tenant-Scoped Dependencies)

**Interfaces** (Domain Layer):
- `IFhirRepositoryFactory` - Creates tenant-specific repositories (cached)
- `ISearchServiceFactory` - Creates tenant-specific search services (cached)
- `IPartitionStrategy` - Determines read/write partitions

**Implementations** (DataLayer):
- `FileBasedFhirRepositoryFactory` - Uses `ConcurrentDictionary<int, IFhirRepository>`
- `IsolatedModePartitionStrategy` - Single partition per tenant (current)
- Future: `DistributedModePartitionStrategy` - Horizontal sharding with fanout

**Configuration** (`appsettings.json`):
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
        "DisplayName": "Mayo Clinic",
        "FhirVersion": "4.0",
        "IsActive": true,
        "Storage": { "Type": "FileSystem", "BaseDirectory": "tenants/1" }
      }
    ]
  }
}
```

### Key Multi-Tenancy Files

| Layer | File | Purpose |
|-------|------|---------|
| **Domain** | `Constants/SystemConstants.cs` | Defines Partition 0 |
| | `Models/TenantConfiguration.cs` | Tenant config model |
| | `Abstractions/IFhirRepositoryFactory.cs` | Repository factory interface |
| | `Abstractions/IPartitionStrategy.cs` | Partition determination |
| **Application** | `Infrastructure/AppSettingsTenantConfigurationStore.cs` | Loads tenants from config |
| | `Features/Bundle/DeferredWriteCoordinator.cs` | Allocates transaction IDs from Partition 0 |
| **DataLayer** | `FileBasedFhirRepositoryFactory.cs` | Creates tenant repositories |
| | `IsolatedModePartitionStrategy.cs` | Isolation mode strategy |
| **API** | `Middleware/TenantResolutionMiddleware.cs` | Extracts/validates tenant, protects Partition 0 |

### Transaction Watcher (Background Recovery)

**Purpose**: Automatically detects and commits stalled transactions across all tenants and storage types.

**Architecture**:
- `TransactionWatcherService` (IHostedService) - Timer-based periodic scanning
- Multi-tenant aware via `ITenantConfigurationStore`
- Multi-storage support (FileSystem, SQL) via `IFhirRepositoryFactory`
- Scans every 60 seconds (default), commits transactions stalled >5 minutes

**Storage Implementations**:
- **FileSystem**: Scans `_transactions/**/*.lock.ndjson` files, checks modification time
- **SQL**: Queries `TransactionEntity WHERE IsCompleted = false AND HeartbeatDate < threshold`

**Configuration**:
```json
{
  "TransactionWatcher": {
    "Enabled": true,
    "ScanInterval": "00:01:00",
    "StallThreshold": "00:05:00"
  }
}
```

### History Bundle Total Count Pattern

**Default**: NO total count (optimal streaming performance).

**FHIR `_total` Parameter**:

| Value | Behavior | Performance |
|-------|----------|-------------|
| **none** (default) | No total in Bundle | ✅ Fast - single streaming query |
| **estimate** (future) | Estimated total | ⚠️ Medium - cheap estimate |
| **accurate** | Exact count | ❌ Slow - full enumeration |

**Why?** Calculating totals requires enumerating ALL results (defeats streaming, doubles query time).

**Implementation**:
```csharp
// Default: No total
GET /Patient/123/_history?_count=20
// Response: No "total" field, includes "next" link

// Accurate: Separate count query
GET /Patient/123/_history?_count=20&_total=accurate
// Response: "total": 157, includes "last" link
```

## Common Commands

```bash
# Build
dotnet build All.sln

# Test
dotnet test All.sln

# Run API
dotnet run --project src/Ignixa.Api/Ignixa.Api.csproj

# Code Generation (Structure Providers)
cd codegen
./generate.ps1        # PowerShell (Windows)
./generate.sh         # Bash (Linux/Mac)
```

## Git Workflow

**IMPORTANT**: ⛔ **DO NOT commit changes without explicit user permission**

Claude Code must always ask for user approval before creating git commits:
- Present a summary of changes and proposed commit message
- Wait for user approval/rejection
- Only proceed with `git commit` after user explicitly confirms
- This applies to all commits, including squash commits or amends
- Exception: Only automatically commit if user explicitly requests automation in their workflow

**Pattern**:
```
1. Make changes and test them
2. Present git diff, status, and proposed commit message
3. Ask: "Should I commit these changes with message: [message]?"
4. Wait for user response
5. Execute `git commit` only upon approval
```

## Code Standards

**File Organization**:
- ❌ **ONE class/interface/enum per file** (strict rule)
- ✅ File name must match type name exactly (e.g., `ValidationSchema.cs` contains only `ValidationSchema` class)
- ❌ Never bundle multiple types in a single file (even if closely related)
- ✅ Use separate files even for small types (interfaces, enums, records)

**StyleCop/Analysis**:
- 4 spaces, no tabs
- System usings first, outside namespace
- Nullable reference types enabled (Domain, Application, DataLayer, Api)
- Warnings as errors (specific suppressions for SA/CA rules)

**Testing**:
- xUnit framework
- BDD naming: `Given[Context]_When[Action]_Then[Result]`
- Example: `GivenAPatientPoco_WhenConvertingToJsonNode_ThenMetaIsPopulated`
- AAA pattern (Arrange-Act-Assert)
- Group with `#region` blocks

## Copyright Headers

**Policy**: Copyright headers reflect code origin, with clarity about degree of derivation:

| Origin | Header | Examples | Notes |
|--------|--------|----------|-------|
| **Microsoft FHIR Server** (True Derivation) | `// Copyright (c) Microsoft Corporation. All rights reserved.` | `IFhirRepository.cs`, `ISearchService.cs`, FHIR exceptions | Core abstractions with minimal modifications |
| **Microsoft FHIR Server** (Architectural Pattern) | `// Copyright (c) Microsoft Corporation. All rights reserved.` | `FhirEndpoints.cs`, `*Handler.cs`, `BundleProcessor.cs` | Substantially rewritten implementations based on MS patterns |
| **Firely SDK Libraries** | `// Copyright (c) 2015-2023, Firely (info@fire.ly) and contributors` | `ISourceNode.cs`, `ITypedElement.cs`, `IAnnotated.cs` | Derived interface definitions |
| **Ignixa Contributors** | `// Copyright (c) Ignixa Contributors. All rights reserved.` | `FhirPatchEngine.cs`, `FhirPathEvaluator.cs`, streaming code | Newly created implementations |

**Understanding the Microsoft Headers**:
Most files with Microsoft copyright were inspired by or adapted from Microsoft FHIR Server architectural patterns, but have been substantially rewritten for Ignixa's needs:
- **Truly Derived** (~5-10 files): Core abstractions (IFhirRepository, ISearchService, exception base classes)
- **Substantially Rewritten** (~30-50 files): Handlers, endpoints, bundle processing (migrated to Minimal API, streaming, Medino)
- **New Implementations** (~400+ files): Everything else is newly created or generated

See `LICENSE` file section "Microsoft FHIR Server - Architectural Inspiration" for detailed breakdown.

**When Creating New Files**:
1. Copying from Microsoft FHIR Server? Keep Microsoft copyright and note modifications in file header
2. Using Firely SDK code? Keep Firely copyright and cite source in header
3. Brand new Ignixa implementation? Use Ignixa copyright
4. Majority Ignixa but inspired by Microsoft pattern? Use Ignixa copyright with optional note

**Do NOT**:
- ❌ Add headers to generated files (`obj/`, `bin/`, `.g.cs`)
- ❌ Add headers to test projects (inherit from source they test)
- ❌ Add headers to `.csproj` or configuration files
- ❌ Over-attribute to Microsoft for code that's substantially different

**Quick Audit**:
```bash
# Count files by header
grep -l "Copyright (c) Microsoft" src/ -r --include="*.cs" | wc -l    # ~250
grep -l "Firely" src/ -r --include="*.cs" | wc -l                     # ~11
find src/ -name "*.cs" ! -path "*/obj/*" | wc -l                      # ~492 total
# Note: Microsoft headers are used conservatively for architectural attribution
```

## Key Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Hl7.Fhir.R4 | 6.0.0 | Multi-version FHIR support (R4/R4B/R5/STU3) |
| Medino | 2.0.1 | In-process messaging (CQRS) |
| Autofac | 8.2.0 | Dependency injection |
| Microsoft.IO.RecyclableMemoryStream | 3.0.1 | Memory optimization |

**SDK 6.0 Changes**:
- Unified packages (no separate .Core/.Specification)
- `FhirJsonNode.ParseAsync()` for parsing
- Nullable context enabled

## FHIR Support

**Versions**: R4 (primary), R4B, R5, STU3
**Search Parameters**: Embedded JSON in `Ignixa.Search/Data/{Version}/`
- `search-parameters.json` - Supported parameters
- `unsupported-search-parameters.json` - Not implemented
- `BaseCapabilities.json` - Capability statement

## Multi-Agent Development System

This project uses a **hierarchical multi-agent architecture**:

```
fhir-coordinator (Orchestrator - Sonnet)
    ├─→ fhir-agent (FHIR Spec Research - Sonnet)
    ├─→ coding-agent (Complex Implementation - Sonnet)
    └─→ fast-coding-agent (Quick Tasks - Haiku 3.5) ⚡
```

### Agent Roles

| Agent | Model | Tools | Use Case |
|-------|-------|-------|----------|
| **fhir-coordinator** | Sonnet | Task, Read, Write, Edit, Grep, Glob | Orchestrate workflows, manage ADRs, delegate tasks |
| **fhir-agent** | Sonnet | WebFetch, Read, Grep, Glob (NO Write) | Research FHIR specs, create ADR-ready docs |
| **coding-agent** | Sonnet | All tools | Multi-file features, architecture changes, complex refactoring |
| **fast-coding-agent** | Haiku 3.5 | Read, Write, Edit, Bash | Single-file edits, simple refactoring, build fixes |

### When to Use Each Agent

| Task | Agent | Reason |
|------|-------|--------|
| Research FHIR spec | fhir-agent | Spec expertise, WebFetch |
| Coordinate complex feature | fhir-coordinator | Orchestration |
| Add simple parameter | fast-coding-agent | Speed, single file |
| Multi-file feature | coding-agent | Complexity |
| Update ADRs | fhir-coordinator | Documentation management |

### Example Workflows

**Simple Task**:
```
@fhir-coordinator "Add _count parameter support"
1. Checks ADR-2501 for context
2. Spawns fhir-agent: Research _count spec
3. Updates ADR with requirements
4. Spawns fast-coding-agent: Add ParseCount() method
5. Verifies build
```

**Complex Feature**:
```
@fhir-coordinator "Implement FHIR Subscriptions"
1. Reads ADR-2500 roadmap
2. Spawns fhir-agent: Research Subscriptions spec
3. Creates ADR-2530 with findings
4. Spawns coding-agent: Implement subscription engine
5. Updates ADR with status
```

**Direct Invocation** (skip coordinator when task is clear):
```bash
@fast-coding-agent "Fix CS0103 error in SearchHandler.cs"
@coding-agent "Refactor BundleSerializer to extract helpers"
@fhir-agent "Research FHIR _total parameter specification"
```

**ADR Integration**: Coordinator maintains `docs/investigations/ADR-*.md` as single source of truth (spec → ADR → code lineage).

**Agent Config Files**: `.claude/agents/*.md`

## Common Patterns Cheat Sheet

### PATCH Operations (FHIRPath Patch)

**Pattern**: FHIRPath expressions with Parameters resource

```http
PATCH /Patient/123
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "operation",
    "part": [
      { "name": "type", "valueCode": "replace" },
      { "name": "path", "valueString": "Patient.name.where(use='official').family" },
      { "name": "value", "valueString": "NewLastName" }
    ]
  }]
}
```

**Supported Operations**:
- **add** - Add element to collection: `{ type: "add", path: "Patient.telecom", value: {...} }`
- **insert** - Insert at index: `{ type: "insert", path: "Patient.name", index: 0, value: {...} }`
- **delete** - Remove element: `{ type: "delete", path: "Patient.telecom[1]" }`
- **replace** - Replace value: `{ type: "replace", path: "Patient.gender", value: "female" }`
- **move** - Move element: `{ type: "move", source: "Patient.name[1]", destination: "Patient.name[0]" }`

**FHIRPath Support**:
```csharp
// Simple paths
"Patient.gender" → Replace primitive value

// Array indexing
"Patient.name[0].family" → Replace array element property

// Complex FHIRPath expressions (NEW!)
"Patient.name.where(use='official').family" → Uses where() function
"Patient.telecom.where(system='phone').first()" → Uses where() + first()
"Patient.address.where(use='home').city" → Filter + property access
```

**Architecture**:
```
FHIRPath Expression → FhirPathEvaluator → ITypedElement
                                              ↓
                                    IAnnotated.Annotation<JsonNode>()
                                              ↓
                                        Mutate in-place
```

**Immutable Properties** (Protected):
- ❌ Cannot PATCH: `id`, `meta.versionId`, `meta.lastUpdated`
- ✅ Use PUT to change resource ID
- ✅ Server auto-manages versionId and lastUpdated

**Implementation Files**:
- `Ignixa.Application/Features/Patch/FhirPatchEngine.cs` - Orchestrator (strategy pattern)
- `Ignixa.Application/Features/Patch/Executors/IOperationExecutor.cs` - Strategy interface
- `Ignixa.Application/Features/Patch/Executors/*OperationExecutor.cs` - 5 executors
- `Ignixa.Application/Features/Patch/FhirPathPatchHelper.cs` - FHIRPath evaluation with IAnnotated
- `Ignixa.Application/Features/Patch/Validation/*Validator.cs` - Validators
- `Ignixa.Api/Infrastructure/PatchEndpoints.cs` - Minimal API routes

**Key Pattern - IAnnotated for JsonNode Extraction**:
```csharp
// Evaluate FHIRPath expression
var matches = _fhirPathEvaluator.Evaluate(typedElement, expression);

// Extract JsonNode using IAnnotated (part of resource tree)
var jsonNode = (matches.First() as IAnnotated)?.Annotation<JsonNode>();

// Mutate in-place (no serialization roundtrip needed)
if (jsonNode.Parent is JsonObject parentObj)
{
    parentObj[propertyName] = newValue; // Direct mutation
}
```

### FHIR Validation System

**Three-Tier Architecture** (ADR-2527):

| Tier | Performance | Checks | Blocking |
|------|-------------|--------|----------|
| **Fast** | <25ms | JSON structure, required fields | Yes (API) |
| **Spec** | <200ms | + Cardinality, types, FHIRPath | Yes (API) |
| **Profile** | <1000ms | + Profiles, slicing, terminology | No ($validate) |

**Key Files**:
- `Ignixa.Validation/FastValidator.cs` - Tier 1 validator
- `Ignixa.Validation/Checks/*.cs` - Validation checks (JsonStructure, RequiredField, Cardinality, Type)
- `Ignixa.Validation/Abstractions/IValidationCheck.cs` - Check interface
- `Ignixa.Validation/README.md` - Usage guide

**Pattern**: All checks implement `IValidationCheck` and use `ISourceNode` for FHIR-aware navigation.

**HAPI Compatibility**: Validation results use `OperationOutcomeJsonNode` with HAPI-compatible error codes (bdl-*, ele-*, etc.).

**Example**:
```csharp
var validator = new FastValidator();
var result = validator.Validate(jsonNode);
if (!result.IsValid) {
    var outcome = result.ToOperationOutcome(); // HAPI-compatible
    throw new ValidationException(result);
}
```

**Custom Checks**:
```csharp
var checks = new List<IValidationCheck>
{
    new RequiredFieldCheck("id", isRequired: true),
    new CardinalityCheck("name", min: 1, max: null) // 1..*
};
var result = validator.Validate(sourceNode, checks);
```

**Legacy Code**:
- `Ignixa.Validation/SourceNodeValidation/FastPathValidator.cs` - DEPRECATED (marked [Obsolete]), use FastValidator instead

### Adding a New Minimal API Endpoint

```csharp
// 1. Create Endpoints class
public static class MyEndpoints
{
    public static IEndpointRouteBuilder MapMyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/my-route", HandleMyRequest);
        return endpoints;
    }

    private static async Task<IResult> HandleMyRequest(
        HttpContext context, IMediator mediator, CancellationToken ct)
    {
        var query = new MyQuery();
        var result = await mediator.SendAsync(query, ct);
        return Results.Ok(result);
    }
}

// 2. Register in Program.cs
app.MapMyEndpoints();
```

### Adding a New Medino Handler

```csharp
// Query/Command
public record MyQuery(string Id) : IRequest<MyResult>;

// Handler
public class MyHandler : IRequestHandler<MyQuery, MyResult>
{
    private readonly IFhirRepository _repository;

    public MyHandler(IFhirRepository repository) => _repository = repository;

    public async Task<MyResult> HandleAsync(MyQuery request, CancellationToken cancellationToken)
    {
        // Implementation
    }
}

// Register in Program.cs (Autofac)
builder.RegisterType<MyHandler>().As<IRequestHandler<MyQuery, MyResult>>();
```

### Adding a New DataLayer Implementation

```csharp
// 1. Create project
dotnet new classlib -n Ignixa.DataLayer.MyStorage -o src/Ignixa.DataLayer.MyStorage
dotnet add src/Ignixa.DataLayer.MyStorage reference src/Ignixa.Domain
dotnet sln add src/Ignixa.DataLayer.MyStorage

// 2. Implement IFhirRepository
public class MyStorageFhirRepository : IFhirRepository
{
    // Implement GetAsync, CreateOrUpdateAsync, GetResourceHistoryAsync, etc.
}

// 3. Register factory in Program.cs
```

### Working with ResourceJsonNode (JsonObject-Based Architecture)

**Pattern**: ResourceJsonNode uses `System.Text.Json.Nodes.JsonObject` as single source of truth for in-place mutation.

```csharp
// Creating a resource
var patient = new ResourceJsonNode
{
    ResourceType = "Patient",
    Id = "example-123",
};

// Accessing the mutable JsonObject via MutableNode property
patient.MutableNode["active"] = JsonSerializer.SerializeToNode(true);
patient.MutableNode["birthDate"] = JsonValue.Create("1990-01-15");

// Adding complex objects
patient.MutableNode["name"] = JsonNode.Parse(@"[{
    ""family"": ""Doe"",
    ""given"": [""John""]
}]");

// Working with Meta extensions
patient.Meta.LastUpdated = DateTimeOffset.UtcNow;
patient.Meta.VersionId = "1";
patient.Meta.RemoveExtension("http://example.com/extension-url");

// Reading values
if (patient.MutableNode.TryGetPropertyValue("active", out var activeNode))
{
    bool isActive = activeNode.GetValue<bool>();
}

// Serializing to JSON string
string json = patient.SerializeToString();
```

**Key Classes**:
- `BaseJsonNode` - Abstract base class with `MutableNode` property
- `ResourceJsonNode` - FHIR resource with ResourceType, Id, Meta
- `MetaJsonNode` - FHIR meta element with VersionId, LastUpdated
- `ParametersJsonNode` - FHIR Parameters resource
- `ParameterJsonNode` - Individual parameter with flexible GetValue/SetValue methods

**JsonNode vs JsonElement**:
| Aspect | JsonNode (Current) | JsonElement (Old) |
|--------|-------------------|-------------------|
| **Mutability** | ✅ Mutable in-place | ❌ Immutable (requires cloning) |
| **API** | Modern System.Text.Json.Nodes | Legacy System.Text.Json |
| **Performance** | No serialization roundtrips | Serialization required for mutations |
| **Pattern** | `MutableNode` property | `ExtensionData` dictionary |

**FHIR-Aware Navigation**:
- `JsonNodeSourceNode` implements ISourceNode with FHIR-specific logic
- Shadow property pairing (e.g., `birthDate` + `_birthDate`)
- Extension handling in shadow properties
- Content vs value distinction for primitives
- Choice type suffix support (`value*` matches `valueString`, `valueCode`, etc.)

**Test Pattern**:
```csharp
// Before (ExtensionData - DEPRECATED)
var resource = new ResourceJsonNode
{
    ResourceType = "Patient",
    ExtensionData = new Dictionary<string, JsonElement>
    {
        ["active"] = JsonSerializer.SerializeToElement(true),
    },
};

// After (MutableNode - CORRECT)
var resource = new ResourceJsonNode
{
    ResourceType = "Patient",
};
resource.MutableNode["active"] = JsonSerializer.SerializeToNode(true);
```

## Anti-Patterns to Avoid

| ❌ Don't | ✅ Do |
|---------|-------|
| Add `Hl7.Fhir.*` to Application/DataLayer | Use `Ignixa.*` equivalents |
| Use MVC Controllers | Use Minimal API endpoints |
| Use `ICommand` | Use `IRequest<TResponse>` |
| Return `ValueTask<T>` from handlers | Return `Task<T>` |
| Name parameter `ct` | Name parameter `cancellationToken` |
| Calculate total count by default | Use `_total=none` (default), only count when requested |
| Allow access to Partition 0 via API | Block in middleware (system partition only) |
| Use `ExtensionData` with `JsonElement` | Use `MutableNode` property with `JsonNode` |
| Call `GetMutableNode()` method | Use `MutableNode` property |
| Use `JsonSerializer.SerializeToElement()` | Use `JsonSerializer.SerializeToNode()` |
| Use `JsonDocument.Parse()` | Use `JsonNode.Parse()` |

## Known Issues

1. **Ignixa.Search Nullable**: Nullable disabled (`<Nullable>disable</Nullable>`). TODO: Incrementally enable.
2. **PocoNode Custom Provider**: SDK 6.0 limitation - `ToPocoNode()` doesn't accept custom `IStructureDefinitionSummaryProvider`. Workaround: Use `ToTypedElement()` where possible.
3. **ISourceNode Serialization**: Store `RawJson` in `ResourceWrapper` for prototype. Production: Use `FhirJsonSerializer`.

## Implementation Progress Summary

| Phase | Status | Completion Date |
|-------|--------|-----------------|
| Phases 1-10 (Prototype) | ✅ COMPLETED | Oct 9, 2025 |
| Phase 20 (Multi-Tenancy) | ✅ COMPLETED | Oct 13, 2025 |
| Phase 21 (Transaction Watcher) | ✅ COMPLETED | Oct 16, 2025 |
| Phase 22 (FHIR _history) | ✅ COMPLETED | Oct 17, 2025 |

**Current Capabilities**:
- ✅ CRUD operations (all resource types via generic endpoints)
- ✅ Search (basic implementation)
- ✅ Transaction bundles (POST /)
- ✅ **PATCH operations (FHIRPath Patch with full expression support)**
- ✅ Multi-tenant data partitioning (Isolation mode)
- ✅ Background transaction recovery
- ✅ FHIR _history operations (instance/type/system)
- ✅ Streaming Bundle responses (95% memory reduction)
- ✅ Tenant-explicit and tenant-agnostic routing

**Next Phases**:
1. **Phase 1.1**: Dynamic routing (eliminate PatientController), enhanced bundle processing
2. **Phase 1.2**: Advanced search (simplified SearchOptionsBuilder, 70% code reduction)
3. **Phase 3**: Additional resource types (Observation, Condition, Medication)
4. **Phase 4**: Production hardening (80% test coverage, performance optimization, security)

## Related Documentation

**Key ADRs**:
- `docs/investigations/ADR-2500-master-roadmap.md` - 116-week roadmap
- `docs/investigations/ADR-2501-prototype-phase.md` - Prototype details (COMPLETED)
- `docs/investigations/ADR-2523-multi-tenancy.md` - Multi-tenant design

**Investigations**:
- `docs/investigations/dynamic-fhir-routing.md` - Generic endpoints (14% faster)
- `docs/investigations/bundle-streaming.md` - Memory optimization (IMPLEMENTED)
- `docs/investigations/search-query-parsing.md` - Simplified parser (70% code reduction)

**Code Generation**:
- `codegen/README.md` - Structure provider generation
- `codegen/generate.ps1` / `generate.sh` - Generation scripts

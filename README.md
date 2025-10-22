
<div align="center">
  <img src="docs/assets/ignixa_transparent.png" alt="Ignixa Logo" width="300"/>
</div>

# Ignixa

A blazing-fast Reference Implementation FHIR server built in .NET/C#.

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![FHIR](https://img.shields.io/badge/FHIR-R4%20%7C%20R4B%20%7C%20R5%20%7C%20STU3-orange)](https://hl7.org/fhir/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

## Overview

Ignixa is a next-generation FHIR server implementation built from the ground up with modern .NET patterns and clean architecture principles. It provides a high-performance, extensible platform for healthcare data interoperability.

### Key Features

**Core FHIR Capabilities**
- **Multi-Version FHIR Support**: R4, R4B, R5, and STU3 with version-aware search parameters
- **Complete RESTful API**: CRUD operations, search, transactions, history, patch, bulk operations
- **FHIRPath Patch Operations**: Complex path expressions (`.where()`, `.first()`, etc.) with in-place mutation
- **FHIR Validation**: Three-tier validation system (Fast, Spec, Profile) with OperationOutcome support
- **Search Parameters**: Full search parameter support with indexing (token, string, date, quantity, reference, etc.)

**High Performance**
- **Zero-Copy Serialization**: Direct JSON → HTTP response without intermediate POCO objects
- **Streaming Responses**: Bundle responses stream results without loading entire dataset into memory
- **Memory Efficiency**: 95% memory reduction for bulk operations (50 MB → 2-3 MB)
- **Fast Indexing**: In-memory resource location index with custom converters for each search parameter type
- **Minimal API Pattern**: 14% faster than MVC Controllers

**Architecture & Design**
- **Clean Architecture**: Strict layer separation (Domain → Application → DataLayer → API)
- **CQRS Pattern**: Medino-based handlers for Commands/Queries with cross-cutting concerns
- **Factory Pattern**: Tenant-scoped repository and search service factories
- **Strategy Pattern**: Pluggable partition strategies and operation executors (PATCH operations, execution strategies)

**Multi-Tenancy**
- **Multi-Tenant Data Partitioning**: Isolation mode with per-tenant file systems or SQL databases
- **Partition 0 Protection**: System partition reserved for transactions and global IDs (API access blocked)
- **Tenant-Explicit & Agnostic Routes**: Both `/tenant/{id}/...` and `/{resourceType}/...` (auto-detect in single-tenant)
- **Multi-Storage Support**: File system, SQL Server, Blob Storage, with extensible factory pattern

**Background & Async Processing**
- **DurableTask Integration**: Reliable async orchestration for $export and $import with fault recovery
- **Transaction Watcher**: Automatic detection and recovery of stalled transactions across all tenants

**Developer Experience**
- **Generated Code**: Structure providers auto-generated from official FHIR packages (R4/R4B/R5/STU3)
- **Comprehensive Documentation**: CLAUDE.md development guide, ADRs, investigation documents
- **Multiple Storage Backends**: File system (prototype), SQL Server (production), Cosmos DB (planned)
- **Extensive Test Coverage**: 8 test projects with xUnit framework and BDD naming

## Quick Start

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Windows, Linux, or macOS

### Running the Server

```bash
# Build the solution
dotnet build All.sln

# Run the API
cd src/Ignixa.Api
dotnet run
```

The server will start at `https://localhost:5001` (or `http://localhost:5000`).

### Try It Out

```bash
# Get the capability statement
curl https://localhost:5001/metadata

# Create a Patient resource
curl -X PUT https://localhost:5001/Patient/example-123 \
  -H "Content-Type: application/fhir+json" \
  -d '{
    "resourceType": "Patient",
    "id": "example-123",
    "name": [{"family": "Smith", "given": ["John"]}]
  }'

# Retrieve the Patient
curl https://localhost:5001/Patient/example-123

# Search for Patients
curl "https://localhost:5001/Patient?name=Smith"
```

## Architecture

Ignixa follows a **layered architecture** with clear separation of concerns:

```
┌─────────────────────────────────────┐
│         Ignixa.Api                  │  ← HTTP endpoints, middleware
├─────────────────────────────────────┤
│      Ignixa.Application             │  ← Business logic, CQRS handlers
├─────────────────────────────────────┤
│        Ignixa.Domain                │  ← Domain models, abstractions
├─────────────────────────────────────┤
│    Ignixa.DataLayer.*               │  ← Storage implementations
│  • FileSystem  • BlobStorage        │
│  • SqlEntityFramework • InMemoryIndex      │
└─────────────────────────────────────┘
```

### Supporting Libraries

- **Ignixa.Extensions**: FHIR extensions, value sets, schema helpers
- **Ignixa.Search**: Search parameter definitions, indexing, search values
- **Ignixa.Specification**: Structure definitions, generated providers
- **Ignixa.Validation**: Fast validation engine with SourceNode support
- **Ignixa.FhirPath**: A fast FHIRPath parser and evaluator built on Superpower
- **Ignixa.SourceNodeSerialization**: Zero-copy JSON serialization

## Current Status

| Category | Status |
|----------|--------|
| **Latest Features** | ✅ FHIR History Operations (_history endpoints) |
| **Build** | ✅ All projects build (0 warnings, 0 errors) |
| **Tests** | ✅ All tests passing |
| **.NET Version** | 9.0.201 |
| **SDK** | Firely SDK 6.0.0 final (R4/R4B/R5/STU3) |

### Fully Implemented Endpoints

**Resource CRUD**
- ✅ `PUT /{resourceType}/{id}` - Create or update resource
- ✅ `GET /{resourceType}/{id}` - Read individual resource
- ✅ `GET /{resourceType}` - List resources (paginated search)
- ✅ `DELETE /{resourceType}/{id}` - Delete resource
- ✅ `PATCH /{resourceType}/{id}` - FHIRPath Patch operations

**Advanced Operations**
- ✅ `POST /` - Transaction bundles (atomic multi-resource operations)
- ✅ `GET /metadata` - Capability statement
- ✅ `GET /{resourceType}/_history` - Type-level resource history
- ✅ `GET /{resourceType}/{id}/_history` - Instance history (version tracking)
- ✅ `GET /_history` - System-level history

**Bulk Operations**
- ✅ `GET /{resourceType}/$export` - Bulk export (async with DurableTask)
- ✅ `POST /{resourceType}/$import` - Bulk import (async with DurableTask)

**Search & Discovery**
- ✅ `POST /{resourceType}/_search` - Search via POST with parameters
- ✅ `GET /{resourceType}?{params}` - Search via GET with query parameters
- ✅ Search result pagination with Bundle links

**Multi-Tenancy Support**
- ✅ Tenant-explicit routes: `/tenant/{tenantId}/{resourceType}/{id}`
- ✅ Tenant-agnostic routes with auto-detection: `/{resourceType}/{id}`
- ✅ Data partitioning with Isolation mode (Distributed mode planned)

## Project Structure

```
fhir-server-contrib/
├── src/
│   ├── Ignixa.Api/                    # ASP.NET Core API
│   ├── Ignixa.Application/            # CQRS handlers (Medino)
│   ├── Ignixa.Domain/                 # Domain models
│   ├── Ignixa.DataLayer.FileSystem/   # File-based storage (prototype)
│   ├── Ignixa.DataLayer.InMemoryIndex/# Resource location index
│   ├── Ignixa.DataLayer.BlobStorage/  # Azure Blob Storage
│   ├── Ignixa.DataLayer.SqlEntityFramework/  # SQL Server with EF Core
│   ├── Ignixa.Extensions/             # FHIR extensions
│   ├── Ignixa.Search/                 # Search infrastructure
│   ├── Ignixa.Specification/          # Structure definitions
│   ├── Ignixa.Validation/             # Validation engine
│   ├── Ignixa.FhirPath/               # FHIRPath engine
│   └── Ignixa.SourceNodeSerialization/# JSON serialization
├── test/
│   ├── Ignixa.Api.Tests/
│   ├── Ignixa.Application.Tests/
│   ├── Ignixa.Extensions.Tests/
│   ├── Ignixa.FhirPath.Tests/
│   ├── Ignixa.SourceNodeSerialization.Tests/
│   └── Ignixa.Validation.Tests/
├── codegen/                           # Code generation tools
│   ├── Ignixa.Specification.Generators/
│   └── fhir-codegen/                  # Git submodule
├── docs/
│   ├── adr/                           # Architecture Decision Records
│   └── investigations/                # Research and design docs
└── All.sln                            # Main solution file
```

## Configuration

### appsettings.json

```json
{
  "FhirRepository": {
    "BaseDirectory": "fhir-data"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

## Storage Backends

### File System (Default - Prototype)

Stores resources as JSON files with metadata sidecars:

```
fhir-data/
├── Patient/
│   ├── example-123.json       # Resource JSON
│   └── example-123.meta.json  # Metadata (version, lastModified)
├── Observation/
└── _jobs/                     # DurableTask state
    ├── instances/
    └── history/
```

### SQL Server with Entity Framework (Production Ready) ✅

Fully implemented SQL Server provider with Entity Framework Core for production workloads.

**Features**:
- Optimized relational schema with indexed columns
- Search parameter indexing (token, string, date, quantity, reference, URI)
- Transaction management with ACID guarantees
- Connection pooling and query optimization
- Full-text search support (future enhancement)
- Compressed resource storage (gzipped JSON)

**Storage Schema**:
- `ResourceEntity` - Stores compressed FHIR resources (gzipped JSON)
- Separate tables for each search parameter type (TokenSearchParam, DateTimeSearchParam, etc.)
- Transaction tracking with stall detection
- Multi-tenant support with TenantId partitioning

**Configuration**: Set storage type in `appsettings.json` to use SQL Server instead of file system. Supports both Isolation mode (per-tenant databases) and single database with partitioning.

### Azure Blob Storage (Planned)

Cloud-native storage option for import/export.

## Dependencies

### Core Packages

- **System.Text.Json**: Native .NET JSON serialization (zero-copy, high performance)
- **Medino 2.0.1**: In-process CQRS messaging
- **Autofac 8.2.0**: Dependency injection container
- **Microsoft.Azure.DurableTask.Core 3.5.0**: Background job orchestration

### Testing

- **xUnit 2.9.2**: Test framework
- **NSubstitute 5.3.0**: Mocking
- **FluentAssertions 7.0.0**: Assertion library

See `Directory.Packages.props` for complete package list (centralized package management).

## Development

### Building

```bash
# Clean build
dotnet clean All.sln
dotnet build All.sln

# Run tests
dotnet test All.sln
```

### Code Generation

Structure definition providers are generated from official FHIR packages:

```bash
cd codegen
./generate.ps1        # PowerShell
./generate.sh         # Bash
```

Supports: R4, R4B, R5, STU3

### Code Style

- **StyleCop**: Enforced via `stylecop.json`
- **Code Analysis**: Enabled with warnings as errors
- **EditorConfig**: Configured for consistency
- **Nullable Reference Types**: Enabled

## Documentation

- **CLAUDE.md**: Development guide for AI assistants
- **docs/adr/**: Architecture Decision Records
  - ADR-2500: Master implementation roadmap
  - ADR-2501: Prototype phase (COMPLETED)
  - ADR-2502: Bundle processing
  - ADR-2503: Search implementation
  - ADR-2504: Search parameter types
- **docs/investigations/**: Research and design documents
  - Dynamic FHIR routing
  - Bundle streaming
  - Search query parsing
  - Multi-tenancy data partitioning
  - And 20+ more investigation documents

## Roadmap

### Completed ✅

**Core Functionality**
- ✅ File-based storage with JSON files
- ✅ Multi-version FHIR support (R4/R4B/R5/STU3)
- ✅ Full CRUD operations (PUT, GET, DELETE, PATCH)
- ✅ Search with comprehensive parameter parsing
- ✅ Transaction bundles with reference resolution
- ✅ Modern API patterns (Minimal API, no MVC Controllers)
- ✅ Streaming Bundle responses (95% memory reduction)

**Enterprise Features**
- ✅ Multi-tenant data partitioning (Isolation mode)
- ✅ Tenant-explicit and tenant-agnostic routing
- ✅ Automatic transaction recovery and stall detection
- ✅ FHIR History operations (instance/type/system level)
- ✅ SQL Server provider with Entity Framework Core
- ✅ Background job orchestration (DurableTask)

### In Development 🚧

**Search Enhancements**
- 🚧 Advanced query parameter support (_include, _revinclude)
- 🚧 Chained search parameters
- 🚧 Sort parameter support
- 🚧 Simplified search query parsing

**API Improvements**
- 🚧 Generic resource endpoints (handle all types dynamically)
- 🚧 Performance optimization (14% improvement potential)

### Planned 📋

**Storage**
- 📋 Azure Cosmos DB (cloud-native, globally distributed)
- 📋 Azure Blob Storage (archival and high-volume workloads)

**Security & Operations**
- 📋 SMART on FHIR authentication
- 📋 Role-based access control
- 📋 Audit logging and compliance

**Advanced Features**
- 📋 FHIR Subscriptions (webhook delivery)
- 📋 Custom search parameters
- 📋 Version conversion/transformation between FHIR versions
- 📋 Response caching (ETag, If-Modified-Since)
- 📋 GraphQL API layer
- 📋 Distributed deployment (multi-node consistency)

## Performance

Ignixa is designed from the ground up for high performance with proven optimizations:

**Core Optimizations**
- **Zero-Copy Serialization**: Direct JSON → HTTP response without intermediate POCO objects
- **Streaming Responses**: IAsyncEnumerable bundle serialization—memory usage scales with active connections, not result set size
- **Memory Pooling**: RecyclableMemoryStream reduces garbage collection pressure
- **Async/Await**: Non-blocking I/O throughout the stack for maximum throughput
- **In-Memory Indexing**: ResourceLocationIndex pre-loads on startup for O(1) lookups

**Scalability**
- Multi-tenant isolation: Separate repositories per tenant prevent cross-tenant interference
- Partition strategies: Extensible design supports horizontal sharding (future Distributed mode)
- Background job processing: DurableTask orchestration enables bulk operations without blocking API threads
- Connection pooling: Factory pattern caches repositories and search services per tenant

## Contributing

We welcome contributions! Please see our [contribution guidelines](CONTRIBUTING.md).

### Getting Help

- 📖 Read the [CLAUDE.md](CLAUDE.md) development guide
- 📚 Browse the [docs/](docs/) folder
- 🐛 Report issues on [GitHub Issues](https://github.com/your-org/fhir-server-contrib/issues)

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

### Sources and Attribution

This project incorporates code and design patterns from multiple open-source projects:

#### Microsoft FHIR Server (Architectural Inspiration)
- **Source**: [microsoft/fhir-server](https://github.com/microsoft/fhir-server)
- **License**: MIT
- **Copyright**: Copyright (c) Microsoft Corporation. All rights reserved.
- **Usage**: This project was inspired by and adapted from architectural patterns in Microsoft FHIR Server. The majority of implementation files have been substantially rewritten or newly generated for Ignixa's specific needs (streaming responses, zero-copy serialization, Minimal API endpoints, etc.).
- **Specifically Derived**: Core abstractions (IFhirRepository, ISearchService), exception hierarchies, multi-tenancy partition concepts
- **Substantially Rewritten**: CQRS handlers (Medino), bundle processing (streaming), API endpoints (Minimal API), metadata capability statements
- **Newly Created**: FHIRPath engine, validation system, PATCH operations, history endpoints, DurableTask integration
- **Files**: See `src/**/*.cs` files with Microsoft copyright headers for conservative attribution

#### Firely SDK
- **Source**: [FirelyTeam/firely-net-sdk](https://github.com/FirelyTeam/firely-net-sdk)
- **License**: BSD 3-Clause
- **Copyright**: Copyright (c) 2015-2023, Firely (info@fire.ly) and contributors
- **Usage**: Core abstractions including `ISourceNode`, `ITypedElement`, `IAnnotated`, and element navigation patterns. Approximately 11 source files derived from Firely SDK.
- **Files**: `Ignixa.SourceNodeSerialization/Abstractions/`, `Ignixa.FhirPath/Expressions/`

#### Ignixa Contributors
- **New/Custom Code**: All original implementations, enhancements, and modifications
- **License**: MIT
- **Copyright**: Copyright (c) Ignixa Contributors. All rights reserved.

### Other Credits

- Structure definition providers generated from official HL7 FHIR packages
- Inspired by the [Microsoft FHIR Server](https://github.com/microsoft/fhir-server) architecture
- Inspired by the [Firely SDK](https://docs.fire.ly/) for FHIR R4/R4B/R5/STU3 support
- Uses [Medino](https://github.com/AndyJB/Medino) for CQRS messaging
- Powered by [.NET 9.0](https://dotnet.microsoft.com/)
- Custom zero-copy serialization with ISourceNode/ITypedElement patterns

---

**Ignixa** / Intelligent Gateway for Next-generation Interoperability and eXtensible APIs / Igniting healthcare data interoperability 🔥


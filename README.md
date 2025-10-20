
<div align="center">
  <img src="docs/assets/ignixa_transparent.png" alt="Ignixa Logo" width="300"/>
</div>

# Ignixa

A blazing-fast FHIR server built in .NET/C# that ignites your healthcare data exchange.

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![FHIR](https://img.shields.io/badge/FHIR-R4%20%7C%20R4B%20%7C%20R5%20%7C%20STU3-orange)](https://hl7.org/fhir/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

## Overview

Ignixa is a next-generation FHIR server implementation built from the ground up with modern .NET patterns and clean architecture principles. It provides a high-performance, extensible platform for healthcare data interoperability.

### Key Features

- **Multi-Version FHIR Support**: R4, R4B, R5, and STU3
- **Clean Architecture**: Separated domain, application, and infrastructure layers
- **Multiple Storage Backends**: File system, SQL Server, Cosmos DB, Azure Blob Storage
- **High Performance**: Zero-copy serialization, streaming responses, memory-efficient operations
- **Multi-Tenancy**: Built-in support for multi-tenant deployments with data partitioning
- **Async Processing**: Background jobs with DurableTask for $export and bulk operations
- **Modern Patterns**: CQRS with Medino, dependency injection with Autofac, endpoint routing

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

**Phase**: Prototype Implementation ✅ COMPLETED (ADR-2501)
**Architecture**: Custom serialization with zero Firely SDK dependencies
**Build Status**: ✅ Building successfully
**Test Status**: ✅ Tests passing

### Implemented Endpoints

- ✅ `GET /metadata` - Capability statement
- ✅ `PUT /{resourceType}/{id}` - Create or update resource
- ✅ `GET /{resourceType}/{id}` - Read resource
- ✅ `GET /{resourceType}?{params}` - Search resources
- ✅ `POST /{resourceType}/_search` - Search via POST
- ✅ `GET /{resourceType}/_history` - Resource history
- ✅ `POST /` - Transaction bundles
- ✅ `GET /{resourceType}/$export` - Bulk export (async with DurableTask)

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

### SQL Server (Coming Soon)

Entity Framework Core implementation with optimized schema.

### Azure Blob Storage (Coming Soon)

Cloud-native storage with partitioning support.

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

- ✅ Phase 1 (Prototype): File-based storage, basic CRUD, search
- ✅ Multi-version FHIR support (R4/R4B/R5/STU3)
- ✅ Transaction bundles with reference resolution
- ✅ Async bulk export with DurableTask
- ✅ Dynamic endpoint routing (zero controllers)
- ✅ Streaming Bundle responses
- ✅ Fast validation engine

### In Progress 🚧

- 🚧 SQL Server provider with optimized schema
- 🚧 Advanced search features (_include, _revinclude, chaining)
- 🚧 Multi-tenancy with data partitioning

### Planned 📋

- 📋 Azure Cosmos DB provider
- 📋 SMART on FHIR authentication
- 📋 Subscriptions with webhook delivery
- 📋 Custom search parameters
- 📋 FHIR version conversion
- 📋 Performance optimization (caching, indexing)

See `docs/adr/adr-2500-master-implementation-roadmap.md` for the complete 112-week plan.

## Performance

Ignixa is designed for high performance:

- **Zero-Copy Serialization**: Direct JSON → ISourceNode without intermediate POCOs
- **Streaming Responses**: Memory usage scales with connection count, not result set size
- **Async Everywhere**: Non-blocking I/O for maximum throughput
- **Memory Pooling**: Recyclable memory streams reduce GC pressure
- **Efficient Indexing**: In-memory indexes for fast lookups

**Benchmarks** (coming soon):
- Bundle streaming: 95% memory reduction (50 MB → 2-3 MB)
- Validation: 15-60ms per resource
- Search: Sub-100ms for typical queries

## Contributing

We welcome contributions! Please see our [contribution guidelines](CONTRIBUTING.md).

### Getting Help

- 📖 Read the [CLAUDE.md](CLAUDE.md) development guide
- 📚 Browse the [docs/](docs/) folder
- 🐛 Report issues on [GitHub Issues](https://github.com/your-org/fhir-server-contrib/issues)

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Built on the [Firely SDK](https://docs.fire.ly/) for FHIR R4/R4B/R5/STU3 support
- Structure definition providers generated from official FHIR packages using custom codegen
- Inspired by the [Microsoft FHIR Server](https://github.com/microsoft/fhir-server)
- Uses [Medino](https://github.com/AndyJB/Medino) for CQRS messaging
- Powered by [.NET 9.0](https://dotnet.microsoft.com/)
- Custom zero-copy serialization with ISourceNode/ITypedElement patterns

---

**Ignixa** / Intelligent Gateway for Next-generation Interoperability and eXtensible APIs / Igniting healthcare data interoperability 🔥


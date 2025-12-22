---
sidebar_position: 1
title: Overview
description: Ignixa FHIR Server architecture and capabilities
---

# Server Overview

**Ignixa** is a modular, high-performance FHIR ecosystem built on **.NET**.
It serves as both a **Reference Server** and a suite of **Standalone Tools** offering a robust foundation for healthcare data interoperability.

Designed for the cloud, Ignixa supports multi-tenancy out of the box, with data isolation and configurable storage backends (currently SQL Server) and bulk operations supporting Azure Blob Storage.

## Key Features

### Multi-Version FHIR Support

Ignixa supports multiple FHIR versions simultaneously:

| Version | Status | Notes |
|---------|--------|-------|
| **R4** | ✅ Full Support | Primary version |
| **R4B** | ✅ Full Support | R4 with extensions |
| **R5** | ✅ Full Support | Latest normative |
| **R6** | 🚧 Preview | Ballot 2 support |
| **STU3** | ✅ Legacy Support | For backward compatibility |

### High Performance

- **Streaming-First Architecture** - Zero-copy serialization minimizes memory footprint
- **Minimal API** - Built on ASP.NET Core Minimal APIs for lowest overhead
- **Compiled FHIRPath** - Expression caching and compilation for fast evaluation

### Enterprise Features

- **Multi-Tenancy** - Physical data isolation between tenants
- **Bulk Operations** - `$export` and `$import` with DurableTask framework
- **Three-Tier Validation** - Fast, Spec, and Profile validation levels

## Architecture

Ignixa follows Clean Architecture with strict layer separation:

```
┌─────────────────────────────────────────────┐
│                 API Layer                    │
│         (Minimal API Endpoints)              │
├─────────────────────────────────────────────┤
│              Application Layer               │
│         (CQRS Handlers, Business Logic)      │
├─────────────────────────────────────────────┤
│               Domain Layer                   │
│       (Interfaces, Models, Contracts)        │
├─────────────────────────────────────────────┤
│               DataLayer                      │
│     (SQL Server, FileSystem, BlobStorage)    │
└─────────────────────────────────────────────┘
```

### Project Structure

```
src/
├── Application/
│   ├── Ignixa.Api/                 # HTTP endpoints
│   ├── Ignixa.Application/         # CQRS handlers
│   ├── Ignixa.Domain/              # Domain models
│   └── Ignixa.Application.Operations/
├── Core/                           # Reusable SDK packages
│   ├── Ignixa.Abstractions/
│   ├── Ignixa.Serialization/
│   ├── Ignixa.FhirPath/
│   └── ...
└── DataLayer/
    ├── Ignixa.DataLayer.SqlEntityFramework/
    ├── Ignixa.DataLayer.FileSystem/
    └── Ignixa.DataLayer.BlobStorage/
```

## Supported Operations

### REST API

| Operation | Endpoint | Description |
|-----------|----------|-------------|
| **Read** | `GET /{type}/{id}` | Retrieve a resource |
| **Create** | `POST /{type}` | Create a new resource |
| **Update** | `PUT /{type}/{id}` | Replace a resource |
| **Patch** | `PATCH /{type}/{id}` | Partial update |
| **Delete** | `DELETE /{type}/{id}` | Remove a resource |
| **Search** | `GET /{type}?params` | Search resources |
| **History** | `GET /{type}/{id}/_history` | Version history |
| **Capabilities** | `GET /metadata` | CapabilityStatement |

### Bundle Operations

- **Batch** - Independent operations in a single request
- **Transaction** - ACID-compliant transactional bundles

### Extended Operations

| Operation | Description |
|-----------|-------------|
| `$validate` | Validate a resource against profiles |
| `$export` | Bulk data export (async) |
| `$import` | Bulk data import (async) |
| `$member-match` | Patient matching |

## Storage Options

Ignixa supports multiple storage backends:

| Provider | Use Case | Features |
|----------|----------|----------|
| **SQL Server** | Production | Full ACID, advanced indexing |
| **File System** | Development | Zero setup, rapid prototyping |
| **Blob Storage** | Archival | Scalable, cost-effective |

## Getting Started

1. [Installation](/docs/getting-started/installation) - Deploy the server
2. [Quick Start](/docs/getting-started/quick-start) - First FHIR requests
3. [Configuration](/docs/server/configuration) - Customize settings

## Related Documentation

- [Architecture Details](/docs/server/architecture)
- [Multi-Tenancy](/docs/server/multi-tenancy)
- [FHIR Compliance](/docs/server/fhir/capability-statement)

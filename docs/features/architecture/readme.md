# Feature: Architecture

Core architectural patterns and design decisions for the FHIR server implementation.

## Overview

This feature area contains investigations related to the fundamental architecture of the FHIR server, including multi-version support, memory efficiency, routing, and core abstractions.

## Investigations

| Investigation | Status | Description |
|---------------|--------|-------------|
| [v2-architecture](investigations/v2-architecture.md) | Viable | Multi-tenant multi-version FHIR server architecture |
| [core-shims](investigations/core-shims.md) | Viable | Core abstractions with Firely SDK interop shims |
| [jsonobject-based](investigations/jsonobject-based.md) | Complete | JsonObject-based architecture for resource manipulation |
| [memory-efficient-patterns](investigations/memory-efficient-patterns.md) | Viable | Memory-efficient FHIR resource handling patterns |
| [request-context](investigations/request-context.md) | Viable | FHIR request context pattern for multi-tenant operations |
| [distributed-messaging](investigations/distributed-messaging.md) | Viable | Distributed messaging architecture |
| [capability-statement](investigations/capability-statement.md) | Viable | Dynamic capability statement generation |
| [dynamic-routing](investigations/dynamic-routing.md) | Viable | Dynamic FHIR routing for multi-version support |
| [interface-enhancements](investigations/interface-enhancements.md) | Viable | Interface enhancements for proper implementation |
| [legacy-type-migration](investigations/legacy-type-migration.md) | Viable | Legacy type migration plan |

## Related ADRs

- [ADR 2509: Vertical Slice Architecture](../../adr/adr-2509-vertical-slice-architecture.md)
- [ADR 2510: CapabilityStatement Without Firely SDK](../../adr/adr-2510-capability-sourcenode-model.md)

## Related Features

- [Storage](../storage/readme.md)
- [Multi-Tenancy](../multi-tenancy/readme.md)
- [FHIR Operations](../fhir-operations/readme.md)

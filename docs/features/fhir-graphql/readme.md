# Feature: FHIR GraphQL Operation

Implementation of the FHIR $graphql operation as defined in the [FHIR GraphQL specification](https://build.fhir.org/graphql.html).

## Status

**Current**: Investigation Complete

## Overview

The FHIR $graphql operation provides a GraphQL interface for querying FHIR resources, enabling clients to request exactly the data they need in a single request. This avoids over-fetching and under-fetching problems inherent in REST-based FHIR interactions.

### Key Requirements (from FHIR Spec)

- **System-level endpoint**: `[base]/$graphql` — query across resource types
- **Instance-level endpoint**: `[base]/[Type]/[id]/$graphql` — query a specific resource
- **Schema introspection**: Clients can discover available types/fields via `__schema`
- **GET and POST support**: Query via URL parameter (GET) or request body (POST)
- **Response format**: `application/json` (not `application/fhir+json`)

### Design Constraints

- Must be an **experimental feature** with configuration-driven toggle (following existing pattern)
- Must follow the established layering: API → Application → Domain → DataLayer
- Must support multi-tenancy (tenant-explicit and tenant-agnostic routing)
- Must leverage existing `ISchema` (StructureDefinition metadata) for GraphQL schema generation
- Must reuse existing `IFhirRepository` / `ISearchService` for data access
- Schema generation must be dynamic and version-aware (STU3, R4, R4B, R5, R6)

## Investigations

| Investigation | Status | Created | Description |
|--------------|--------|---------|-------------|
| [design-proposal-hotchocolate](investigations/design-proposal-hotchocolate.md) | Viable | 2026-03-25 | HotChocolate-based design with ITypeModule dynamic schema generation |
| [design-proposal-graphql-dotnet](investigations/design-proposal-graphql-dotnet.md) | Viable | 2026-03-25 | GraphQL.NET-based design with programmatic schema building |
| [unified-design](investigations/unified-design.md) | Complete | 2026-03-25 | Reconciled best-of-both design: HotChocolate library + CQRS-first resolvers |

## Alignment

Investigation complete. All items below are addressed in [unified-design.md](investigations/unified-design.md) §18 and pending implementation verification.

- [x] Follows layer rules (API → App → Domain → Data)
- [x] F5 Developer Experience (works with minimal setup)
- [x] FHIR spec compliance ($graphql operation)
- [x] Consistent with existing experimental feature patterns
- [x] Multi-tenant support
- [x] Multi-version FHIR support (STU3, R4, R4B, R5, R6)

# Investigation: FHIR Server v2 - Multi-Tenant Multi-Version Architecture

**Feature**: architecture
**Status**: Viable
**Created**: 2025-01-07

## Context

The current FHIR Server implementation in `src-old` has grown organically over time, resulting in:

- **Version-specific project proliferation**: Separate projects for each FHIR version (STU3, R4, R4B, R5) with significant code duplication
- **Storage coupling**: Tight coupling to specific storage implementations (CosmosDB, SQL Server)
- **Monolithic design**: Difficulty hosting multiple tenants or FHIR versions in the same process
- **Shared state challenges**: Global configurations and services make isolation difficult
- **Testing complexity**: Large integration test suites due to tightly coupled components

### Legacy System Analysis

The current system includes extensive functionality:

**Core FHIR Operations:**
- Resource CRUD operations (Create, Read, Update, Delete)
- Search with complex parameter support
- Bundle processing and transaction support
- Versioning and history management
- Compartment-based access control

**Advanced Operations:**
- Bulk operations (Export, Import, Delete, Update)
- Data conversion and transformation
- Validation and conformance checking
- Reindexing and schema management
- Member matching and patient linking

**Infrastructure Features:**
- Multi-storage backends (CosmosDB, SQL Server, Azure Blob)
- Authentication and authorization
- Audit logging and telemetry
- Health monitoring and metrics
- Configuration management

**FHIR Version Support:**
- STU3, R4, R4B, R5 with version-specific APIs
- Operation definitions and capability statements
- Search parameter management

### Problems with Current Approach

1. **Resource Duplication**: Each FHIR version requires separate API, Core, Web, and Client projects
2. **Deployment Complexity**: Cannot serve multiple FHIR versions from single deployment
3. **Tenant Isolation**: No native multi-tenancy support
4. **Storage Abstraction**: Storage implementations are tightly coupled to core logic
5. **Testing Overhead**: Integration tests require full system setup

## Decision

We will design and implement FHIR Server v2 with the following architectural principles:

### 1. Multi-Tenant Multi-Version Core Design

**Tenant and Version Context**: Every operation will be scoped to a specific tenant and FHIR version context, enabling:
- Multiple tenants sharing the same process
- Multiple FHIR versions supported simultaneously per tenant
- Isolated configuration and data per tenant/version combination

**Interface-Based Architecture**: Build on lower-level interfaces to enable:
- Storage-agnostic design with pluggable backends
- Version-agnostic core logic with version-specific adapters
- Testable components with minimal dependencies

### 2. Layered Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      API Layer                              │
│  (Tenant/Version Routing, Authentication, Controllers)      │
├─────────────────────────────────────────────────────────────┤
│                   Business Logic Layer                      │
│  (FHIR Operations, Validation, Search, Bulk Operations)     │
├─────────────────────────────────────────────────────────────┤
│                  Abstraction Layer                          │
│  (Storage Interfaces, Version Adapters, Context Providers)  │
├─────────────────────────────────────────────────────────────┤
│                   Storage Layer                             │
│  (CosmosDB, SQL Server, File System, In-Memory)            │
└─────────────────────────────────────────────────────────────┘
```

### 3. Key Architectural Insights from Current Implementation

**Version-Agnostic Schema Provider Pattern**: The current `src` implementation demonstrates a powerful approach through `IFhirSchemaProvider`:

```csharp
public interface IFhirSchemaProvider : IStructureDefinitionSummaryProvider
{
    FhirSpecification Version { get; }
    IReadOnlySet<string> ResourceTypeNames { get; }
}
```

This enables a single search indexer to work across all FHIR versions by abstracting version-specific schema details through the provider interface.

**Lower-Level Serialization Strategy**: The `SourceNodeSerialization` project shows how to handle multi-version serialization:

- **Version-agnostic JSON parsing**: `JsonSourceNodeFactory` creates `ISourceNode` abstractions from JSON without version-specific knowledge
- **Reflection-based source nodes**: `ReflectedSourceNode` and `ResourceJsonNode` provide lightweight, extensible serialization
- **Extension data handling**: `JsonExtensionData` captures unknown properties for forward/backward compatibility

**Search Indexer Factory Pattern**: The `SearchIndexerFactory` demonstrates dependency injection and composition:

```csharp
public static async Task<ISearchIndexer> CreateInstance(
    IFhirSchemaProvider fhirSchemaProvider,
    ILoggerFactory loggerProvider)
```

This factory creates version-specific search indexers using the schema provider, enabling the same search logic to work across FHIR versions through polymorphism.

### 4. Communication Pattern Decision

**Medino over MediatR**: We will use **Medino** (v2.0.1) as our mediator library for the following reasons:

- **Zero Dependencies**: Core library has no external dependencies, reducing supply chain risk
- **Explicit Async APIs**: All methods use `*Async` suffix, preventing sync-over-async anti-patterns
- **CQRS Optimized**: Designed specifically for Command Query Responsibility Segregation
- **Pipeline Behaviors**: Built-in support for cross-cutting concerns (validation, logging, caching)
- **Easy Migration**: Direct API compatibility with MediatR 12.5 for legacy migration
- **MIT License**: No licensing concerns for commercial use

### 5. Memory Efficiency Strategy

**Modern .NET Memory Patterns**: We will leverage .NET 9's memory-efficient patterns throughout:

**Span<T> and ReadOnlySpan<T> Usage:**
- FHIR JSON parsing will use `ReadOnlySpan<byte>` for zero-allocation string slicing
- Resource property access through spans to avoid string allocations
- Search parameter parsing using span-based operations

**Memory<T> and ReadOnlyMemory<T>:**
- Async FHIR operations will use `Memory<T>` for heap-safe buffer management
- Stream processing with `ReadOnlyMemory<byte>` for efficient I/O operations

**RecyclableMemoryStream Integration:**
- Replace all `MemoryStream` usage with `RecyclableMemoryStream` for buffer pooling
- Large FHIR resources (>85KB) benefit from Large Object Heap optimization
- Reduces GC pressure for bulk operations and export scenarios

**Record Types for FHIR Models:**
```csharp
public record FhirResourceReference(string Type, string Id, string Version = null);
public record SearchParameter(string Name, ReadOnlyMemory<char> Value, SearchModifier Modifier);
```

**ArrayPool<T> and MemoryPool<T>:**
- Search result aggregation using pooled arrays
- Bundle processing with pooled memory to avoid large allocations

### 6. Phased Implementation Strategy

**Phase 1: Foundation (Months 1-2)**
- Implement `IFhirSchemaProvider` pattern for all FHIR versions (STU3, R4, R4B, R5)
- Complete `SourceNodeSerialization` with `ReadOnlySpan<byte>` JSON parsing
- Core tenant/version context management using dependency injection
- `SearchIndexerFactory` with pluggable schema providers
- RecyclableMemoryStream integration for all stream operations
- In-memory storage implementation with `ISourceNode` integration

**Phase 2: Essential Features (Months 3-4)**
- Search implementation leveraging version-agnostic indexer pattern with `Span<T>` optimizations
- CRUD operations using `ISourceNode` abstraction across all versions
- Medino integration for CQRS command/query handling
- Basic validation framework using `IFhirSchemaProvider`
- File-based storage with `Memory<T>` and ArrayPool optimizations

**Phase 3: Advanced Search & Operations (Months 5-6)**
- Complex search parameters and modifiers
- Compartment support
- History and versioning
- Basic bulk operations (export)
- Health monitoring and metrics

**Phase 4: Multi-Version Optimization (Months 7-8)**
- Complete schema providers for all FHIR versions using existing patterns
- Version-specific capability statement generation via schema providers
- Cross-version compatibility and migration utilities
- Performance optimization of version-agnostic patterns

**Phase 5: Production Storage (Months 9-10)**
- CosmosDB implementation
- SQL Server implementation
- Performance optimization
- Caching strategies

**Phase 6: Advanced Operations (Months 11-12)**
- Bulk import and update operations
- Data conversion and transformation
- Reindexing capabilities
- Advanced audit and telemetry

**Phase 7: Enterprise Features (Months 13+)**
- Advanced multi-tenancy (tenant isolation, quotas)
- Custom operation definitions
- Implementation guide support
- Advanced security features

## Consequences

### Positive Consequences

1. **Simplified Deployment**: Single deployment can serve multiple tenants and FHIR versions
2. **Dramatic Code Reduction**: Schema provider pattern eliminates version-specific projects (4x reduction)
3. **Superior Performance**: Memory-efficient patterns (.NET 9) + Medino's zero-dependency design reduces allocations by 50-70%
4. **Version Agnostic Core**: `ISourceNode` abstraction enables single codebase for all FHIR versions
5. **Memory Optimization**: Span/Memory usage, RecyclableMemoryStream, and ArrayPool eliminate most GC pressure
6. **Enhanced Testability**: Interface-based design with lightweight serialization improves unit testing
7. **Storage Flexibility**: `ISourceNode` abstraction works with any storage backend
8. **Zero Supply Chain Risk**: Medino's no-dependency approach reduces security vulnerabilities
9. **Future-Proof Serialization**: Extension data handling supports forward/backward compatibility
10. **Operational Efficiency**: Centralized monitoring, logging, and management across all versions

### Negative Consequences

1. **Migration Complexity**: Existing deployments will require migration strategy
2. **Initial Development Overhead**: Building abstractions requires upfront investment
3. **Performance Considerations**: Additional abstraction layers may impact performance
4. **Complexity Management**: Multi-tenant/multi-version logic adds system complexity

### Risk Mitigation

1. **Performance**: Profile early and optimize critical paths
2. **Complexity**: Maintain clear separation of concerns and comprehensive documentation
3. **Migration**: Develop compatibility adapters and migration tools
4. **Quality**: Implement comprehensive testing strategy from Phase 1

### Success Metrics

- **Code Reduction**: 75% reduction in lines of code compared to legacy system (eliminates version-specific projects)
- **Deployment Simplification**: Single deployment artifact supports all tenants and FHIR versions
- **Performance Improvement**: 50-70% performance improvement through memory-efficient patterns and Medino's optimized design
- **Test Coverage**: >90% unit test coverage with fast execution (<3 minutes via lightweight abstractions)
- **Version Support**: All 4 FHIR versions (STU3, R4, R4B, R5) supported by Phase 4
- **Feature Parity**: 100% feature compatibility by Phase 6 completion

### Implementation Foundation

The current `src` implementation already demonstrates the core architectural patterns:

- **Working Examples**: `IFhirSchemaProvider`, `SearchIndexerFactory`, and `SourceNodeSerialization` are functional
- **Proven Pattern**: Version-agnostic search indexing working across FHIR versions
- **Performance Validated**: Source generation approach shows measurable improvements
- **Incremental Path**: Existing foundation allows incremental implementation without big-bang migration

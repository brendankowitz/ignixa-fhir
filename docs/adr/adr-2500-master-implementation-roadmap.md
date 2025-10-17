# ADR 2500: FHIR Server v2 - Master Implementation Roadmap

## Status

Proposed

## Context

This master ADR provides the complete implementation roadmap for FHIR Server v2, organized into phase-specific ADRs. Each phase is documented in a separate ADR with detailed implementation guidance.

### Design Principles

1. **F5 Developer Experience**: Press F5 and run with zero external dependencies
2. **Vertical Slices**: Complete features end-to-end, not horizontal layers
3. **Test-Driven**: 80% coverage minimum, E2E tests define success
4. **Performance Built-In**: Memory-efficient patterns from day one
5. **Incremental Delivery**: Each phase produces deployable software

### Investigation Summary

See `docs/investigations/SUMMARY.md` for comprehensive analysis of all 29 investigation documents covering:
- Architecture patterns
- Storage strategies (File, SQL, Cosmos)
- Performance optimizations (50-70% allocation reduction)
- Feature implementations (Bundle, Search, Validation, Bulk)
- Infrastructure (Caching, Messaging, Identity)
- Capability statement generation (dynamic refresh with caching)
- Authorization & security (RBAC + SMART + capability enforcement)
- Custom search parameters (lifecycle, conflict resolution, reindexing)
- Version conversion and multi-version support (path-based routing, no automatic conversion)
- Custom structures and R6 extensibility (extensions, Additional Resources, dynamic IG loading)
- Background jobs with DurableTask (orchestrations, activities, fault tolerance)
- Multi-tenancy data partitioning modes (Isolation vs Distributed, pass-through optimization, fanout/union strategy)
- **Dynamic FHIR routing** (endpoint routing eliminates 145+ controllers - see `dynamic-fhir-routing.md`)
- **Bundle streaming** (IAsyncEnumerable + FhirJsonWriter reduces memory by 95% - see `bundle-streaming.md`)
- **Search query parsing** (simplified 800-line factory to 250 lines - see `search-query-parsing.md`)
- **Bundle deferred writes** (two-phase channel architecture with TaskCompletionSource - see `bundle-deferred-writes.md`) - **IMPLEMENTED in Phase 1.1a**
- **Bundle streaming parser** (Utf8JsonReader with state machine, 99% memory reduction - see `bundle-streaming-parser.md`) - **IMPLEMENTED in Phase 1.1a**

## Phase Overview

### Prototype Phase (Week 1) - ADR-2501
**Goal**: Vertical slice with PUT and GET only

**Deliverables**:
- All architectural layers established
- File-based storage with `.metadata.ndjson` sidecar files
- PUT /Patient/{id} and GET /Patient/{id} working end-to-end
- 80% test coverage

**Key Innovation**: Metadata sidecar files with pre-extracted search indices (10x faster startup)

---

### Phase 1: Search Foundation (Weeks 2-8)

#### Phase 1.1: Bundle Processing (Weeks 2-6) - ADR-2502
**Goal**: Transaction bundle support with parallel execution + streaming optimization

**Original Deliverables** (Week 2, 16 hours):
- POST / with transaction bundles
- ASP.NET Core pipeline routing (no switch statements!)
- System.Threading.Channels for parallel execution
- Reference resolution for urn:uuid:

**Phase 1.1a Enhancement** (Weeks 3-6, 76 hours):
- Two-phase channel architecture with deferred writes (DeferredWriteCoordinator)
- Streaming bundle parser with Utf8JsonReader (99% memory reduction)
- ArrayPool<byte> buffer management for zero-copy parsing
- Prefer: streaming HTTP header support
- IFhirRepository.BatchWriteAsync for future SQL/Cosmos bulk operations

**Key Innovations**:
1. Mini HttpContext + pipeline routing for automatic handler discovery
2. TaskCompletionSource pattern for deferred write completion
3. Utf8JsonReader state machine for wire-to-database streaming
4. Manifest-based atomic commits for transaction bundles

**E2E Tests**: `BundleTransactionTests.cs`, `BundleBatchTests.cs` (ALL tests)

**Performance Gains**:
- File-based: +5-10% throughput, atomic commits
- Memory: 99% reduction for large bundles (<1MB vs 100MB for 1000 entries)
- Latency: 840x faster time to first entry (50ms vs 42s)
- Future SQL: +50-70% via BatchWriteAsync with SqlBulkCopy
- Future Cosmos: +60-80% via BatchWriteAsync with batch API

#### Phase 1.2: Search Implementation (Week 7) - ADR-2503
**Goal**: Complete InMemory search from microsoft/fhir-server + Capability Statement + Authorization

**Deliverables**:
- `SearchQueryInterpreter.cs` - Expression visitor pattern
- `ComparisonValueVisitor.cs` - Type-specific comparisons
- `InMemoryIndex.cs` - Index structure from subscription-engine branch
- IndexLoaderService loads from `.metadata.ndjson` files
- **GET /metadata** - CapabilityStatementService with basic segments
- **FhirAuthorizationMiddleware** - Authentication + basic authorization

**Key Innovation**: Reuse proven microsoft/fhir-server InMemory search architecture + segmented capability statement

**E2E Tests**: `Search/BasicSearchTests.cs`, `Search/StringSearchTests.cs`

#### Phase 1.3: Search Parameter Types (Week 8) - ADR-2504
**Goal**: Support all basic search parameter types

**Deliverables**:
- String, Token, Date, Number, Reference search
- _id, _lastUpdated, _type parameters
- Pagination with continuation tokens

**E2E Tests**: `Search/TokenSearchTests.cs`, `Search/DateSearchTests.cs`, `Search/NumberSearchTests.cs`

---

### Phase 2: Multi-Resource CRUD (Weeks 9-11) - ADR-2505
**Goal**: Extend to Observation, Encounter with reference validation

**Deliverables**:
- Generic resource handling (not Patient-specific)
- Reference validation across resources
- Observation and Encounter CRUD

**E2E Tests**: All CRUD tests for multiple resource types

**Note**: Start week shifted by +4 due to Phase 1.1a enhancement

---

### Phase 3: Validation (Weeks 12-14) - ADR-2506
**Goal**: Two-tier validation architecture + Capability Enforcement

**Deliverables**:
- Tier 1: Fast structural (<50ms) - always runs
- Tier 2: Profile validation (<5s) - opt-in via $validate
- Modern Firely SDK 6.0 with caching
- **ProfileCapabilitySegment** - Dynamic capability refresh when profiles loaded
- **CapabilityEnforcementHandler** - Server must respect its CapabilityStatement

**Key Innovation**: Separate fast/slow validation solves legacy >1s validation problem + capability enforcement

**E2E Tests**: `ValidateTests.cs`

---

### Phase 4: Advanced Search (Weeks 15-18) - ADR-2507
**Goal**: Chaining, _include, _revinclude, composite parameters

**Deliverables**:
- Chained search (patient.name=John)
- _include (forward references)
- _revinclude (reverse references)
- Composite search parameters

**E2E Tests**: `Search/ChainingSearchTests.cs`, `Search/IncludeSearchTests.cs`, `Search/CompositeSearchTests.cs`

---

### Phase 5: Multi-Version Support (Weeks 19-23) - ADR-2508
**Goal**: STU3, R4, R4B, R5, R6 from single deployment with path-based routing

**Deliverables**:
- IFhirSchemaProviderFactory for all versions (STU3, R4, R4B, R5, R6)
- Path-based version routing (`/{tenantId}/{version}/*`)
- FhirRequestContext with version resolution
- VersionEnforcementMiddleware (reject unsupported resource types per version)
- Version-tagged storage (add `FhirVersion` field to all storage layers)
- Version-specific search parameter loading
- Per-version CapabilityStatement generation
- **NO automatic version conversion** (explicit design decision)

**Key Innovation**: Zero conversion overhead with per-version isolation (industry standard: Google Cloud, Azure, AWS)

**E2E Tests**: `MultiVersionTests.cs` (20+ tests)

---

### Phase 6: Multi-Tenant (Weeks 24-27) - ADR-2509
**Goal**: Tenant isolation and routing

**Deliverables**:
- Tenant context resolution
- Tenant-scoped repositories
- Tenant routing (/{tenantId}/R4/)
- Complete data isolation
- **TenantIsolationHandler** - Authorization layer tenant enforcement
- **Tenant-aware capability caching** - Separate CapabilityStatement per tenant

---

### Phase 7: Distributed Infrastructure (Weeks 28-33) - ADR-2510
**Goal**: Web farm deployment with Redis

**Deliverables**:
- Redis cache implementation
- Redis message bus for distributed messaging
- Health checks and monitoring
- Worker services for background processing
- **Redis capability statement cache** - Distributed caching with pattern invalidation
- **Event-based cache invalidation** - Pub/sub for capability changes across nodes

---

### Phase 8: Legacy SQL Server Storage (Weeks 34-39) - ADR-2511
**Goal**: EF-based data layer compatible with existing legacy schema

**Deliverables**:
- Entity Framework Core integration with legacy schema
- Core search functionality (no jobs or events - we have our own plan)
- Read/Write to existing Resource, ResourceHistory tables
- Basic transaction support using EF transactions
- Migration from file storage
- Compatibility with microsoft/fhir-server schema

**Key Innovation**: Reuse existing production databases without schema migration, enabling gradual adoption

**E2E Tests**: All CRUD and basic search tests against SQL Server

---

### Phase 8a: Optimized SQL Server Storage (Weeks 40-45) - ADR-2511a
**Goal**: Optimized database storage with decoupled resource/index storage

**Deliverables**:
- 3-table split (Resource, ResourceHistory, RawResource)
- Optimized search indices
- Advanced transaction support
- **URN-based storage locations** - Single `StorageLocation varchar(500)` column
- **Separate IResourceStore and ISearchIndexStore** - Decoupled storage pattern
- Migration path from legacy SQL schema to optimized schema

**Key Innovation**: 3-table split eliminates NULLs, 61% storage reduction + URN pattern enables hybrid storage

**E2E Tests**: Performance benchmarks showing 61% storage reduction

---

### Phase 9: Cosmos DB Storage (Weeks 46-53) - ADR-2512
**Goal**: Planet-scale storage

**Deliverables**:
- Compact search index pattern (from issue #2686)
- Smart partitioning ({ResourceType}|{YearMonth}|{TenantId})
- Sub-second point reads (<5ms, <5 RU)
- Bulk operations support

---

### Phase 10: SMART on FHIR (Weeks 54-59) - ADR-2513
**Goal**: OAuth 2.0 authorization with multi-provider identity

**Deliverables**:
- SMART on FHIR v2 endpoints
- Scope parsing (patient/*.read, user/*.write, etc.)
- PKCE support for public clients
- Entra ID, Auth0, Keycloak integration
- **SmartScopeAuthorizationHandler** - Complete authorization pipeline
- **Patient compartment filtering** - Automatic data filtering for patient/* scopes

---

### Phase 11: Implementation Guides (Weeks 60-65) - ADR-2514
**Goal**: US Core and custom IG support

**Deliverables**:
- NPM package loading from packages.fhir.org
- Header-based IG resolution
- Composite schema provider (base + profiles)
- US Core 6.1.0 validation

---

### Phase 12: Custom Search Parameters (Weeks 66-69) - ADR-2515
**Goal**: Client-posted search parameters with lifecycle management and reindexing

**Deliverables**:
- POST /SearchParameter endpoint with conflict detection
- SearchParameterRepository with source-aware priority (Base > IG > Custom)
- Multi-status lifecycle: Supported → Reindexing → Enabled
- SearchParameterReindexOrchestrator with background job queue
- R5+ `constraint` field support (FHIRPath filter for conditional application)
- SearchParameterConflictResolver (prevents IG overwrites of base parameters)
- POST /$reindex operation with job monitoring
- CapabilityStatement integration (expose active parameters)

**Key Innovation**: Source-aware priority system solves 86% US Core collision problem + constraint evaluation for conditional parameters

**E2E Tests**: `CustomSearchParameterTests.cs`, `SearchParameterConflictTests.cs`, `SearchParameterReindexTests.cs` (30+ tests)

---

### Phase 13: Bulk Operations (Weeks 70-75) - ADR-2516
**Goal**: $export and $import with channels

**Deliverables**:
- System/$export, Patient/$export, Group/$export
- $import with channel-based processing
- NDJSON streaming
- Azure Blob Storage integration

**Key Innovation**: Reuse System.Threading.Channels pattern from import investigation

**E2E Tests**: `Export/ExportTests.cs`, `Import/ImportTests.cs` (10+ files)

---

### Phase 14: Advanced Operations (Weeks 76-81) - ADR-2517
**Goal**: $bulk-delete, $bulk-update (HIGH priority from gap analysis)

**Deliverables**:
- $bulk-delete operation
- $bulk-update operation
- Streaming channel-based processing

---

### Phase 15: US Core Operations (Weeks 82-87) - ADR-2518
**Goal**: $docref, $everything (US Core requirements)

**Deliverables**:
- $docref operation
- $everything operation (patient compartment)

---

### Phase 16: Compartment Search (Weeks 88-90) - ADR-2519
**Goal**: FHIR compartment definitions

**Deliverables**:
- Patient compartment
- Encounter compartment
- Other standard compartments

---

### Phase 17: Patch Operations (Weeks 91-94) - ADR-2520
**Goal**: JSON Patch and FHIRPath Patch

**Deliverables**:
- JSON Patch (RFC 6902)
- FHIRPath Patch

**E2E Tests**: `JsonPatchTests.cs`, `FhirPathPatchTests.cs`

---

### Phase 18: Production Readiness (Weeks 95-100) - ADR-2521
**Goal**: Observability, monitoring, operations

**Deliverables**:
- OpenTelemetry integration (tracing + metrics)
- Structured logging
- Rate limiting
- Circuit breakers
- Deployment guides

---

### Phase 19: Custom Structures and Extensions (Weeks 101-104) - ADR-2522
**Goal**: Production-ready extension and R6 Additional Resource support

**Deliverables**:
- Extension indexing for search (FHIRPath-based extraction)
- Custom SearchParameter registration for extensions
- R6 Additional Resource hot-loading (`POST /$load-ig`)
- IAdditionalResourceProvider for dynamic resource type registration
- Enhanced CompositeSchemaProvider (base + IGs + Additional Resources)
- CapabilityStatement updates when IGs loaded (segment invalidation)
- Extension validation (Tier 2)
- Dynamic ITypedElement parsing for Additional Resources

**Key Innovation**: Hot-reload Implementation Guides without server restart, enabling R6 Additional Resources with zero downtime

**E2E Tests**:
- `ExtensionSearchTests.cs` (10+ tests): Search by extension values
- `AdditionalResourceTests.cs` (15+ tests): R6 Additional Resource CRUD and search
- `DynamicIgLoadingTests.cs` (8+ tests): Hot-reload NPM packages

---

### Phase 20: Multi-Tenancy Data Partitioning (Weeks 105-116) - ADR-2523
**Goal**: Complete multi-tenancy data partitioning with Isolation and Distributed modes

**Deliverables**:
- Data Layer Registry with capabilities and participation modes
- Isolation mode strategies: Database per Tenant, Schema per Tenant, Partition Key
- Distributed mode with intelligent query execution
- Pass-through optimization (zero overhead for 0-1 layers)
- Fanout/Union Executor (parallel and sequential strategies)
- Result Aggregator with deduplication and distributed sorting
- Composite continuation tokens for distributed pagination
- Pre-query filters for de-identification
- Distributed query authorization
- Audit logging for cross-layer queries
- Layer health monitoring and failover
- Performance benchmarks for all partitioning modes

**Key Innovation**: Dual-mode architecture with pass-through optimization ensures zero overhead for simple scenarios while enabling distributed queries across multiple data layers

**Real-World Use Cases**:
- Healthcare Research Network (5+ hospitals with OptIn participation)
- Multi-Region Deployment (geographic data residency with global search)
- SaaS with Premium Tier (partition key for basic, dedicated DB for premium)

**E2E Tests**:
- `IsolationModeTests.cs` (20+ tests): Database/Schema/Partition key isolation
- `DistributedModeTests.cs` (30+ tests): Fanout, aggregation, deduplication
- `PassThroughOptimizationTests.cs` (10+ tests): Zero overhead verification
- `DataLayerRegistryTests.cs` (15+ tests): Registration, participation modes
- `DistributedAuthorizationTests.cs` (10+ tests): Cross-layer access control

---

## Timeline Summary

| Phase | Weeks | Claude Code Hours | Cumulative | Key Deliverable |
|-------|-------|-------------------|------------|-----------------|
| Prototype | 1 | 20 | 20 | PUT/GET with file storage |
| 1.1 | 2 | 16 | 36 | Bundle processing (original) |
| 1.1a | 3-6 | 76 | 112 | Streaming & deferred writes |
| 1.2 | 7 | 16 | 128 | InMemory search |
| 1.3 | 8 | 16 | 144 | Search parameters |
| 2 | 9-11 | 48 | 192 | Multi-resource CRUD |
| 3 | 12-14 | 48 | 240 | Two-tier validation |
| 4 | 15-18 | 64 | 304 | Advanced search |
| 5 | 19-23 | 80 | 384 | Multi-version |
| 6 | 24-27 | 64 | 448 | Multi-tenant |
| 7 | 28-33 | 96 | 544 | Distributed infra |
| 8 | 34-39 | 96 | 640 | Legacy SQL Server |
| 8a | 40-45 | 96 | 736 | Optimized SQL Server |
| 9 | 46-53 | 128 | 864 | Cosmos DB |
| 10 | 54-59 | 96 | 960 | SMART on FHIR |
| 11 | 60-65 | 96 | 1056 | Implementation Guides |
| 12 | 66-69 | 64 | 1120 | Custom search parameters |
| 13 | 70-75 | 96 | 1216 | Bulk operations |
| 14 | 76-81 | 96 | 1312 | Advanced operations |
| 15 | 82-87 | 96 | 1408 | US Core operations |
| 16 | 88-90 | 48 | 1456 | Compartments |
| 17 | 91-94 | 64 | 1520 | Patch operations |
| 18 | 95-100 | 96 | 1616 | Production readiness |
| 19 | 101-104 | 64 | 1680 | Custom structures & extensions |
| 20 | 105-116 | 192 | 1872 | Multi-tenancy data partitioning |

**Total**: 116 weeks, ~1,872 Claude Code hours

**Note**: Phase 1.1a adds 4 weeks and 76 hours to original plan. Timeline shift applies to all subsequent phases.

## Definition of Done

**FHIR Server v2 is complete when ALL 118 E2E tests from src-old/test pass.**

See `legacy-feature-analysis.md` for complete test inventory.

### Per-Phase Requirements

1. **Functional**: All deliverables implemented
2. **Tests**: 80% minimum coverage (xUnit + NSubstitute)
3. **E2E**: Phase-specific E2E tests pass
4. **Performance**: Meets phase-specific targets
5. **Documentation**: ADR updated with learnings

### System-Level Metrics (Phase 17)

| Metric | Target | Storage Type |
|--------|--------|--------------|
| CRUD operations | <10ms | In-memory |
| CRUD operations | <20ms | File |
| CRUD operations | <100ms | SQL/Cosmos |
| Simple search | <50ms | In-memory |
| Simple search | <100ms | File |
| Simple search | <200ms | SQL |
| Point read | <5ms, <5 RU | Cosmos |
| Cross-partition search | <500ms, <500 RU | Cosmos |
| Bulk export | 10,000 resources/min | All |
| Concurrent users | 1,000 | Distributed |
| Uptime SLA | 99.9% | Production |

## Phase-Specific ADRs

Each phase has a dedicated ADR with detailed implementation guidance:

- **ADR-2501**: Prototype Phase (Week 1)
- **ADR-2502**: Phase 1.1 - Bundle Processing (Week 2)
- **ADR-2503**: Phase 1.2 - Search Implementation (Week 3)
- **ADR-2504**: Phase 1.3 - Search Parameter Types (Week 4)
- **ADR-2505**: Phase 2 - Multi-Resource CRUD (Weeks 5-7)
- **ADR-2506**: Phase 3 - Validation (Weeks 8-10)
- **ADR-2507**: Phase 4 - Advanced Search (Weeks 11-14)
- **ADR-2508**: Phase 5 - Multi-Version Support (Weeks 15-19)
- **ADR-2509**: Phase 6 - Multi-Tenant (Weeks 20-23)
- **ADR-2510**: Phase 7 - Distributed Infrastructure (Weeks 24-29)
- **ADR-2511**: Phase 8 - Legacy SQL Server Storage (Weeks 30-35)
- **ADR-2511a**: Phase 8a - Optimized SQL Server Storage (Weeks 36-41)
- **ADR-2512**: Phase 9 - Cosmos DB Storage (Weeks 42-49)
- **ADR-2513**: Phase 10 - SMART on FHIR (Weeks 50-55)
- **ADR-2514**: Phase 11 - Implementation Guides (Weeks 56-61)
- **ADR-2515**: Phase 12 - Custom Search Parameters (Weeks 62-65)
- **ADR-2516**: Phase 13 - Bulk Operations (Weeks 66-71)
- **ADR-2517**: Phase 14 - Advanced Operations (Weeks 72-77)
- **ADR-2518**: Phase 15 - US Core Operations (Weeks 78-83)
- **ADR-2519**: Phase 16 - Compartment Search (Weeks 84-86)
- **ADR-2520**: Phase 17 - Patch Operations (Weeks 87-90)
- **ADR-2521**: Phase 18 - Production Readiness (Weeks 91-96)
- **ADR-2522**: Phase 19 - Custom Structures and Extensions (Weeks 97-100)
- **ADR-2523**: Phase 20 - Multi-Tenancy Data Partitioning (Weeks 101-112)

## Key Architectural Decisions

From 29 investigation documents (see `docs/investigations/SUMMARY.md`):

1. **Vertical Slices**: Complete features end-to-end (not layers)
2. **Medino Messaging**: In-process abstraction, Redis for distributed
3. **Memory Efficiency**: RecyclableMemoryStream, Span<T>, ArrayPool<T> (50-70% reduction)
4. **File Storage**: NDJSON + `.metadata.ndjson` sidecar files
5. **InMemory Search**: microsoft/fhir-server SearchQueryInterpreter pattern
6. **Bundle Processing**: ASP.NET Core pipeline routing + channels
7. **SQL Storage**: 3-table split (eliminates NULLs, 61% reduction)
8. **Cosmos Storage**: Compact search index (issue #2686 pattern)
9. **Validation**: Two-tier (fast structural + opt-in profile)
10. **Bulk Operations**: System.Threading.Channels for streaming
11. **Segmented Capability Statement**: Static/quasi-static/dynamic/tenant-specific segments with version hash tracking
12. **Capability Enforcement**: Server MUST respect its CapabilityStatement (authorization layer enforces)
13. **Layered Authorization**: 5-layer pipeline (auth → tenant → RBAC → scopes → capability)
14. **URN Storage Locations**: Decoupled resource/index storage with URN-based pointers
15. **Multi-Version Routing**: Path-based version routing with single canonical resource (no duplication across versions)
16. **No Automatic Conversion**: Explicit rejection over lossy conversion; zero overhead
17. **R6 Additional Resources**: Hot-load via `POST /$load-ig` for dynamic resource types
18. **Extension Indexing**: FHIRPath-based extraction for searchable extensions
19. **Feature Folder Organization**: Organize by capability (Patient/, Search/, Bundle/) not technical layer (Controllers/, Services/)
20. **Project Name: "Ignixa"**: Side project with `Ignixa.*` namespace (not Microsoft.Health)
21. **DurableTask Framework**: All background operations use Azure DurableTask (not custom task framework)
22. **Dual-Mode Data Partitioning**: Isolation mode (single repository) vs Distributed mode (fanout/union) as first-class abstractions
23. **Pass-Through Optimization**: Zero overhead for Isolation mode and Distributed mode with 0-1 layers
24. **Data Layer Registry**: Centralized registry of data stores with capabilities, participation modes, and health monitoring
25. **Endpoint Routing Over Controllers**: Zero controllers with generic RequestDelegate handlers (eliminates 145+ controller files - see `dynamic-fhir-routing.md`)
26. **Streaming Bundle Serialization**: IAsyncEnumerable + FhirJsonWriter for 95% memory reduction (see `bundle-streaming.md`)
27. **Simplified Search Query Parsing**: 250-line builder vs 800-line legacy factory (70% reduction - see `search-query-parsing.md`)
28. **Two-Phase Bundle Writes**: DeferredWriteCoordinator with TaskCompletionSource for atomic commits and future bulk operations (Phase 1.1a - see `bundle-deferred-writes.md`)
29. **Streaming Bundle Parser**: Utf8JsonReader with state machine for wire-to-database streaming, 99% memory reduction (Phase 1.1a - see `bundle-streaming-parser.md`)

## Consequences

### Positive

1. **Incremental Delivery**: Working software every week
2. **Risk Mitigation**: Problems found early in simple scenarios
3. **Developer Experience**: F5 works from Prototype Phase onward
4. **Performance**: Built-in from day one (not retrofitted)
5. **Test Coverage**: 80% minimum ensures quality
6. **Production Ready**: Each phase deployable
7. **Clear Success Criteria**: 118 E2E tests define "done"

### Negative

1. **Extended Timeline**: 116 weeks (29 months) to full parity (was 112 weeks; +4 weeks for Phase 1.1a)
2. **Coordination**: 24 phase ADRs to maintain (including 8a)
3. **Late Features**: Some operations deferred to Phase 14-19
4. **Multi-Version Complexity**: Resource ID must be unique across versions; version routing needs careful design to avoid duplication
5. **Early Complexity**: Phase 1.1a introduces TaskCompletionSource and state machine patterns early in roadmap

### Mitigation

1. **Weekly Demos**: Stakeholder feedback loop
2. **Continuous Integration**: All tests run on every commit
3. **Performance Benchmarks**: Track from Prototype Phase
4. **ADR Reviews**: Validate before starting each phase
5. **Phase 1.1a Justification**: 4-week investment validates patterns early with file storage, yields 50-80% gains in future SQL/Cosmos phases
6. **Pattern Encapsulation**: State machine complexity isolated in helper classes (BundleParserState), well-documented with investigation documents

## References

- Investigation Summary: `docs/investigations/SUMMARY.md`
- Legacy Feature Analysis: `docs/investigations/legacy-feature-analysis.md`
- Gap Analysis: `docs/investigations/roadmap-gap-analysis.md`
- Previous Roadmap: ADR-2510 (consolidated, now split into phases)

## Next Steps

1. **Review this master ADR** with stakeholders
2. **Begin Prototype Phase** (ADR-2501)
3. **Create remaining phase ADRs** (2503-2522) as phases approach
4. **Weekly progress tracking** against E2E test pass rate
5. **Design multi-version resource identity** - ensure Patient/123 is same resource across R4/R5 endpoints (no duplication)

# Investigation: Legacy FHIR Server Feature Analysis

**Feature**: status-reports
**Status**: Complete
**Created**: 2025-01-08

This document provides a comprehensive analysis of features extracted from the legacy FHIR Server codebase (`src-old`) to guide the v2 implementation.

## Project Structure Analysis

### Version-Specific Projects (Current Approach)
```
Microsoft.Health.Fhir.{Version}.{Layer}
├── Stu3.{Api|Core|Web|Client}
├── R4.{Api|Core|Web|Client}
├── R4B.{Api|Core|Web|Client}
└── R5.{Api|Core|Web|Client}
```

**Problems Identified:**
- 4x code duplication across versions
- Deployment requires version-specific endpoints
- Cannot serve multiple versions from single process
- Version-specific testing and maintenance

### Storage Implementations
```
Storage Backends Identified:
├── Microsoft.Health.Fhir.CosmosDb (+ Core, Initialization)
├── Microsoft.Health.Fhir.SqlServer
├── Microsoft.Health.Fhir.BlobStorage
└── Microsoft.Health.Fhir.Azure (Integration)
```

## Core Feature Inventory

### 1. FHIR Resource Operations

**Basic CRUD Operations:**
- Create resource with server-assigned ID
- Create resource with client-assigned ID
- Read resource by ID
- Update resource (create new version)
- Delete resource (logical delete)
- Conditional create/update/delete

**Advanced Resource Operations:**
- Resource versioning and history
- Resource validation against profiles
- Resource transformation and serialization
- Compartment-based access control

### 2. Search Capabilities

**Search Infrastructure:**
- Parameter parsing and validation
- Expression tree building
- Search result ranking and pagination
- Include/RevInclude processing

**Search Parameter Types:**
- String, Token, Reference, Quantity
- Date, URI, Number, Composite
- Special parameters (_id, _lastUpdated, _profile, _security, _tag)

**Search Modifiers:**
- `:exact`, `:contains`, `:missing`
- `:above`, `:below` (for hierarchical codes)
- `:not`, `:in`, `:not-in`
- `:text` (narrative search)

**Search Features:**
- Chained search (`Patient.name.family`)
- Reverse chained search (`_has`)
- Composite search parameters
- Search result bundles with pagination

### 3. Bundle Processing

**Bundle Types Supported:**
- Document bundles
- Message bundles
- Transaction bundles
- Batch bundles
- Collection bundles
- Search-result bundles

**Transaction Processing:**
- ACID transaction support
- Conditional operations within transactions
- Reference resolution and validation
- Rollback on failure

### 4. Bulk Operations

**Export Operations:**
- System-level export ($export)
- Patient-level export (Patient/$export)
- Group-level export (Group/[id]/$export)
- Export to Azure Blob Storage
- Export format configuration (NDJSON, etc.)

**Import Operations:**
- Bulk resource import
- Import validation and error handling
- Import progress tracking
- Import from Azure Blob Storage

**Bulk Update/Delete:**
- Bulk resource updates
- Bulk resource deletion
- Progress tracking and status reporting

### 5. Advanced FHIR Operations

**Validation Operations:**
- Resource validation ($validate)
- Profile-based validation
- Terminology validation
- Narrative validation

**Data Conversion:**
- Convert data between formats ($convert-data)
- Template-based transformations
- FHIR Liquid templates support

**Member Matching:**
- Patient matching ($member-match)
- Coverage determination
- Identity resolution

**Everything Operation:**
- Patient everything ($everything)
- Encounter everything
- Compartment-based data retrieval

### 6. Administrative Operations

**Search Parameter Management:**
- Search parameter definition
- Search parameter status management
- Custom search parameter creation
- Search parameter reindexing

**Reindexing Operations:**
- Full system reindexing
- Incremental reindexing
- Resource-specific reindexing
- Progress tracking and status

**Operation Definitions:**
- Custom operation definitions
- Operation metadata management
- Version-specific operations

### 7. Authentication & Authorization

**Authentication Methods:**
- JWT Bearer tokens
- OpenID Connect integration
- Certificate-based authentication
- API key authentication

**Authorization Features:**
- Role-based access control (RBAC)
- Resource-level permissions
- Compartment-based authorization
- Custom authorization handlers

**Security Features:**
- Audit logging
- Security headers
- CORS configuration
- Rate limiting

### 8. Conformance & Metadata

**Capability Statements:**
- Version-specific capability statements
- Operation definitions
- Search parameter definitions
- Interaction capabilities

**Implementation Guides:**
- IG package loading
- Profile validation
- ValueSet expansion
- CodeSystem lookups

**Metadata Management:**
- StructureDefinition handling
- SearchParameter definitions
- OperationDefinition metadata
- ConceptMap transformations

### 9. Infrastructure Features

**Health Monitoring:**
- Health check endpoints
- Dependency health validation
- Performance metrics
- Availability monitoring

**Telemetry & Metrics:**
- Performance counters
- Request/response logging
- Error tracking
- Custom metrics

**Configuration Management:**
- Feature flags
- Environment-specific configuration
- Tenant-specific settings
- Runtime configuration updates

**Background Services:**
- Task management
- Scheduled operations
- Queue processing
- Job status tracking

## Multi-Tenant Requirements Analysis

### Current Limitations
- Single tenant per deployment
- Global configuration shared across all resources
- No tenant isolation at data level
- Authentication context not tenant-aware

### V2 Multi-Tenant Requirements

**Tenant Isolation:**
```csharp
public interface ITenantContext
{
    string TenantId { get; }
    string FhirVersion { get; }
    TenantConfiguration Configuration { get; }
    IServiceProvider Services { get; }
}
```

**Tenant-Scoped Services:**
- Storage repositories scoped to tenant
- Search indices per tenant
- Configuration per tenant
- Metrics and audit trails per tenant

**Tenant Discovery:**
```
Routing Options:
├── Subdomain: {tenant}.fhir.example.com/R4/
├── Path: fhir.example.com/{tenant}/R4/
├── Header: X-Tenant-Id + fhir.example.com/R4/
└── JWT Claim: tenant claim in auth token
```

## Multi-Version Requirements Analysis

### Current Version Handling
Each FHIR version requires separate:
- API controllers and routing
- Core business logic
- Data models and serialization
- Client libraries
- Web hosting configuration

### V2 Multi-Version Requirements

**Version Context:**
```csharp
public interface IFhirVersionContext
{
    FhirVersion Version { get; } // STU3, R4, R4B, R5
    IFhirVersionAdapter Adapter { get; }
    ICapabilityStatement Capabilities { get; }
}
```

**Version Adapters:**
- Resource serialization/deserialization per version
- Search parameter mapping across versions
- Operation definition differences
- Capability statement generation

**Version-Agnostic Core:**
```csharp
public interface IFhirResourceService<T> where T : IFhirResource
{
    Task<T> CreateAsync(T resource, ITenantContext tenant);
    Task<T> ReadAsync(string id, ITenantContext tenant);
    Task<T> UpdateAsync(T resource, ITenantContext tenant);
    Task DeleteAsync(string id, ITenantContext tenant);
    Task<Bundle> SearchAsync(SearchParameters parameters, ITenantContext tenant);
}
```

## Implementation Priority Matrix

| Feature Category | Legacy Complexity | Business Value | Implementation Phase |
|------------------|-------------------|----------------|---------------------|
| Basic CRUD | High | Critical | Phase 1 |
| Search (Basic) | High | Critical | Phase 2 |
| Bundle/Transaction | Medium | High | Phase 2 |
| Validation | Medium | High | Phase 2 |
| Multi-Version | High | High | Phase 4 |
| Bulk Export | Medium | Medium | Phase 3 |
| Advanced Search | High | Medium | Phase 3 |
| Bulk Import | Low | Medium | Phase 6 |
| Custom Operations | Medium | Low | Phase 6 |
| Member Matching | Low | Low | Phase 7 |

## Recommended Abstractions for V2

### Storage Abstraction
```csharp
public interface IFhirRepository
{
    Task<ResourceWrapper> CreateAsync(ResourceWrapper resource, CancellationToken cancellationToken);
    Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken);
    Task<ResourceWrapper> UpdateAsync(ResourceWrapper resource, CancellationToken cancellationToken);
    Task DeleteAsync(ResourceKey key, CancellationToken cancellationToken);
    Task<SearchResult> SearchAsync(SearchOptions options, CancellationToken cancellationToken);
}
```

### Search Abstraction
```csharp
public interface ISearchService
{
    Task<Bundle> SearchAsync(
        string resourceType,
        IReadOnlyList<Tuple<string, string>> parameters,
        ITenantContext context,
        CancellationToken cancellationToken);
}
```

### Validation Abstraction
```csharp
public interface IResourceValidator
{
    Task<OperationOutcome> ValidateAsync(
        ISourceNode resource,
        string profile,
        ITenantContext context,
        CancellationToken cancellationToken);
}
```

This analysis provides the foundation for implementing FHIR Server v2 with clear phase boundaries and reduced complexity compared to the legacy system.

## E2E Test Analysis

The legacy codebase contains **118 E2E/Integration test files** across **~129 test files** covering all FHIR R4, R4B, R5, and STU3 versions. Tests are organized in `src-old/test/` directory with the following structure:

### Test Project Organization

**E2E Test Projects (Per FHIR Version):**
- `Microsoft.Health.Fhir.R4.Tests.E2E`
- `Microsoft.Health.Fhir.R4B.Tests.E2E`
- `Microsoft.Health.Fhir.R5.Tests.E2E`
- `Microsoft.Health.Fhir.Stu3.Tests.E2E`

**Integration Test Projects (Per FHIR Version):**
- `Microsoft.Health.Fhir.R4.Tests.Integration`
- `Microsoft.Health.Fhir.R4B.Tests.Integration`
- `Microsoft.Health.Fhir.R5.Tests.Integration`
- `Microsoft.Health.Fhir.Stu3.Tests.Integration`

**Shared Test Projects:**
- `Microsoft.Health.Fhir.Shared.Tests.E2E` - Common E2E tests (75+ test classes)
- `Microsoft.Health.Fhir.Shared.Tests.E2E.Common` - Test fixtures and utilities
- `Microsoft.Health.Fhir.Shared.Tests.Integration` - Common integration tests
- `Microsoft.Health.Fhir.Shared.Tests.Smart` - SMART on FHIR authorization tests
- `Microsoft.Health.Fhir.Shared.Tests.Crucible` - HL7 Crucible conformance tests

### E2E Test Coverage by Feature Area

**Basic CRUD Operations (Priority 1):**
- `CreateTests.cs` - Resource creation with server/client-assigned IDs, provenance headers
- `ReadTests.cs` - Resource retrieval by ID
- `UpdateTests.cs` - Resource updates and versioning
- `DeleteTests.cs` - Logical delete operations
- `VReadTests.cs` - Versioned resource reads
- `HistoryTests.cs` - Resource version history

**Conditional Operations (Priority 1):**
- `ConditionalCreateTests.cs` - Conditional create with search criteria
- `ConditionalUpdateTests.cs` - Conditional update operations
- `ConditionalDeleteTests.cs` - Conditional delete operations
- `ConditionalPatchTests.cs` - Conditional PATCH operations

**Bundle Processing (Priority 1):**
- `BundleTransactionTests.cs` - ACID transaction bundles (DELETE→POST→PUT→GET order)
- `BundleBatchTests.cs` - Independent batch operations
- `BundleEdgeCaseTests.cs` - Error handling and edge cases

**Search Operations (Priority 2):**
- `BasicSearchTests.cs` - Multi-parameter search, resource type filtering
- `StringSearchTests.cs` - String parameter search with modifiers
- `TokenSearchTests.cs` - Token parameter search (identifiers, codes)
- `ReferenceSearchTests.cs` - Reference parameter search
- `DateSearchTests.cs` - Date range search
- `NumberSearchTests.cs` - Numeric parameter search
- `QuantitySearchTests.cs` - Quantity with units search
- `UriSearchTests.cs` - URI parameter search
- `CompositeSearchTests.cs` - Composite search parameters
- `CanonicalSearchTests.cs` - Canonical reference search
- `IdSearchTests.cs` - Resource ID search
- `SortTests.cs` - Sort order validation
- `ChainingSearchTests.cs` - Chained search (Patient.name.family)
- `ChainingAndSortTests.cs` - Combined chaining and sorting
- `IncludeSearchTests.cs` - _include and _revinclude
- `IncludesOperationTests.cs` - Advanced include operations
- `NotExpressionTests.cs` - :not modifier
- `NotReferencedSearchTests.cs` - Resources not referenced by others
- `EscapeCharactersSearchTests.cs` - Special character handling
- `TokenOverflowTests.cs` - Large token value handling
- `CustomSearchParamTests.cs` - Custom search parameter definitions

**Patch Operations (Priority 2):**
- `JsonPatchTests.cs` - JSON Patch (RFC 6902)
- `FhirPathPatchTests.cs` - FHIRPath Patch operations

**Validation (Priority 2):**
- `ValidateTests.cs` - $validate operation against profiles

**Bulk Operations (Priority 3):**
- `Export/ExportTests.cs` - System-level $export
- `Export/ExportDataTests.cs` - Patient/Group-level export
- `Export/ExportDataValidationTests.cs` - Export validation rules
- `Export/AnonymizedExportTests.cs` - De-identified export
- `Export/AnonymizedExportUsingAcrTests.cs` - ACR-based anonymization
- `Import/ImportTests.cs` - Bulk resource import
- `Import/Import*SearchTests.cs` (10 files) - Search after import validation
- `Import/ImportHistorySoftDeleteTests.cs` - Version history preservation
- `Import/ImportRebuildIndexesTests.cs` - Search index rebuild
- `BulkUpdateTests.cs` - Bulk update operations
- `BulkDeleteTests.cs` - Bulk delete operations

**Advanced Operations (Priority 4-6):**
- `EverythingOperationTests.cs` - Patient/$everything
- `MemberMatchTests.cs` - Patient/$member-match
- `ConvertDataTests.cs` - $convert-data operation
- `CustomConvertDataTests.cs` - Custom conversion templates
- `Conformance/DocRefOperationTests.cs` - DocumentReference operations
- `ObservationResolveReferenceTests.cs` - Reference resolution

**Compartments (Priority 5):**
- `CompartmentTests.cs` - Patient/Encounter compartment search

**Metadata & Conformance (Priority 1):**
- `MetadataTests.cs` - CapabilityStatement endpoint
- `OperationVersionsTests.cs` - Version-specific operation support

**Security & Authorization (Priority 7):**
- `BasicAuthTests.cs` - Basic authentication
- `Audit/AuditTests.cs` - Audit logging validation
- `Shared.Tests.Smart/SmartProxy/*` - SMART on FHIR authorization flows

**Infrastructure (All Phases):**
- `HealthTests.cs` - Health check endpoints
- `MetricTests.cs` - Performance metrics
- `CorsTests.cs` - CORS configuration
- `CustomHeadersTests.cs` - Custom HTTP headers
- `ExceptionTests.cs` - Error handling and OperationOutcome

**Conformance Validation (Priority 8):**
- `Shared.Tests.Crucible/*` - HL7 Crucible test suite integration

### Integration Test Coverage

**Storage Layer Tests:**
- `FhirStorageTests.cs` - Core storage CRUD operations
- `FhirStorageVersioningPolicyTests.cs` - Version management policies
- `SqlComplexQueryTests.cs` - Complex SQL query generation
- `SearchParameterStatusDataStoreTests.cs` - Search parameter lifecycle

**Background Operations:**
- `Reindex/ReindexJobTests.cs` - Reindex job execution
- `Reindex/ReindexSearchTests.cs` - Search after reindex validation
- `Export/CreateExportRequestHandlerTests.cs` - Export job creation
- `Export/CosmosDbExportTests.cs` - CosmosDB export-specific tests
- `Export/SqlServerExportTests.cs` - SQL Server export-specific tests

**Change Feed:**
- `ChangeFeed/SqlServerFhirResourceChangeCaptureEnabledTests.cs`
- `ChangeFeed/SqlServerFhirResourceChangeCaptureDisabledTests.cs`

**Infrastructure:**
- `QueueClientTests.cs` - Task queue operations
- `SqlRetryServiceTests.cs` - SQL retry logic
- `SearchParameterOptimisticConcurrencyIntegrationTests.cs` - Concurrency handling

## Test Success Criteria for V2

**Phase 1 - Hello FHIR (Basic CRUD):**
- ✅ CreateTests.cs - All basic create scenarios
- ✅ ReadTests.cs - All read scenarios
- ✅ UpdateTests.cs - All update scenarios
- ✅ DeleteTests.cs - All delete scenarios
- ✅ VReadTests.cs - Versioned reads
- ✅ HistoryTests.cs - Resource history
- ✅ MetadataTests.cs - CapabilityStatement
- ✅ HealthTests.cs - Health checks

**Phase 2 - Bundle Processing:**
- ✅ BundleTransactionTests.cs - ALL transaction tests pass (SqlServer only)
- ✅ BundleBatchTests.cs - ALL batch tests pass
- ✅ BundleEdgeCaseTests.cs - ALL edge case tests pass
- ⚠️ Note: CosmosDB transaction support is NOT required (returns 405 MethodNotAllowed in legacy)

**Phase 3 - Search Foundation:**
- ✅ BasicSearchTests.cs - Multi-parameter search
- ✅ StringSearchTests.cs - String search with modifiers
- ✅ TokenSearchTests.cs - Token/code search
- ✅ ReferenceSearchTests.cs - Reference search
- ✅ DateSearchTests.cs - Date range search
- ✅ SortTests.cs - Result sorting

**Phase 4 - Conditional Operations:**
- ✅ ConditionalCreateTests.cs - ALL tests pass
- ✅ ConditionalUpdateTests.cs - ALL tests pass
- ✅ ConditionalDeleteTests.cs - ALL tests pass

**Phase 5 - Advanced Search:**
- ✅ NumberSearchTests.cs
- ✅ QuantitySearchTests.cs
- ✅ CompositeSearchTests.cs
- ✅ ChainingSearchTests.cs
- ✅ IncludeSearchTests.cs
- ✅ CustomSearchParamTests.cs

**Phase 6 - Patch Operations:**
- ✅ JsonPatchTests.cs
- ✅ FhirPathPatchTests.cs
- ✅ ConditionalPatchTests.cs

**Phase 7 - Validation:**
- ✅ ValidateTests.cs - $validate operation

**Phase 8 - Bulk Export:**
- ✅ Export/ExportTests.cs
- ✅ Export/ExportDataTests.cs
- ✅ Export/ExportDataValidationTests.cs

**Phase 9 - Bulk Import:**
- ✅ Import/ImportTests.cs
- ✅ All Import/*SearchTests.cs - Verify search works after import

**Phase 10 - Advanced Operations:**
- ✅ EverythingOperationTests.cs
- ✅ MemberMatchTests.cs
- ✅ ConvertDataTests.cs

**Phase 11+ - Production Readiness:**
- ✅ AuditTests.cs - Audit logging
- ✅ BasicAuthTests.cs - Authentication
- ✅ SmartProxy/* - SMART on FHIR
- ✅ MetricTests.cs - Metrics collection
- ✅ ExceptionTests.cs - Error handling
- ✅ CorsTests.cs - CORS support

**Final Gate - Crucible Conformance:**
- ✅ Shared.Tests.Crucible/* - HL7 Crucible test suite (validates FHIR conformance)

## Definition of Done

**The FHIR Server v2 implementation is complete when:**

1. **All E2E tests from src-old/test pass against v2 implementation**
   - Tests run via: `dotnet test src-old/test/**/*.E2E.csproj`
   - Minimum 80% of tests passing per phase before moving to next phase
   - 100% of tests passing before declaring production-ready

2. **All integration tests pass**
   - Storage layer tests pass for in-memory provider
   - Background operation tests pass
   - Optional: CosmosDB/SQL Server-specific tests pass if those providers are implemented

3. **Test execution requirements:**
   - Developer can run `dotnet test` locally with F5 experience (no external dependencies)
   - All tests complete in under 10 minutes for rapid feedback
   - Tests run in parallel where possible

4. **Test migration strategy:**
   - Copy E2E tests from src-old/test to new test projects
   - Update namespace imports to reference v2 assemblies
   - Maintain test logic unchanged - if test behavior changes, implementation is wrong
   - Keep legacy tests in src-old as gold standard until v2 achieves 100% parity

## Implementation Notes

- **Version-Specific Tests**: Legacy has separate E2E projects per FHIR version (R4, R4B, R5, STU3). V2 should run shared tests against all versions using a single parameterized test suite.
- **Storage-Specific Tests**: Tests use `[HttpIntegrationFixtureArgumentSets(DataStore.All)]` attribute to run against CosmosDB, SqlServer, and in-memory stores. V2 should start with in-memory only.
- **Test Fixtures**: Legacy uses `HttpIntegrationTestFixture` that spins up full ASP.NET Core host. V2 should maintain this approach for true E2E validation.
- **Sample Data**: Tests use `Samples.GetJsonSample<T>()` and `Samples.GetDefaultX()` methods. V2 should reuse these sample resource generators.
- **Authentication**: Many tests use `TestFhirClient` with authentication disabled for simplicity. V2 should support both authenticated and unauthenticated test modes.

This comprehensive test inventory ensures v2 achieves 100% feature parity with legacy before replacing it in production.
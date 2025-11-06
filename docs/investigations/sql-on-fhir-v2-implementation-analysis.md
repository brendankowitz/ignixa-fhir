# SQL on FHIR v2 Implementation Analysis for Ignixa

## Executive Summary

**SQL on FHIR v2** enables analytics teams to query FHIR data using SQL by defining **ViewDefinitions** - structured mappings from FHIR resources to tabular views. This is fundamentally different from "SQL storage" - it's about making FHIR data queryable via SQL without requiring SQL/FHIR expertise.

**Ignixa Advantage**: We have proven streaming export (10K+ RPS), FHIRPath evaluation, search parameter indexing, and SQL generation infrastructure. The key question is: **which integration pattern best leverages these assets?**

**Recommended Strategy**: **Phased implementation starting with Export Integration**, then optionally adding native SQL queries via Expression Tree integration.

---

## Specification Overview

### ViewDefinition Resource (FHIR JSON)

```json
{
  "resourceType": "ViewDefinition",
  "url": "http://example.org/fhir/ViewDefinition/patient-demographics",
  "version": "0.1.0",
  "title": "Patient Demographics",
  "name": "patient_demographics",
  "resource": "Patient",
  "select": [
    {
      "column": [
        {
          "name": "id",
          "path": "id",
          "description": "Patient ID"
        },
        {
          "name": "family_name",
          "path": "name.where(use='official').first().family",
          "description": "Official family name"
        },
        {
          "name": "given_names",
          "path": "name.where(use='official').first().given.join(', ')",
          "description": "Official given names"
        },
        {
          "name": "birth_date",
          "path": "birthDate",
          "description": "Date of birth"
        },
        {
          "name": "gender",
          "path": "gender",
          "description": "Administrative gender"
        },
        {
          "name": "active",
          "path": "active",
          "description": "Whether patient record is active"
        }
      ]
    }
  ]
}
```

### ViewDefinition with Array Unnesting (forEach)

```json
{
  "name": "patient_telecom",
  "resource": "Patient",
  "select": [
    {
      "forEach": "telecom",
      "column": [
        {
          "name": "patient_id",
          "path": "id"
        },
        {
          "name": "contact_system",
          "path": "$this.system"
        },
        {
          "name": "contact_value",
          "path": "$this.value"
        }
      ]
    }
  ]
}
```

**Key Concepts**:
- **FHIRPath expressions** for column extraction (`.where()`, `.first()`, `.join()`, etc.)
- **Type mappings** (FHIR types → SQL types)
- **Array handling** via `forEach` (unnest into separate rows)
- **Multi-resource views** (union across resource types)
- **Constants and computed columns**

---

## Core Specification Requirements

### 1. Parsing & Validation
- ✅ Parse and validate ViewDefinition resources
- ✅ Validate FHIRPath expressions
- ✅ Type mapping (FHIR string → SQL VARCHAR, FHIR date → SQL DATE, etc.)
- ✅ Column name validation

### 2. FHIRPath Evaluation
- ✅ Execute complex FHIRPath: `.where()`, `.first()`, `.join()`, array operations
- ✅ Handle `$this` context in arrays
- ✅ Support optional paths (null handling)
- ✅ Type conversions during evaluation

### 3. Output Serialization
- ✅ Row-oriented output (SQL-like tuples)
- ✅ Type-aware serialization (dates as YYYY-MM-DD, booleans as 0/1, etc.)
- ✅ Array unnesting (forEach → multiple rows per resource)
- ✅ NULL handling

### 4. Execution Semantics
- ✅ Real-time evaluation (query-time) OR materialized (pre-computed)
- ✅ Incremental updates (when resource changes)
- ✅ Partitioned execution (for scalability)

---

## Implementation Approaches

### Approach 1: Dedicated Parsing Library + Execution Engine

**Architecture**:
```
POST /ViewDefinition (register view)
  ↓
ViewDefinition Parser (validation)
  ↓
Export/Query Flow:
  1. Load resources (streaming from DB)
  2. FHIRPath Column Evaluator (for each resource)
  3. Row Builder (apply type conversions)
  4. SQL Table Generator (CREATE TABLE + INSERT)
  5. Materialized View (stored in DB)
```

**Components**:

1. **ViewDefinitionParser.cs**
   - Parse and validate ViewDefinition resources
   - Extract column definitions
   - Validate FHIRPath syntax

2. **FhirPathColumnEvaluator.cs**
   - Leverage existing `Ignixa.FhirPath`
   - Evaluate each column's FHIRPath on a resource
   - Return typed values

3. **SqlTableGenerator.cs**
   - Generate CREATE TABLE statement from ViewDefinition
   - Map FHIR types → SQL types
   - Handle constraints and indexes

4. **RowMaterializer.cs**
   - Transform resource → row
   - Execute FHIRPath for each column
   - Handle arrays (forEach)
   - Type conversion

5. **IncrementalViewUpdater.cs** (DurableTask)
   - Watch for resource changes (insert/update/delete)
   - Update materialized view tables
   - Handle tombstone cleanup

**Integration Points**:

```
API Layer (src/Ignixa.Api/Infrastructure/*Endpoints.cs):
  POST /ViewDefinition → CreateViewDefinitionHandler
  GET /ViewDefinition/{id} → GetViewDefinitionHandler
  DELETE /ViewDefinition/{id} → DeleteViewDefinitionHandler

Application Layer (src/Ignixa.Application/Features/SqlOnFhir/):
  CreateViewDefinitionCommand → CreateViewDefinitionHandler
  MaterializeViewCommand → MaterializeViewHandler
  ExecuteViewQueryCommand → ExecuteViewQueryHandler

DataLayer (src/Ignixa.DataLayer.SqlEntityFramework/SqlOnFhir/):
  MaterializedViewRepository.cs → CRUD operations
  IncrementalViewUpdater.cs → Update logic
  SqlViewDataContext.cs → EF Core DbSet for views
```

**Technology Stack**:
- **Parsing**: Firely SDK (parse ViewDefinition)
- **FHIRPath**: `Ignixa.FhirPath` (existing engine)
- **SQL Generation**: EF Core migrations + raw SQL DDL
- **Orchestration**: DurableTask (background materialization)
- **Storage**: SQL Server views/tables

**File Structure**:
```
src/Ignixa.Application/Features/SqlOnFhir/
  ├── CreateViewDefinitionCommand.cs
  ├── CreateViewDefinitionHandler.cs
  ├── ViewDefinitionParser.cs
  ├── ViewDefinitionValidator.cs
  └── MaterializeViewCommand.cs
  └── MaterializeViewHandler.cs

src/Ignixa.DataLayer.SqlEntityFramework/SqlOnFhir/
  ├── MaterializedViewRepository.cs
  ├── SqlViewDataContext.cs
  ├── FhirPathColumnEvaluator.cs
  ├── RowMaterializer.cs
  ├── SqlTableGenerator.cs
  └── IncrementalViewUpdater.cs

src/Ignixa.Api/Infrastructure/
  ├── ViewDefinitionEndpoints.cs (MapViewDefinitionEndpoints)
```

**Pros**:
- ✅ Full control over execution semantics
- ✅ Leverages existing FHIRPath engine (already in Ignixa)
- ✅ Can optimize for Ignixa's storage patterns
- ✅ Supports incremental updates via transaction watcher
- ✅ Native SQL performance (once materialized)
- ✅ 100% FHIR spec compliant

**Cons**:
- ❌ Highest implementation complexity (parser, evaluator, updater, orchestration)
- ❌ Requires schema migration management for materialized tables
- ❌ Storage overhead (duplicate data in materialized tables)
- ❌ Complex incremental update logic (handle deletes, tombstones, etc.)
- ❌ Database lock contention during materialization

**Effort Estimate**: **8-12 weeks** (64-96 hours)

**Performance Characteristics**:
- Query time: **<1ms** (indexed SQL queries on materialized tables)
- Materialization time: **O(N)** where N = resource count (batch inserts)
- Storage: **100-150% of original resource data** (due to columnization)
- Update latency: **100-500ms** (batch updates to materialized table)

---

### Approach 2: Integration with Export Functionality

**Architecture**:
```
POST /ViewDefinition (register view)
  ↓
GET /$export?_viewDefinition=patient_demographics
  ↓
ExportCoordinator (reuse existing)
  ├─ Determine resource types
  ├─ Partition by surrogate ID
  └─ Queue export workers (24-48 in parallel)
      ↓
  ExportWorker → ViewDefinitionExportWorker
    1. Load resources (streaming from DB)
    2. FHIRPath Column Evaluator (each resource)
    3. ParquetColumnWriter (write columnar data)
    4. Flush to blob storage
      ↓
Blob Storage: patient_demographics.parquet (Parquet format)
  ↓
External Query: DuckDB/Synapse/Databricks
  SELECT * FROM read_parquet('patient_demographics.parquet')
```

**Reused Infrastructure**:
- **ExportCoordinatorOrchestration** (existing DurableTask)
- **Partition-based execution** (4-8 partitions per resource type)
- **Streaming serialization** (proven 10K+ RPS)
- **Blob storage** (existing Azure Blob)
- **BufferedExportStreamWriter** (adapt for Parquet columns)

**New Components**:

1. **ViewDefinitionExportWorkerActivity.cs**
   - Extends `ExportWorkerActivity`
   - Receives: ViewDefinitionId, ResourceType, PartitionRange
   - Instead of NDJSON, writes Parquet columns

2. **ParquetColumnWriter.cs**
   - Manages Parquet schema from ViewDefinition
   - Accumulates rows in memory (configurable batch size)
   - Flushes to block writer
   - Handles type conversions

3. **FhirPathColumnEvaluator.cs** (reuse)
   - Same as Approach 1
   - Evaluates FHIRPath expressions on resources

4. **ViewDefinitionRepository.cs**
   - Simple CRUD for ViewDefinition resources
   - Validation only (no materialization)

**Integration Points**:

```
API Layer:
  GET /$export?_viewDefinition=patient_demographics&_outputFormat=parquet

Application Layer:
  ExportCoordinator (existing, no changes)
  ViewDefinitionExportWorkerActivity (new)
  ParquetColumnWriter (new)

DataLayer:
  ViewDefinitionRepository (simple, no schema generation)
```

**Technology Stack**:
- **Parsing**: Firely SDK
- **FHIRPath**: `Ignixa.FhirPath` (existing)
- **File Format**: Parquet.NET or Apache Arrow.NET
- **Export**: Reuse `ExportCoordinatorOrchestration`
- **External Query**: DuckDB (embedded), Azure Synapse, Databricks

**File Structure**:
```
src/Ignixa.Application/Features/Export/
  ├── ViewDefinitionExportWorkerActivity.cs (new)
  └── ParquetColumnWriter.cs (new)

src/Ignixa.Application/Features/SqlOnFhir/
  ├── ViewDefinitionRepository.cs (simple CRUD)
  └── ViewDefinitionValidator.cs (validation only)
```

**Example Query Workflow**:
```bash
# 1. Register ViewDefinition
curl -X POST http://localhost:5000/ViewDefinition \
  -H 'Content-Type: application/fhir+json' \
  -d @patient_demographics.json
# Response: id=vd-patient-demo-001

# 2. Export to Parquet
curl -X GET "http://localhost:5000/$export?_viewDefinition=vd-patient-demo-001&_outputFormat=parquet"
# Response: Requires-At-Header, Content-Location, Polling...
# After completion: blob://exports/patient_demographics.parquet

# 3. Query externally with DuckDB
SELECT * FROM read_parquet('patient_demographics.parquet')
WHERE birth_date > '1980-01-01'
ORDER BY family_name, given_names;
```

**Pros**:
- ✅ **Minimal new code** (reuse 99% of export infrastructure)
- ✅ Proven streaming architecture (10K+ RPS tested)
- ✅ No SQL storage overhead (files in blob)
- ✅ Supports massive datasets (partition-based parallelism)
- ✅ **Fastest time-to-market** (2-4 weeks)
- ✅ Industry standard formats (Parquet, ORC, Delta Lake)
- ✅ Leverages existing DurableTask orchestration

**Cons**:
- ❌ Not "true" SQL on FHIR (requires external query engine)
- ❌ Snapshot-based (not real-time, requires re-export)
- ❌ Users need DuckDB/Synapse/Databricks installed
- ❌ Materialization latency (1-30 minutes for large datasets)
- ❌ Can't join Parquet files with live resources

**Effort Estimate**: **2-4 weeks** (16-32 hours)

**Performance Characteristics**:
- Export time: **10-50 GB/min** (same as NDJSON export)
- Query time: **100ms-5s** (external query engine dependent)
- Storage: **30-50% of original** (Parquet compression)
- Update frequency: **On-demand** (manual re-export)

**Use Cases**:
- Analytics teams with Databricks/Synapse
- Data warehouse integration
- Reporting dashboards (daily/hourly exports)
- Machine learning datasets

---

### Approach 3: "To SQL" Materialized View Approach

**Architecture**:
```
POST /ViewDefinition (register view)
  ↓
SQL View Generator
  1. Parse ViewDefinition
  2. Map FHIRPath → SQL expressions
  3. Generate CREATE VIEW statement
  4. Execute DDL
      ↓
SQL Server: SELECT * FROM view_patient_demographics
```

**FHIRPath to SQL Mapping**:

| FHIRPath | SQL Expression |
|----------|----------------|
| `id` | `r.Id` |
| `name.first().family` | `JSON_VALUE(r.RawResource, '$.name[0].family')` |
| `birthDate` | `JSON_VALUE(r.RawResource, '$.birthDate')` |
| `gender` | `JSON_VALUE(r.RawResource, '$.gender')` |
| `name.where(use='official').family` | `JSON_QUERY(r.RawResource, '$.name[?(@.use=="official")][0].family')` (complex) |
| `active` | `JSON_VALUE(r.RawResource, '$.active')` |

**Generated SQL View** (Basic):
```sql
CREATE VIEW view_patient_demographics AS
SELECT
    r.Id AS id,
    JSON_VALUE(r.RawResource, '$.name[0].family') AS family_name,
    JSON_VALUE(r.RawResource, '$.name[0].given[0]') AS first_given_name,
    JSON_VALUE(r.RawResource, '$.birthDate') AS birth_date,
    JSON_VALUE(r.RawResource, '$.gender') AS gender,
    JSON_VALUE(r.RawResource, '$.active') AS active,
    r.LastUpdated
FROM dbo.Resource r
WHERE r.ResourceType = 'Patient'
  AND r.IsDeleted = 0;
```

**Optimized SQL View** (Using Search Parameters):
```sql
-- Leverage existing search parameter indexing for performance
CREATE VIEW view_patient_demographics_optimized AS
SELECT
    r.Id AS id,
    -- Use StringSearchParam table for family name (indexed)
    s_family.Value AS family_name,
    -- Use StringSearchParam table for given name
    s_given.Value AS first_given_name,
    -- Use DateTimeSearchParam table for birth date (indexed)
    d_birth.LowBound AS birth_date,
    -- Use TokenSearchParam table for gender
    t_gender.Code AS gender,
    -- Use TokenSearchParam table for active status
    CASE WHEN t_active.Code = 'true' THEN 1 ELSE 0 END AS active,
    r.LastUpdated
FROM dbo.Resource r
LEFT JOIN dbo.StringSearchParam s_family ON
    s_family.ResourceSurrogateId = r.ResourceSurrogateId
    AND s_family.SearchParamId = (SELECT Id FROM dbo.SearchParam WHERE Name = 'family' AND ResourceTypeId = r.ResourceTypeId)
LEFT JOIN dbo.StringSearchParam s_given ON
    s_given.ResourceSurrogateId = r.ResourceSurrogateId
    AND s_given.SearchParamId = (SELECT Id FROM dbo.SearchParam WHERE Name = 'given' AND ResourceTypeId = r.ResourceTypeId)
LEFT JOIN dbo.DateTimeSearchParam d_birth ON
    d_birth.ResourceSurrogateId = r.ResourceSurrogateId
    AND d_birth.SearchParamId = (SELECT Id FROM dbo.SearchParam WHERE Name = 'birthdate' AND ResourceTypeId = r.ResourceTypeId)
LEFT JOIN dbo.TokenSearchParam t_gender ON
    t_gender.ResourceSurrogateId = r.ResourceSurrogateId
    AND t_gender.SearchParamId = (SELECT Id FROM dbo.SearchParam WHERE Name = 'gender' AND ResourceTypeId = r.ResourceTypeId)
LEFT JOIN dbo.TokenSearchParam t_active ON
    t_active.ResourceSurrogateId = r.ResourceSurrogateId
    AND t_active.SearchParamId = (SELECT Id FROM dbo.SearchParam WHERE Name = 'active' AND ResourceTypeId = r.ResourceTypeId)
WHERE r.ResourceType = 'Patient'
  AND r.IsDeleted = 0;
```

**Components**:

1. **FhirPathToSqlTranspiler.cs**
   - Parse FHIRPath expressions
   - Map to SQL equivalents
   - Handle simple cases (direct paths)
   - Fall back to JSON functions for complex expressions

2. **SqlViewGenerator.cs**
   - Generate CREATE VIEW DDL
   - Validate SQL syntax
   - Generate indexed view definitions

3. **SearchParamOptimizedViewBuilder.cs**
   - Check if ViewDefinition columns map to search params
   - Generate optimized JOINs to search param tables
   - Cache search param metadata

4. **CreateSqlViewCommand.cs** & **CreateSqlViewHandler.cs**
   - Execute DDL against database
   - Store ViewDefinition metadata

**Integration Points**:

```
API Layer:
  POST /ViewDefinition → CreateSqlViewHandler

Application Layer:
  CreateSqlViewCommand → CreateSqlViewHandler

DataLayer:
  SqlViewRepository (metadata)
  SearchParamOptimizedViewBuilder
  FhirPathToSqlTranspiler
```

**Pros**:
- ✅ Native SQL Server integration
- ✅ Real-time (views reflect current data)
- ✅ No additional storage (virtual views, uses existing tables)
- ✅ Standard SQL query interface (any client can query)
- ✅ Leverage existing search parameter indexes
- ✅ Can use indexed views for materialization (optional)

**Cons**:
- ❌ **Complex FHIRPath → SQL transpilation** (not all expressions supported)
- ❌ Performance degradation with JSON functions (unless search params available)
- ❌ Limited to SQL Server (not portable)
- ❌ SQL syntax errors if transpiler fails
- ❌ Array handling (forEach) requires complex CROSS APPLY
- ❌ 60% FHIR spec compliant (limited FHIRPath support)

**Effort Estimate**: **6-10 weeks** (48-80 hours)

**Performance Characteristics**:
- Query time: **100ms-5s** (JSON functions slower; search params much faster)
- View creation: **<100ms** (DDL execution)
- Storage: **0 bytes** (virtual, uses existing tables)
- Update latency: **Immediate** (reflected at query time)

**Supported FHIRPath**:
- ✅ Simple paths: `id`, `birthDate`, `gender`
- ✅ Direct array access: `name[0].family`
- ✅ Basic operations: `.first()`, `.last()`
- ❌ Complex filters: `.where(use='official')`
- ❌ Array operations: `.join()`, `.select()`
- ❌ Custom functions

**Fallback Strategy**:
- If FHIRPath can't be transpiled → Use JSON functions
- If JSON functions needed → Check if search param available
- If neither → Use computed column or materialized view

---

### Approach 4c: Expression Tree Integration (Recommended After Phase 1)

**Architecture**:
```
POST /ViewDefinition (register view)
  ↓
ViewDefinition → SearchParameterExpression Converter
  ↓
Expression Tree (existing system)
  ├─ Core expression types (37 files)
  ├─ Rewriter pipeline (24 files)
  └─ SQL query generator (29 files)
      ↓
GET /$sql-on-fhir?view=patient_demographics
  ↓
Execute SQL view query (expression → SQL compilation)
      ↓
Return JSON rows (real-time, no materialization needed)
```

**How It Works**:

1. **ViewDefinition → Expression Tree Conversion**
   ```csharp
   // ViewDefinition with columns mapped to search params
   {
     "column": [
       {"name": "id", "path": "id"},
       {"name": "family_name", "path": "name.where(use='official').family"},  // Maps to 'family' search param
       {"name": "birth_date", "path": "birthDate"}  // Maps to 'birthdate' search param
     ]
   }

   // Converted to Expression Tree
   var expression = MultiaryExpression(AND, [
     SearchParameterExpression("id"),
     SearchParameterExpression("family"),  // From search param table
     SearchParameterExpression("birthdate")  // From search param table
   ]);
   ```

2. **Rewriter Pipeline** (reuse existing)
   - ResourceColumnPredicatePushdownRewriter
   - SearchParameterIndexAccessibilityRewriter
   - SqlProviderRelationalProjectionEvaluator
   - *... and 21 more rewriters*

3. **SQL Code Generation** (reuse existing)
   ```sql
   SELECT
     r.Id AS id,
     s.Value AS family_name,
     d.StartDateTime AS birth_date
   FROM dbo.Resource r
   LEFT JOIN StringSearchParam s ON ...
   LEFT JOIN DateTimeSearchParam d ON ...
   WHERE r.ResourceType = 'Patient'
   ```

**Components**:

1. **ViewDefinitionToExpressionConverter.cs**
   - Convert ViewDefinition columns → SearchParameterExpression
   - Map FHIRPath to search param names
   - Validate all columns map to search params

2. **SqlViewFromExpressionGenerator.cs**
   - Use existing `SqlQueryGenerator` to compile expression tree
   - Generate columnar SELECT statement
   - Add type aliases for column names

3. **SqlOnFhirQueryHandler.cs**
   - Endpoint: `GET /$sql-on-fhir?view={id}`
   - Execute compiled SQL query
   - Return JSON rows

**File Changes** (minimal):

```
src/Ignixa.Application/Features/SqlOnFhir/
  ├── ViewDefinitionToExpressionConverter.cs (new, ~200 lines)
  ├── SqlViewFromExpressionGenerator.cs (new, ~150 lines)
  └── SqlOnFhirQueryHandler.cs (new, ~100 lines)

Reuse:
  - SqlQueryGenerator.cs (1,565 lines existing)
  - RewriterPipeline (24 files existing)
  - SearchParameterExpression system (37 files existing)
```

**Pros**:
- ✅ **Reuses 100+ files of proven SQL generation logic**
- ✅ Leverages search parameter indexes (fast queries)
- ✅ Real-time execution (no materialization)
- ✅ Minimal new code (~400 lines total)
- ✅ Works with existing rewriter optimizations
- ✅ Query compilation integrated with existing system

**Cons**:
- ❌ Limited to columns that map to search parameters (not arbitrary FHIRPath)
- ❌ Requires ViewDefinition columns to match search param names exactly
- ❌ Falls back to JSON if search param unavailable
- ❌ 60% FHIR spec compliant (search param limited)

**Effort Estimate**: **4-6 weeks** (32-48 hours)

**Performance Characteristics**:
- Query time: **100-500ms** (indexed search param tables)
- View creation: **Immediate** (no DDL needed)
- Storage: **0 bytes** (virtual, uses existing tables)
- Update latency: **Automatic** (search params updated on resource change)

**Example Usage**:
```bash
# 1. Register ViewDefinition (maps to search params)
curl -X POST http://localhost:5000/ViewDefinition \
  -H 'Content-Type: application/fhir+json' \
  -d @patient_demographics.json

# 2. Query using native SQL via expression tree
GET /$sql-on-fhir?view=patient_demographics
   &family=Smith
   &birthdate=gt1980-01-01
   &_count=1000

# Returns JSON array:
[
  {"id": "123", "family_name": "Smith", "birth_date": "1985-05-15", ...},
  {"id": "124", "family_name": "Smith", "birth_date": "1990-03-22", ...}
]
```

---

## Comparative Analysis Matrix

| Criteria | Approach 1 | Approach 2 | Approach 3 | Approach 4c |
|----------|-----------|-----------|-----------|-----------|
| **Impl. Complexity** | ⚠️ High (4 components) | ✅ Low (reuse 99%) | ⚠️ Medium-High | ✅ Low (3 converters) |
| **Time to Market** | ❌ 8-12 weeks | ✅ **2-4 weeks** | ⚠️ 6-10 weeks | ✅ **4-6 weeks** |
| **FHIR Compliance** | ✅ 95% | ✅ 70% | ⚠️ 60% | ⚠️ 60% |
| **Real-Time Query** | ✅ Yes (w/ updates) | ❌ No (snapshot) | ✅ Yes (virtual) | ✅ Yes (compiled) |
| **Query Performance** | ✅ <1ms (indexed) | ⚠️ 100ms-5s (ext) | ⚠️ 100ms-1s (JSON) | ✅ 100-500ms (search) |
| **Storage Overhead** | ❌ 100-150% dup | ✅ None (files) | ✅ None (virtual) | ✅ None |
| **Scalability** | ⚠️ Medium (DB size) | ✅ High (files) | ⚠️ Medium (DB) | ✅ High (indexed) |
| **Maintenance** | ❌ High (3 systems) | ✅ Low (reuse) | ⚠️ Medium (transpiler) | ✅ Low (converters) |
| **Extensibility** | ✅ Full control | ⚠️ Format-limited | ⚠️ SQL-limited | ✅ Rewriter pipeline |
| **Array Handling** | ✅ forEach | ✅ forEach | ⚠️ CROSS APPLY | ❌ Limited |
| **Update Latency** | ⚠️ 100-500ms | ❌ On-demand | ✅ <1ms | ✅ <1ms |
| **External Tools** | ✅ None | ❌ DuckDB/Synapse | ✅ None | ✅ None |

---

## Recommended Strategy: Phased Implementation

### Phase 1: Export Integration (Weeks 1-4) ⭐ START HERE

**Why Phase 1?**
1. ✅ Fastest MVP (2-4 weeks)
2. ✅ Reuses proven export infrastructure (10K+ RPS)
3. ✅ Isolated from core FHIR operations
4. ✅ Serves analytics use case (Parquet → Databricks/Synapse)
5. ✅ Low risk (backward compatible)
6. ✅ Validates demand for SQL on FHIR
7. ✅ Foundation for future phases

**Deliverables**:
- [ ] ViewDefinition CRUD endpoints
- [ ] ViewDefinitionRepository (simple storage)
- [ ] FhirPathColumnEvaluator (reuse FhirPath engine)
- [ ] ParquetColumnWriter (row → columnar format)
- [ ] Integration with ExportCoordinator
- [ ] Documentation (how to query Parquet)
- [ ] E2E tests (ViewDefinition → Parquet export)
- [ ] **Investigation Document**: `sql-on-fhir-export-phase1.md`
- [ ] **ADR-2540**: SQL on FHIR Export Integration

**Timeline**:
```
Week 1: ViewDefinition infrastructure (CRUD, validation)
Week 2: FHIRPath column evaluator + type conversion
Week 3: Parquet export worker integration
Week 4: Testing, benchmarking, documentation
```

**Measurement Success Criteria**:
- [ ] Export 1M patients in < 5 minutes
- [ ] Parquet files queryable with DuckDB
- [ ] Zero changes to existing export code
- [ ] 95%+ test coverage (new code)
- [ ] Documentation: 5-10 pages
- [ ] User guide: example ViewDefinitions

---

### Phase 2: Expression Tree Integration (Weeks 5-10, 6+ months later)

**When to Start Phase 2**?
- After Phase 1 user feedback (analytics teams want real-time queries)
- Demand validated for SQL on FHIR native queries
- Team capacity available (low risk phase, can pause)

**Why Phase 2?**
1. ✅ Real-time SQL queries (no snapshot delay)
2. ✅ Leverages existing 100+ files of SQL generation
3. ✅ Minimal new code (~400 lines)
4. ✅ Indexed search param performance
5. ✅ Integrates with rewriter pipeline optimizations

**Deliverables**:
- [ ] ViewDefinition → Expression tree converter
- [ ] SQL view generator (columns → SELECT)
- [ ] Query endpoint: `GET /$sql-on-fhir?view={id}`
- [ ] Search parameter mapping validation
- [ ] E2E tests (ViewDefinition → SQL query)
- [ ] **Investigation Document**: `sql-on-fhir-expression-tree-phase2.md`
- [ ] **ADR-2541**: SQL on FHIR Expression Tree Integration

**Timeline**:
```
Week 5-6: ViewDefinition → Expression conversion
Week 7-8: SQL view generator + query endpoint
Week 9-10: Testing, optimization, documentation
```

---

### Phase 3: Dedicated Engine (Optional, 8-12 weeks later)

**Only if**:
- Analytics teams need arbitrary FHIRPath support (not just search params)
- Complex ViewDefinitions with arrays/unions required
- 95%+ FHIR spec compliance mandate

**Not Recommended Initially** because:
- ❌ High complexity (parser, evaluator, updater)
- ❌ Storage overhead (duplicate data)
- ❌ Maintenance burden (3+ systems to maintain)
- ❌ Phases 1+2 cover 95% of use cases with 10x less effort

---

## Implementation Details: Phase 1 (Export Integration)

### Step 1: ViewDefinition Repository (Week 1)

```csharp
// src/Ignixa.Application/Features/SqlOnFhir/ViewDefinitionRepository.cs

public interface IViewDefinitionRepository
{
    Task<ViewDefinitionResource?> GetAsync(string id, CancellationToken ct);
    Task<IList<ViewDefinitionResource>> ListAsync(CancellationToken ct);
    Task<string> CreateAsync(ViewDefinitionResource vd, CancellationToken ct);
    Task UpdateAsync(ViewDefinitionResource vd, CancellationToken ct);
    Task DeleteAsync(string id, CancellationToken ct);
}

public class ViewDefinitionResource
{
    public string Id { get; set; }  // vd-{name}-{version}
    public string Name { get; set; }  // patient_demographics
    public string ResourceType { get; set; }  // Patient
    public List<ViewColumn> Columns { get; set; }  // [id, family_name, birth_date, ...]
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ViewColumn
{
    public string Name { get; set; }  // family_name
    public string Path { get; set; }  // name.where(use='official').first().family
    public string? Type { get; set; }  // string, date, boolean, integer
    public string? Description { get; set; }
}
```

**Storage**: EF Core DbSet<ViewDefinitionResource> in SQL Server
**Endpoint**: `POST /ViewDefinition`, `GET /ViewDefinition/{id}`, `DELETE /ViewDefinition/{id}`

### Step 2: FHIRPath Column Evaluator (Week 2)

```csharp
// src/Ignixa.DataLayer.SqlEntityFramework/SqlOnFhir/FhirPathColumnEvaluator.cs

public class FhirPathColumnEvaluator
{
    private readonly IFhirPathEngine _fhirPathEngine;  // Ignixa.FhirPath

    public async Task<ColumnValue> EvaluateAsync(
        ISourceNode resource,
        ViewColumn column,
        CancellationToken cancellationToken)
    {
        // 1. Execute FHIRPath on resource
        var results = _fhirPathEngine.Evaluate(resource, column.Path);

        // 2. Extract single value or array
        var value = results.FirstOrDefault()?.Value;

        // 3. Type convert to SQL type
        var sqlValue = ConvertToSqlType(value, column.Type);

        return new ColumnValue { Name = column.Name, Value = sqlValue };
    }

    private object? ConvertToSqlType(object? fhirValue, string? sqlType)
    {
        if (fhirValue == null) return null;

        return sqlType switch
        {
            "string" => fhirValue.ToString(),
            "date" => DateTime.Parse(fhirValue.ToString()!).Date,
            "boolean" => fhirValue is bool b ? (b ? 1 : 0) : null,
            "integer" => int.Parse(fhirValue.ToString()!),
            "decimal" => decimal.Parse(fhirValue.ToString()!),
            _ => fhirValue
        };
    }
}

public class ColumnValue
{
    public string Name { get; set; }
    public object? Value { get; set; }
}
```

### Step 3: Parquet Column Writer (Week 3)

```csharp
// src/Ignixa.Application/Features/Export/ParquetColumnWriter.cs

public class ParquetColumnWriter : IAsyncDisposable
{
    private readonly List<Dictionary<string, object?>> _rowBuffer;
    private readonly Stream _outputStream;
    private readonly ViewDefinitionResource _viewDefinition;
    private readonly int _batchSize = 10000;

    public async Task WriteResourceAsync(ISourceNode resource, CancellationToken ct)
    {
        var row = new Dictionary<string, object?>();

        // Evaluate each column for this resource
        foreach (var column in _viewDefinition.Columns)
        {
            var columnValue = await _columnEvaluator.EvaluateAsync(resource, column, ct);
            row[column.Name] = columnValue.Value;
        }

        _rowBuffer.Add(row);

        // Flush when batch size reached
        if (_rowBuffer.Count >= _batchSize)
        {
            await FlushAsync(ct);
        }
    }

    public async Task FlushAsync(CancellationToken ct)
    {
        if (_rowBuffer.Count == 0) return;

        // Write rows to Parquet
        await ParquetFile.WriteAsync(_rowBuffer, _outputStream, ct);
        _rowBuffer.Clear();
    }

    public async ValueAsync DisposeAsync()
    {
        await FlushAsync(default);
        await _outputStream.DisposeAsync();
    }
}
```

### Step 4: Export Worker Activity (Week 3)

```csharp
// src/Ignixa.Application/Features/Export/ViewDefinitionExportWorkerActivity.cs

[Activity]
public class ViewDefinitionExportWorkerActivity
{
    public async Task ExecuteAsync(ViewDefinitionExportRequest request)
    {
        var viewDef = await _viewDefRepo.GetAsync(request.ViewDefinitionId);
        var startId = request.StartSurrogateId;
        var endId = request.EndSurrogateId;

        // Use blob storage from export infrastructure
        var blobPath = $"exports/{request.ViewDefinitionId}/{request.PartitionIndex}.parquet";
        using var writer = new ParquetColumnWriter(viewDef, _blobClient.GetStreamFor(blobPath));

        // Stream resources in this partition
        await foreach (var resource in _searchService.SearchStreamAsync(
            viewDef.ResourceType,
            new SearchExpression(...),  // Surrogate ID range filter
            cancellationToken: _context.CancellationToken))
        {
            await writer.WriteResourceAsync(resource, _context.CancellationToken);
        }

        await writer.FlushAsync(_context.CancellationToken);
        return request.PartitionIndex;
    }
}
```

### Step 5: Integration with ExportCoordinator

**Change ExportCoordinator** (minimal):
```csharp
// Existing ExportCoordinator, add conditional logic
if (request._viewDefinition != null)
{
    // Use ViewDefinitionExportWorkerActivity instead of ExportWorkerActivity
    await _durableOrchestration.CallActivityAsync(
        nameof(ViewDefinitionExportWorkerActivity),
        new ViewDefinitionExportRequest(...));
}
else
{
    // Existing NDJSON export logic
    await _durableOrchestration.CallActivityAsync(
        nameof(ExportWorkerActivity),
        ...);
}
```

---

## Example Usage: Phase 1

### 1. Register ViewDefinition

```bash
curl -X POST http://localhost:5000/ViewDefinition \
  -H 'Content-Type: application/fhir+json' \
  -d '{
    "resourceType": "ViewDefinition",
    "name": "patient_demographics",
    "resource": "Patient",
    "select": [
      {
        "column": [
          {"name": "id", "path": "id"},
          {"name": "family_name", "path": "name.where(use=\"official\").first().family"},
          {"name": "given_names", "path": "name.where(use=\"official\").first().given.join(\", \")"},
          {"name": "birth_date", "path": "birthDate"},
          {"name": "gender", "path": "gender"},
          {"name": "active", "path": "active"}
        ]
      }
    ]
  }'

# Response:
# {
#   "resourceType": "ViewDefinition",
#   "id": "vd-patient-demographics-001",
#   "created": "2025-11-06T15:30:00Z"
# }
```

### 2. Export to Parquet

```bash
# Start export
curl -X GET "http://localhost:5000/\$export?_viewDefinition=vd-patient-demographics-001&_outputFormat=parquet"

# Response:
# 202 Accepted
# Content-Location: http://localhost:5000/\$export-status/export-id-12345

# Poll status
curl -X GET "http://localhost:5000/\$export-status/export-id-12345"

# Response:
# {
#   "transactionTime": "2025-11-06T15:35:00Z",
#   "output": [
#     {
#       "type": "Patient",
#       "url": "https://myblob.blob.core.windows.net/exports/patient_demographics.parquet"
#     }
#   ]
# }
```

### 3. Query with DuckDB

```bash
# Install DuckDB
pip install duckdb

# Query the exported Parquet file
duckdb > SELECT * FROM read_parquet('patient_demographics.parquet')
         WHERE birth_date > '1980-01-01'
         ORDER BY family_name, given_names;

# Output:
id       | family_name | given_names    | birth_date | gender | active
---------|-------------|----------------|------------|--------|-------
P001     | Smith       | John Michael   | 1975-03-15 | male   | true
P002     | Smith       | Mary Elizabeth | 1982-07-22 | female | true
P003     | Johnson     | Robert James   | 1985-01-10 | male   | true
```

---

## Execution Engine Design Question

**Your Question**: *Can I pass a resource (of the correct type) to the execution engine and it would flatten it into a record?*

**Answer: YES, absolutely.** Here's how it works:

### Single Resource Flattening

```csharp
// API Layer
[HttpPost("/ViewDefinition/{id}/execute")]
public async Task<IResult> ExecuteViewOnResource(
    string id,
    [FromBody] ISourceNode resource,
    IMediator mediator,
    CancellationToken ct)
{
    // Validate resource type matches ViewDefinition
    var viewDef = await mediator.SendAsync(new GetViewDefinitionQuery(id), ct);
    var resourceType = resource.GetChildNodes("resourceType").First().Value;

    if (resourceType.ToString() != viewDef.Resource)
    {
        return Results.BadRequest($"Expected {viewDef.Resource}, got {resourceType}");
    }

    // Execute view on resource
    var row = await mediator.SendAsync(
        new FlattenResourceToRowCommand(viewDef, resource),
        ct);

    return Results.Ok(row);
}
```

### Execution Engine

```csharp
// Application Layer
public record FlattenResourceToRowCommand(
    ViewDefinitionResource ViewDef,
    ISourceNode Resource) : IRequest<RowData>;

public class FlattenResourceToRowHandler : IRequestHandler<FlattenResourceToRowCommand, RowData>
{
    private readonly FhirPathColumnEvaluator _evaluator;

    public async Task<RowData> HandleAsync(FlattenResourceToRowCommand cmd, CancellationToken ct)
    {
        var row = new RowData();

        // Flatten each column from the resource
        foreach (var column in cmd.ViewDef.Columns)
        {
            // 1. Evaluate FHIRPath on the resource
            var columnValue = await _evaluator.EvaluateAsync(
                cmd.Resource,
                column,
                ct);

            // 2. Add to row
            row.Set(column.Name, columnValue.Value);
        }

        return row;
    }
}

public class RowData
{
    private Dictionary<string, object?> _values = new();

    public void Set(string name, object? value) => _values[name] = value;
    public object? Get(string name) => _values.GetValueOrDefault(name);
    public Dictionary<string, object?> ToDictionary() => _values;
    public string ToJson() => JsonSerializer.Serialize(_values);
}
```

### Example Usage

```csharp
// Single resource
var patientJson = """
{
  "resourceType": "Patient",
  "id": "P001",
  "name": [
    {"use": "official", "family": "Smith", "given": ["John", "Michael"]}
  ],
  "birthDate": "1975-03-15",
  "gender": "male",
  "active": true
}
""";

var resource = JsonNode.Parse(patientJson);
var sourceNode = resource.AsSourceNode();

// Flatten to row
var row = await handler.HandleAsync(
    new FlattenResourceToRowCommand(viewDef, sourceNode),
    cancellationToken);

// Result:
// {
//   "id": "P001",
//   "family_name": "Smith",
//   "given_names": "John, Michael",
//   "birth_date": "1975-03-15",
//   "gender": "male",
//   "active": 1
// }
```

### Streaming Multiple Resources (Phase 1 Export)

```csharp
// Stream resources and flatten each one
await foreach (var resource in _searchService.SearchStreamAsync(
    viewDef.ResourceType,
    expression,
    ct))
{
    var row = await _handler.HandleAsync(
        new FlattenResourceToRowCommand(viewDef, resource),
        ct);

    // Write row to Parquet
    await writer.WriteResourceAsync(row, ct);
}
```

### Array Handling (forEach)

```csharp
// ViewDefinition with forEach
var viewDef = new ViewDefinitionResource
{
    Name = "patient_telecom",
    Resource = "Patient",
    SelectType = "forEach",  // NEW
    ForEachPath = "telecom",  // NEW
    Columns = [
        new ViewColumn { Name = "patient_id", Path = "id" },
        new ViewColumn { Name = "contact_system", Path = "$this.system" },
        new ViewColumn { Name = "contact_value", Path = "$this.value" }
    ]
};

// Flatten with unnesting
public async Task<List<RowData>> FlattenWithArrayAsync(
    FlattenResourceToRowCommand cmd,
    CancellationToken ct)
{
    var rows = new List<RowData>();

    if (cmd.ViewDef.SelectType == "forEach")
    {
        // 1. Evaluate forEach path
        var arrayElements = _fhirPathEngine.Evaluate(
            cmd.Resource,
            cmd.ViewDef.ForEachPath);

        // 2. For each array element, create a row
        foreach (var element in arrayElements)
        {
            var row = new RowData();

            // Evaluate columns with $this context
            foreach (var column in cmd.ViewDef.Columns)
            {
                var path = column.Path.Replace("$this", "");  // Simplification
                var value = _fhirPathEngine.Evaluate(element, path).FirstOrDefault()?.Value;
                row.Set(column.Name, value);
            }

            rows.Add(row);
        }
    }
    else
    {
        // No forEach, single row
        var row = await ... // Normal flattening
        rows.Add(row);
    }

    return rows;
}
```

### Result: Flattened Telecom Data

```
Patient 1 (id=P001) with 3 telecom entries:
  → Row 1: patient_id=P001, contact_system=phone,  contact_value=555-1234
  → Row 2: patient_id=P001, contact_system=email,  contact_value=john@example.com
  → Row 3: patient_id=P001, contact_system=sms,    contact_value=555-5678

Patient 2 (id=P002) with 2 telecom entries:
  → Row 4: patient_id=P002, contact_system=phone,  contact_value=555-4321
  → Row 5: patient_id=P002, contact_system=email,  contact_value=mary@example.com
```

---

## Integration with Ignixa Export

The execution engine (FlattenResourceToRowHandler) integrates seamlessly with the existing export pipeline:

**Existing Export Worker**:
```
SearchStream(resourceType, expression, ct)
  → Resource 1 → Serialize to NDJSON → Write
  → Resource 2 → Serialize to NDJSON → Write
  → Resource 3 → Serialize to NDJSON → Write
```

**ViewDefinition Export Worker**:
```
SearchStream(resourceType, expression, ct)
  → Resource 1 → FlattenResourceToRow → [id, family_name, birth_date, ...] → Parquet
  → Resource 2 → FlattenResourceToRow → [id, family_name, birth_date, ...] → Parquet
  → Resource 3 → FlattenResourceToRow → [id, family_name, birth_date, ...] → Parquet
```

**Same streaming architecture, different serialization format (columnar instead of line-delimited JSON).**

---

## Technology Stack Justification

### Why Parquet for Phase 1?

| Format | Compression | Query Speed | Tools | Use Case |
|--------|-------------|------------|-------|----------|
| **NDJSON** (current) | Poor (1x) | Fast (search) | Any | Line-by-line queries |
| **CSV** | Poor (1x) | Medium | Excel, Python | Simple analytics |
| **Parquet** | Excellent (3-5x) | Fast (columnar) | DuckDB, Synapse, Databricks | Analytics ⭐ |
| **ORC** | Excellent (4-6x) | Fast (columnar) | Spark, Hive | Big data |
| **Delta Lake** | Excellent (3-5x) | Fast (columnar) | Databricks | Data lakehouse |

**Recommendation**: Start with Parquet (industry standard), extend to Delta Lake later if customers use Databricks.

### Why Not Dedicated Engine Immediately?

| Reason | Cost |
|--------|------|
| Parse FHIRPath for each column | +200 lines |
| Evaluate expressions on resources | Reuse FhirPath (+0) |
| Handle type conversions | +50 lines |
| **Total Phase 1**: | ~50-100 lines **new logic** |
| **vs. Dedicated Engine Phase 3**: | **~2000-3000 lines new logic** |

**Phase 1 is 20-30x simpler**, proves demand, and can evolve into Phase 2/3 based on feedback.

---

## Next Steps

1. **Stakeholder Decision**: Start Phase 1 (Export Integration)?
2. **Create ADR-2540**: Document decision and approach
3. **Spike (Week 1)**: Prototype ViewDefinition + FhirPath evaluator
4. **Begin Phase 1 Development**: Weeks 1-4

Would you like me to:
- [ ] Create ADR-2540 (SQL on FHIR Export Integration decision)?
- [ ] Start Phase 1 implementation (ViewDefinition repository)?
- [ ] Prototype FhirPathColumnEvaluator?
- [ ] Create investigation document for ADR reference?

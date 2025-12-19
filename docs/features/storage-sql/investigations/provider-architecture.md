# Investigation: SQL Data Provider Architecture Visualization

**Feature**: sql-storage
**Status**: Viable
**Created**: 2025-10-22
**Original ADR**: N/A

## Five-Layer Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                          HTTP FHIR Request                          │
│              GET /Patient?name=Smith&birthdate=gt2000               │
└─────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────┐
│                    API Layer (MediatR Pattern)                      │
│  FhirController → SearchResourceHandler → ExpressionParser          │
└─────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────┐
│          LAYER 1: Core Expression System (37 files)                 │
│         Microsoft.Health.Fhir.Core/Features/Search/Expressions/     │
│                                                                       │
│  Expression Tree (Abstract, Data Store Independent):                │
│                                                                       │
│  MultiaryExpression(AND, [                                          │
│    SearchParameterExpression(                                       │
│      Parameter: name,                                               │
│      Expression: StringExpression(Contains, "Smith")                │
│    ),                                                               │
│    SearchParameterExpression(                                       │
│      Parameter: birthdate,                                          │
│      Expression: BinaryExpression(GreaterThan, 2000-01-01)          │
│    )                                                                │
│  ])                                                                 │
│                                                                       │
│  Base: Expression.cs, IExpressionVisitor<TContext, TOutput>         │
│  Types: SearchParameterExpression, BinaryExpression,                │
│         StringExpression, ChainedExpression, IncludeExpression,     │
│         NotExpression, MultiaryExpression, etc.                     │
└─────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────┐
│      LAYER 2: SQL-Specific Extensions (15 files)                    │
│       Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/  │
│                                                                       │
│  SQL Expression Tree:                                               │
│                                                                       │
│  SqlRootExpression                                                  │
│  ├── ResourceTableExpressions (direct Resource table queries)       │
│  └── SearchParamTableExpressions (search param table queries)       │
│      ├── SearchParamTableExpression(Kind: Normal)                   │
│      │   └── Predicate: StringExpression(...)                       │
│      └── SearchParamTableExpression(Kind: Normal)                   │
│          └── Predicate: BinaryExpression(...)                       │
│                                                                       │
│  Extensions: SqlRootExpression, SearchParamTableExpression,         │
│             SqlChainLinkExpression, ISqlExpressionVisitor           │
└─────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────┐
│           LAYER 3: Rewriter Pipeline (24 files)                     │
│    Microsoft.Health.Fhir.SqlServer/.../Expressions/Visitors/        │
│                                                                       │
│  Transformation Pipeline (ORDER MATTERS!):                          │
│                                                                       │
│  1. ChainFlatteningRewriter                                         │
│     └─→ Flattens ChainedExpression → SearchParamTableExpression     │
│                                                                       │
│  2. SortRewriter                                                    │
│     └─→ Adds SearchParamTableExpression(Kind: Sort)                 │
│                                                                       │
│  3. PartitionEliminationRewriter                                    │
│     └─→ Optimizes partition predicates                              │
│                                                                       │
│  4. TopRewriter                                                     │
│     └─→ Applies TOP clause optimizations                            │
│                                                                       │
│  5. ResourceColumnPredicatePushdownRewriter (PERF CRITICAL!)        │
│     └─→ Pushes predicates to Resource table (massive speedup)       │
│                                                                       │
│  Additional: DateTimeBoundedRangeRewriter, NumericRangeRewriter,    │
│             StringOverflowRewriter, NotExpressionRewriter,          │
│             IncludeRewriter, LastUpdatedToResourceSurrogateIdRewriter│
│                                                                       │
│  Base: SqlExpressionRewriter (immutable transformations)            │
└─────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────┐
│          LAYER 4: Query Generation (29 files)                       │
│    Microsoft.Health.Fhir.SqlServer/.../QueryGenerators/             │
│                                                                       │
│  SqlQueryGenerator.VisitSqlRoot() (1,565 lines - Master)            │
│    └─→ Orchestrates CTE generation, JOINs, sorting, pagination      │
│                                                                       │
│  Type-Specific Generators (Factory Pattern):                        │
│  ┌──────────────────────────────────────────────────────────┐      │
│  │ SearchParamTableExpressionQueryGeneratorFactory          │      │
│  │   ├─→ StringQueryGenerator (for name parameter)          │      │
│  │   ├─→ DateTimeQueryGenerator (for birthdate parameter)   │      │
│  │   ├─→ TokenQueryGenerator                                │      │
│  │   ├─→ ReferenceQueryGenerator                            │      │
│  │   └─→ (10+ more type-specific generators)                │      │
│  └──────────────────────────────────────────────────────────┘      │
│                                                                       │
│  Generated T-SQL:                                                   │
│  ┌──────────────────────────────────────────────────────────┐      │
│  │ WITH cte0 AS (                                            │      │
│  │   SELECT ResourceSurrogateId                              │      │
│  │   FROM dbo.StringSearchParam                              │      │
│  │   WHERE SearchParamId = @p0 AND Text LIKE '%Smith%'      │      │
│  │ ),                                                        │      │
│  │ cte1 AS (                                                 │      │
│  │   SELECT ResourceSurrogateId                              │      │
│  │   FROM dbo.DateTimeSearchParam                            │      │
│  │   WHERE SearchParamId = @p1 AND StartDateTime > @p2      │      │
│  │ )                                                         │      │
│  │ SELECT r.*                                                │      │
│  │ FROM dbo.Resource r                                       │      │
│  │ WHERE EXISTS (SELECT 1 FROM cte0                          │      │
│  │               WHERE ResourceSurrogateId = r.ResourceSurrogateId) │
│  │   AND EXISTS (SELECT 1 FROM cte1                          │      │
│  │               WHERE ResourceSurrogateId = r.ResourceSurrogateId) │
│  │ OPTION (OPTIMIZE FOR UNKNOWN)                             │      │
│  └──────────────────────────────────────────────────────────┘      │
│                                                                       │
│  Strategies: EXISTS vs INNER JOIN, CTE hierarchy, parameter hashing │
└─────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────┐
│         LAYER 5: Schema Management (96 versions)                    │
│        Microsoft.Health.Fhir.SqlServer/Features/Schema/             │
│                                                                       │
│  Schema Versions: V1 → V96 (Current: V87 Min, V96 Max)             │
│                                                                       │
│  SchemaVersionConstants:                                            │
│  ├─→ PartitionedTables = V9                                         │
│  ├─→ TokenOverflow = V41                                            │
│  ├─→ Defrag = V43                                                   │
│  ├─→ Merge = V50                                                    │
│  ├─→ MergeThrottling = V87                                          │
│  ├─→ SearchParameterOptimisticConcurrency = V95                     │
│  └─→ SearchParameterMaxLastUpdatedStoredProcedure = V96             │
│                                                                       │
│  Database Objects:                                                  │
│  ├─→ 36 Tables (Resource, TokenSearchParam, StringSearchParam, ...) │
│  ├─→ 66 Stored Procedures (MergeResources*, GetResourceVersions, ...)│
│  ├─→ 22 User-Defined Types (TokenSearchParamList, ...)             │
│  └─→ 132 Migration Files (incremental .diff.sql changes)           │
│                                                                       │
│  Generated Models: VLatest, V60-V96 (multi-targeted: net6.0/8.0/9.0)│
└─────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────┐
│                    SQL Server Execution                             │
│            SqlServerFhirDataStore → Database                        │
│              Returns SearchResult with Resources                    │
└─────────────────────────────────────────────────────────────────────┘
```

## 🔄 Rewriter Pipeline Visualization

```
Core Expression Tree
        ↓
┌──────────────────────────────────────────────┐
│  ChainFlatteningRewriter                     │
│  • Flattens ChainedExpression                │
│  • Creates SqlChainLinkExpression            │
│  • Critical for reference chaining           │
└──────────────────────────────────────────────┘
        ↓
┌──────────────────────────────────────────────┐
│  SortRewriter                                │
│  • Adds SearchParamTableExpression(Sort)     │
│  • Handles _sort parameter                   │
│  • Sets up ORDER BY generation               │
└──────────────────────────────────────────────┘
        ↓
┌──────────────────────────────────────────────┐
│  PartitionEliminationRewriter                │
│  • Optimizes partition predicates            │
│  • Requires V9+ (PartitionedTables)          │
│  • Massive perf gain on partitioned systems  │
└──────────────────────────────────────────────┘
        ↓
┌──────────────────────────────────────────────┐
│  TopRewriter                                 │
│  • Applies TOP clause optimization           │
│  • Limits result set early                   │
│  • Improves query performance                │
└──────────────────────────────────────────────┘
        ↓
┌──────────────────────────────────────────────┐
│  ResourceColumnPredicatePushdownRewriter     │
│  ⭐ CRITICAL PERFORMANCE OPTIMIZATION         │
│  • Pushes predicates to Resource table       │
│  • Avoids search param table joins           │
│  • 10-100x speedup for applicable queries    │
└──────────────────────────────────────────────┘
        ↓
┌──────────────────────────────────────────────┐
│  (Additional Rewriters as needed)            │
│  • DateTimeBoundedRangeRewriter              │
│  • NumericRangeRewriter                      │
│  • StringOverflowRewriter                    │
│  • NotExpressionRewriter                     │
│  • IncludeRewriter                           │
│  • LastUpdatedToResourceSurrogateIdRewriter  │
└──────────────────────────────────────────────┘
        ↓
SQL-Optimized Expression Tree
```

## 🏭 Query Generator Factory Pattern

```
SqlRootExpression
        ↓
SearchParamTableExpressionQueryGeneratorFactory.GetGenerator(expression)
        ↓
    ┌───────────────────────────────────────┐
    │  Determine Expression/Parameter Type  │
    └───────────────────────────────────────┘
                    ↓
        ┌───────────┴───────────┐
        │                       │
    Token Type            String Type
        ↓                       ↓
TokenQueryGenerator      StringQueryGenerator
        ↓                       ↓
Generate CTE for         Generate CTE for
TokenSearchParam         StringSearchParam
table                    table
        │                       │
        └───────────┬───────────┘
                    ↓
            Combine in SqlQueryGenerator
                    ↓
            Final T-SQL Query
```

## 📊 Expression Type Hierarchy

```
Expression (Abstract Base)
├── SearchParameterExpression (wraps expressions bound to search param)
├── BinaryExpression (=, >, <, >=, <=)
├── StringExpression (StartsWith, Contains, EndsWith, Equals)
├── MultiaryExpression (AND, OR)
│   ├── MultiaryOperator.And
│   └── MultiaryOperator.Or
├── ChainedExpression (reference chaining: Patient?organization.name=Acme)
├── IncludeExpression (_include, _revinclude)
│   ├── reversed=false (include)
│   └── reversed=true (revinclude)
├── CompartmentSearchExpression (compartment-based search)
├── SmartCompartmentSearchExpression (SMART compartments)
├── NotExpression (logical NOT)
├── InExpression<T> (IN clause)
├── SortExpression (sorting)
├── MissingSearchParameterExpression (:missing modifier)
├── MissingFieldExpression (missing field check)
├── UnionExpression (union operations)
└── NotReferencedExpression (not referenced check)

SQL-Specific Extensions:
├── SqlRootExpression (root of SQL tree)
├── SearchParamTableExpression (search param table query)
│   └── Kind: Normal, Chain, Include, IncludeLimit, Sort, SortWithFilter, Union
└── SqlChainLinkExpression (chain link)
```

## 🗄️ Schema Architecture

```
Database Schema (Current: V87-V96)
├── Core Tables
│   ├── Resource (main resource storage)
│   ├── ResourceType (resource type lookup)
│   └── System (system metadata)
│
├── Search Parameter Tables
│   ├── TokenSearchParam
│   ├── StringSearchParam
│   ├── DateTimeSearchParam
│   ├── NumberSearchParam
│   ├── QuantitySearchParam
│   ├── ReferenceSearchParam
│   └── UriSearchParam
│
├── Supporting Tables
│   ├── CompartmentAssignment
│   ├── ReindexJob
│   ├── TaskInfo
│   ├── Transactions
│   └── EventLog
│
├── Stored Procedures (66)
│   ├── Resource CRUD: MergeResources*, GetResourceVersions, HardDeleteResource
│   ├── Jobs: AcquireReindexJobs, CreateReindexJob, DequeueJob
│   ├── Performance: Defrag, ExecuteCommandForRebuildIndexes
│   └── Change Capture: CaptureResourceChanges, FetchResourceChanges
│
└── User-Defined Types (22)
    ├── Lists: BigintList, StringList, TokenTextList
    ├── Search Param Lists: TokenSearchParamList, StringSearchParamList, ...
    └── Composite: TokenTokenCompositeSearchParamList, ...
```

## 🎯 Critical Decision Points

### When Expression Becomes SQL

```
Expression Type         →  Generator              → SQL Pattern
─────────────────────────────────────────────────────────────────
Token                   →  TokenQueryGenerator    → CTE from TokenSearchParam
String                  →  StringQueryGenerator   → CTE from StringSearchParam + LIKE
DateTime                →  DateTimeQueryGenerator → CTE from DateTimeSearchParam + range
Reference               →  ReferenceQueryGenerator→ CTE from ReferenceSearchParam
ChainedExpression       →  ChainLinkQueryGenerator→ Multi-CTE with JOIN on reference
IncludeExpression       →  IncludeQueryGenerator  → Separate CTE for includes
Composite (Token+Token) →  TokenTokenCompositeQG  → CTE with composite predicate

Final Combination: SqlQueryGenerator combines all CTEs with EXISTS or INNER JOIN
```

### JOIN Strategy Decision

```
SqlQueryGenerator decides:

IF (Simple query, few parameters):
    Use EXISTS subqueries
    └─→ Better for simple queries, avoids join overhead

ELSE IF (Complex query, many parameters):
    Use INNER JOIN with CTEs
    └─→ Better for complex queries, SQL Server optimizes joins

Applies: OPTION (OPTIMIZE FOR UNKNOWN)
    └─→ Prevents parameter sniffing issues
```

## 📈 Performance Optimization Flow

```
Original Query: Patient?_lastUpdated=gt2024&gender=male&birthdate=lt2000
                                ↓
┌──────────────────────────────────────────────────────────┐
│  Expression Tree Analysis                                │
│  MultiaryExpression(AND, [                               │
│    SearchParameterExpression(_lastUpdated, ...),         │
│    SearchParameterExpression(gender, ...),               │
│    SearchParameterExpression(birthdate, ...)             │
│  ])                                                      │
└──────────────────────────────────────────────────────────┘
                                ↓
┌──────────────────────────────────────────────────────────┐
│  LastUpdatedToResourceSurrogateIdRewriter                │
│  • Converts _lastUpdated to ResourceSurrogateId range    │
│  • Enables index seek instead of scan                    │
└──────────────────────────────────────────────────────────┘
                                ↓
┌──────────────────────────────────────────────────────────┐
│  ResourceColumnPredicatePushdownRewriter                 │
│  • Identifies: gender, birthdate can push to Resource    │
│  • Moves predicates from SearchParam tables to Resource  │
│  • Result: Avoids 2 search param table joins!            │
└──────────────────────────────────────────────────────────┘
                                ↓
┌──────────────────────────────────────────────────────────┐
│  Optimized SQL Generation                                │
│  SELECT r.*                                              │
│  FROM dbo.Resource r                                     │
│  WHERE r.ResourceSurrogateId >= @minSurrogateId          │
│    AND r.Gender = 'male'                                 │
│    AND r.BirthDate < '2000-01-01'                        │
│                                                          │
│  (No search param table joins needed!)                   │
└──────────────────────────────────────────────────────────┘
```

## 🔍 Debugging Flow

```
Issue: Search returns incorrect results
                ↓
┌──────────────────────────────────────────────────────────┐
│  1. Examine Expression Tree                              │
│     Add breakpoint: ExpressionParser.Parse()             │
│     Verify: Correct expression type and structure        │
└──────────────────────────────────────────────────────────┘
                ↓
┌──────────────────────────────────────────────────────────┐
│  2. Trace Rewriter Pipeline                              │
│     Add logging: Each rewriter's AcceptVisitor()         │
│     Verify: Transformations are correct                  │
│     Check: Rewriter execution order                      │
└──────────────────────────────────────────────────────────┘
                ↓
┌──────────────────────────────────────────────────────────┐
│  3. Inspect SQL Generation                               │
│     Breakpoint: SqlQueryGenerator.VisitSqlRoot()         │
│     Check: Generator selection via factory               │
│     Verify: CTE generation logic                         │
└──────────────────────────────────────────────────────────┘
                ↓
┌──────────────────────────────────────────────────────────┐
│  4. Examine Generated SQL                                │
│     Enable logging: Capture actual T-SQL                 │
│     Analyze: Query plan in SSMS                          │
│     Verify: Parameter values and predicates              │
└──────────────────────────────────────────────────────────┘
                ↓
┌──────────────────────────────────────────────────────────┐
│  5. Schema Compatibility Check                           │
│     Verify: SchemaVersionConstants checks                │
│     Confirm: Current schema version supports feature     │
└──────────────────────────────────────────────────────────┘
```

## 📚 Quick Reference

### File Locations

| Component | Location |
|-----------|----------|
| Core Expressions | `src/Microsoft.Health.Fhir.Core/Features/Search/Expressions/` |
| SQL Extensions | `src/Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/` |
| Rewriters | `src/Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/Visitors/` |
| Query Generators | `src/Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/Visitors/QueryGenerators/` |
| Schema | `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/` |
| Orchestration | `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlServerSearchService.cs` |

### Key Classes

| Class | Purpose | Lines |
|-------|---------|-------|
| `Expression.cs` | Abstract base for all expressions | 339 |
| `SqlQueryGenerator.cs` | Master query generator | 1,565 |
| `SqlServerSearchService.cs` | Search orchestration | ~1,000 |
| `SchemaVersionConstants.cs` | Feature version flags | ~200 |

### Design Patterns

- **Visitor Pattern**: Expression tree traversal (IExpressionVisitor)
- **Factory Pattern**: Query generator selection
- **Builder Pattern**: SQL query construction
- **Strategy Pattern**: JOIN strategy selection
- **Pipeline Pattern**: Rewriter chain execution
- **Immutability Pattern**: Expression transformations

---

**Use this visualization alongside the SQL Provider Agent for maximum understanding!**

Invoke: `@sql-provider-agent <your question>`

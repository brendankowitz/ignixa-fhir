# FHIR $graphql Spec Compliance Roadmap

**Date:** 2026-03-30
**Status:** Proposed
**Scope:** Full gap analysis and prioritized roadmap for FHIR GraphQL specification compliance
**Reference:** [FHIR GraphQL Specification](https://build.fhir.org/graphql.html)

---

## Problem Statement

The Ignixa FHIR server has a substantially implemented `$graphql` operation using HotChocolate v15 with dynamic schema generation, CQRS-first resolvers, multi-version FHIR support, and multi-tenancy. However, cross-referencing against the FHIR GraphQL specification (`build.fhir.org/graphql.html`) reveals several compliance gaps ranging from critical bugs (reference resolution returning null) to missing spec-defined features (reverse references, flattening directives, mutations).

This roadmap structures the remaining work as a spec-first, bottom-up sequence: fix the schema foundation first, then layer on field-level features, then advanced query capabilities, then polish.

## Approach

**Spec-First, Bottom-Up** — Fix structural schema issues that everything depends on before adding features. Each phase is independently shippable and testable. Breaking changes to the current experimental GraphQL schema are acceptable.

Both `PatientList` (flat array) and `PatientConnection` (paginated with edges) search patterns will be implemented per the FHIR spec.

---

## Current State Summary

### What Works

- 8 endpoints (system + instance level, GET + POST, tenant-aware + agnostic)
- Dynamic schema generation from FHIR `ISchema` metadata via `ITypeModule`
- CQRS-first resolvers (`ResourceResolver`, `SearchResolver`, `FieldResolver`)
- `ResourceDataLoader` for batched reference resolution (exists but not wired)
- Multi-version FHIR support (STU3, R4, R4B, R5, R6) via named schemas
- Multi-tenancy context propagation via `FhirHttpRequestInterceptor`
- Query depth limiting, execution timeout, introspection toggle
- Schema warmup on startup, cache invalidation on package load
- 25 unit/E2E tests, 93 GraphQL test data files per FHIR version

### Critical Gaps

| Gap | Severity | Detail |
|-----|----------|--------|
| Reference `resource` field returns null | **Critical** | DataLoader exists but isn't wired into `ResourceReference` resolver |
| Connection shape wrong | **High** | Uses `entry[]`/`link` instead of spec `edges[{mode,score,resource}]`/pagination fields |
| No simple List search | **High** | Spec defines flat array return; only connection-style wrapper exists |
| No primitive extension fields | **Medium** | No `_birthDate` companion field for primitive extension access |
| No list navigation arguments | **Medium** | No `fhirpath`/field filter/`_offset`/`_count` on repeating complex fields |
| No reverse references | **Medium** | No `_reference` parameter for instance-level reverse lookups |
| No flattening directives | **Medium** | No `@flatten`/`@first`/`@singleton`/`@slice` (test data exists) |
| No mutations | **Low** | Create/Update/Delete not implemented |

---

## Phase 1: Schema Foundation

Fix the broken/incorrect structural elements that all other features depend on.

### 1.1 Wire ResourceDataLoader into Reference Resolver

**Problem:** `ResourceReference.resource` field resolves to `null` (hardcoded). The `ResourceDataLoader` exists with full batching logic but is never called from the schema.

**Solution:**
- In `FhirTypeModule.BuildResourceReferenceType()`, replace the hardcoded null resolver with one that:
  1. Extracts the `reference` string from the parent `JsonElement`
  2. Parses it into a `ResourceKey` (type + id)
  3. Calls `ResourceDataLoader.LoadAsync(key)` to batch the fetch
  4. Returns the resolved `JsonElement` as the resource value
- The resolver must handle relative references (`Patient/123`), absolute references, and contained references
- Return null gracefully for references that can't be parsed or resolved

**Files:** `Schema/FhirTypeModule.cs`, `Resolvers/FieldResolver.cs`

### 1.2 Restructure Connection Type to Match FHIR Spec

**Problem:** Current Connection shape: `{ entry: [Resource], total: Int, link: PaginationLinks }`. FHIR spec shape: `{ count: Int, offset: Int, pagesize: Int, edges: [{ mode: String, score: Float, resource: Resource }], first: String, previous: String, next: String, last: String }`.

**Solution:**
- Rename/rebuild Connection type to match spec:
  - `count` (total match count)
  - `offset` (current offset)
  - `pagesize` (page size used)
  - `edges` → list of `{ mode: String, score: Float, resource: <ResourceType> }`
  - `first`, `previous`, `next`, `last` → cursor strings for navigation
- Update `SearchConnectionResult` model to carry edge metadata (mode, score per entry)
- Update `SearchResolver` to populate the new shape from `SearchResourcesQuery` results
- The current `PatientList` field (which returns a connection-like wrapper) becomes `PatientConnection`

**Files:** `Schema/FhirTypeModule.cs`, `Models/SearchConnectionResult.cs`, `Resolvers/SearchResolver.cs`

### 1.3 Add Simple List Search (Flat Array)

**Problem:** Spec defines `PatientList(name: "Smith")` returning `[Patient]` directly — no pagination wrapper. Server rejects if too many results. This pattern is simpler for clients that don't need pagination.

**Solution:**
- Add NEW `PatientList` query fields that return `[Patient]` (a `ListType` of the resource type)
- Note: after 1.2, the old `PatientList` has been renamed to `PatientConnection`, so the name is available
- Accept same search parameters as Connection variant
- If result count exceeds `MaxPageSize`, return a GraphQL error (not silent truncation)
- No cursor/pagination support — that's what Connection is for

**Files:** `Schema/FhirTypeModule.cs`, `Resolvers/SearchResolver.cs`

### 1.4 Add `optional` and `type` Arguments to Reference Resolution

**Problem:** Spec defines two arguments on the `resource` field of Reference types:
- `optional: Boolean` (default false) — if true, unresolvable references return null instead of error
- `type: String` — only resolve if the referenced resource matches this type

**Solution:**
- Add `optional` argument (Boolean, default false) to `ResourceReference.resource`
- Add `type` argument (String) to `ResourceReference.resource`
- In the resolver: if `optional=false` and reference can't be resolved, add a GraphQL error; if `optional=true`, return null
- If `type` is specified and the referenced resource doesn't match, return null (not an error)

**Files:** `Schema/FhirTypeModule.cs`

### 1.5 Verify Single-Resource Read Field

**Problem:** Spec defines `Patient(_id: "example")` at system level returning a single resource (not a list). Need to verify this exists or add it.

**Solution:**
- Confirm that the query type has `Patient(_id: ID!)` returning a single `Patient` (not a connection)
- If missing, add it alongside List/Connection variants
- Should call `ResourceResolver.ResolveByIdAsync()`

**Files:** `Schema/FhirTypeModule.cs`

---

## Phase 2: Field-Level Spec Compliance

Make individual field access fully spec-compliant.

### 2.1 Primitive Extension Fields (`_fieldName` Convention)

**Problem:** For every primitive field like `birthDate`, the spec requires a companion `_birthDate` field that exposes the FHIR Element type (`id` + `extension[]`). Currently only the scalar value is exposed.

**Solution:**
- Create an `Element` ObjectType with fields: `id: String`, `extension: [Extension]`
- Create an `Extension` ObjectType with fields: `url: String`, plus all choice element value fields (`valueString`, `valueInteger`, `valueBoolean`, etc.)
- In `FhirTypeModule`, when generating primitive fields, also generate a `_fieldName` companion field
- The companion field's resolver extracts the `_fieldName` property from the parent `JsonElement` (following FHIR JSON convention)
- For arrays: if `telecom` is `[ContactPoint]`, then `_telecom` is `[Element]` with matching cardinality

**Files:** `Schema/FhirTypeModule.cs`, new `Schema/ElementTypes.cs`

### 2.2 List Navigation Arguments on Complex Fields

**Problem:** Repeating complex fields (e.g., `name`, `identifier`, `telecom`) should accept filtering/pagination arguments per the spec:
- `fhirpath: String` — FHIRPath expression to filter the list
- `[subfield]: String` — field-value filter (e.g., `name(use: "official")`)
- `_offset: Int` — skip N entries
- `_count: Int` — return at most N entries

**Solution:**
- When generating ObjectType fields for repeating complex elements, add these arguments
- For `fhirpath`: evaluate the expression against each list element using the existing FHIRPath engine, include only matches
- For field filters: match the sub-property value as a simple string equality
- For `_offset`/`_count`: apply after filtering, standard skip/take semantics
- Resolver wraps the underlying `JsonElement` array iteration with filter + pagination logic

**Files:** `Schema/FhirTypeModule.cs`, `Resolvers/FieldResolver.cs`

### 2.3 Search Parameter Array Syntax

**Problem:** Search parameter arguments accept scalar `String` only. Spec requires array support for OR semantics: `PatientList(_id: ["1", "2"])`.

**Solution:**
- Change search parameter argument types from `StringType` to `ListType(StringType)` (or `[String]`)
- GraphQL automatically coerces a scalar value to a single-element list
- In `SearchResolver`, join array values with `,` when building the search query (FHIR OR syntax)

**Files:** `Schema/FhirTypeModule.cs`, `Resolvers/SearchResolver.cs`

### 2.4 Expose FHIR Search Parameters as Typed Arguments

**Problem:** Currently only `_count`, `_cursor`, `_sort`, `_total` are exposed as search arguments. The spec says all search parameters for a resource type should be available as GraphQL arguments.

**Solution:**
- During schema generation, query `ISearchService` (or `ISchema`) for all search parameters defined for each resource type
- For each search parameter, add a GraphQL argument with the parameter name (replacing `-` with `_` per spec)
- All search parameters typed as `[String]` (array for OR support)
- In `SearchResolver.BuildSearchOptions()`, iterate all provided arguments and map them to `QueryParameter` entries
- Skip parameters that are handled specially: `_include`, `_revinclude`, `_contained`, `_containedType` (not supported per spec)

**Files:** `Schema/FhirTypeModule.cs`, `Resolvers/SearchResolver.cs`

---

## Phase 3: Advanced Query Features

Higher-level spec features that enable complex query patterns.

### 3.1 Reverse References (`_reference` Parameter)

**Problem:** At instance level, the spec allows reverse lookups: querying a Patient and also fetching all Conditions that reference that patient. Example: `{ name { family } ConditionList(_reference: patient) { id clinicalStatus } }`.

**Solution:**
- At instance level, add `[Type]List` and `[Type]Connection` fields to the root query with a mandatory `_reference: String!` argument
- The `_reference` argument names the search parameter on the target type that references the current resource
- Resolver builds a search query: `Condition?patient=Patient/{id}` where `{id}` comes from the instance context
- Reuse existing `SearchResolver` with the additional reference constraint
- Connection-based reverse lookups also supported but cursor pagination is prohibited at nested level per spec — clients must issue a separate top-level query to follow cursors

**Files:** `Schema/FhirTypeModule.cs`, `Resolvers/SearchResolver.cs`

### 3.2 Data Flattening Directives

**Problem:** The spec defines four custom directives for flattening GraphQL output for analytics/statistical use:
- `@flatten` — hoist children up to parent level, children become lists
- `@first` — select only the first element from a list
- `@singleton` — assert that a flattened field has exactly one value (return scalar not list)
- `@slice(path: "...")` — split a list into named singletons using a FHIRPath discriminator

**Solution:**
- Register four custom HotChocolate directives using `DirectiveType<T>`
- Implement a custom `IOperationResultMiddleware` (or result formatter) that post-processes the GraphQL result tree:
  - `@flatten`: walk the result, for fields marked flatten, merge their children into the parent object
  - `@first`: filter arrays to first element only
  - `@singleton`: unwrap single-element arrays to scalars, error if >1
  - `@slice`: evaluate the FHIRPath `path` expression on each list element, suffix the field name with the result
- Test data already exists in the repo for all four directives (`test/Ignixa.FhirPath.Tests/TestData/fhir-test-cases/*/graphql/`)

**Files:** New `Directives/` folder under GraphQl module, custom result middleware

### 3.3 `_filter` Parameter

**Problem:** Simple search parameter arguments can't express AND combinations, chained searches, or modifier-based queries. The `_filter` parameter provides a text-based query language for complex searches.

**Solution:**
- Add `_filter: String` argument to all List and Connection search fields
- Pass the filter value directly to the FHIR search engine (it already supports `_filter` if enabled)
- Document that `_filter` availability depends on the server's search capabilities

**Files:** `Schema/FhirTypeModule.cs`, `Resolvers/SearchResolver.cs`

---

## Phase 4: Error Handling & Operations

Polish and spec completeness.

### 4.1 OperationOutcome in GraphQL Error Extensions

**Problem:** Spec says errors SHOULD include a FHIR OperationOutcome in `extensions.resource`. Currently, raw HotChocolate errors are returned without FHIR wrapping.

**Solution:**
- Implement a custom HotChocolate `IErrorFilter` that wraps errors:
  ```json
  {
    "extensions": {
      "resource": {
        "resourceType": "OperationOutcome",
        "issue": [{ "severity": "error", "code": "exception", "diagnostics": "..." }]
      }
    },
    "message": "..."
  }
  ```
- Map HotChocolate error codes to FHIR issue types where possible
- Register the error filter in `ExperimentalServicesRegistration.cs`

**Files:** New `Pipeline/FhirGraphQlErrorFilter.cs`, `Infrastructure/ExperimentalServicesRegistration.cs`

### 4.2 `_graphql` Parameter on FHIR Operations

**Problem:** Spec allows `GET [base]/ValueSet/x/$validate-code?_graphql={...}` — applying a GraphQL query to any FHIR operation's output.

**Solution:**
- Add middleware/filter that detects `_graphql` query parameter on operation responses
- If present, parse the operation result as a `JsonElement` and execute the GraphQL query against it
- Return the GraphQL result instead of the FHIR operation result
- If the operation fails (returns OperationOutcome with errors), skip GraphQL execution and return the error
- This is a cross-cutting concern — applies to all FHIR operations, not just GraphQL endpoints

**Files:** New middleware in API layer, potentially `Endpoints/Experimental/GraphQlOperationMiddleware.cs`

### 4.3 Mutations (Create/Update/Delete)

**Problem:** Spec defines mutation operations:
- `PatientCreate(res: Patient) { ... }` — create, server assigns/overwrites ID
- `PatientUpdate(id: ID, res: Patient) { ... }` — update existing resource
- `PatientDelete(id: ID)` — delete, no return value

**Solution:**
- Generate GraphQL InputObjectTypes mirroring each resource's OutputType
- Register mutation type in HotChocolate schema with `[Type]Create`, `[Type]Update`, `[Type]Delete` fields
- Resolvers call existing CQRS commands: `CreateResourceCommand`, `UpdateResourceCommand`, `DeleteResourceCommand`
- Input validation through existing FHIR validation pipeline
- Return the created/updated resource (or null for delete)
- This is the largest work item — input type generation mirrors the full type system

**Files:** `Schema/FhirTypeModule.cs` (input types), new `Resolvers/MutationResolver.cs`

### 4.4 Capability Statement Advertisement

**Problem:** Spec says servers SHALL indicate GraphQL support in their CapabilityStatement.

**Solution:**
- When GraphQL is enabled, add appropriate entries to the CapabilityStatement response
- Include the `$graphql` operation definition reference
- Indicate which resource types support GraphQL queries

**Files:** CapabilityStatement generation code (outside GraphQL module)

---

## Dependency Graph

```
Phase 1.1 (wire DataLoader) ─┐
Phase 1.2 (Connection shape) ─┤── Phase 2.4 (search params) ── Phase 3.1 (reverse refs)
Phase 1.3 (simple List)      ─┤                                Phase 3.3 (_filter)
Phase 1.4 (ref args)         ─┘
Phase 1.5 (single read)

Phase 2.1 (primitive ext)     ── independent
Phase 2.2 (list nav args)    ── independent (uses FHIRPath engine)
Phase 2.3 (array syntax)     ── feeds into 2.4

Phase 3.2 (flattening)       ── independent (post-processing)

Phase 4.1 (error filter)     ── independent
Phase 4.2 (_graphql param)   ── depends on 1.1 (needs working execution)
Phase 4.3 (mutations)        ── depends on all of Phase 1 + 2
Phase 4.4 (capability stmt)  ── independent
```

---

## Out of Scope

- **GraphQL Subscriptions** — Spec doesn't define these for FHIR. Future bridge to FHIR Subscriptions is a separate feature.
- **Federation/Schema Stitching** — Single-server implementation only.
- **Profile-aware extension typing** — Extensions remain generic `[Extension]` lists; profile-specific typing is a separate feature.
- **`_include`/`_revinclude`** — Superseded by GraphQL's inline reference resolution per spec.

## Risks

- **Schema size explosion** — Adding input types for mutations roughly doubles the schema size (~6000+ types for R5). May impact startup time and client tooling.
- **Flattening directive complexity** — Post-processing the result tree is non-trivial, especially `@slice` with FHIRPath evaluation. May require careful performance testing.
- **Breaking changes** — Connection shape restructuring will break existing clients. Mitigated by experimental status.
- **HotChocolate v15 limitations** — Query complexity analysis configuration option exists but HC v15 doesn't expose a built-in complexity rule. May need custom implementation or version upgrade.

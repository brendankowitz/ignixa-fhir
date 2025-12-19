# Investigation: SQL-Optimized Terminology Expansion

**Feature**: terminology-services
**Status**: Proposed
**Created**: 2025-12-18
**Original ADR**: N/A

## Goal
Move compose-based ValueSet expansion and terminology filtering out of application-side scanning into SQL-native queries and indexes, to reduce CPU/memory pressure and improve latency on large CodeSystems (LOINC, SNOMED).

## Current Gaps
- Compose include/exclude with filters (code/display/regex/is-a/property) is executed in-process after loading candidate concepts.
- Full-system includes are guarded by a flag but still require loading all codes when enabled.
- No persisted ancestry or property key/value indexes to support fast filtering.

## Proposed SQL Approach
1) **Persisted property KV for filtering**
   - Schema: add `TermConceptProperty` table (TermConceptId FK, PropertyCode nvarchar(100), PropertyValue nvarchar(400), maybe ValueCanonical, ValueCodingCode/System).
   - Migration: populate from CodeSystem properties during import; keep PropertiesJson for completeness.
   - Indexes: `(PropertyCode, PropertyValue, TermCodeSystemId, Code)`; potentially filtered on active.
   - Compose mapping: property filters in include/exclude map to WHERE clauses on `TermConceptProperty`.

2) **Ancestry table for is-a/descendant-of**
   - Schema: `TermConceptAncestor` (DescendantId, AncestorId, Depth). Populate during import (hierarchy flatten).
   - Index: `(DescendantId, AncestorId)` and `(AncestorId)` to support “is-a” lookups quickly.
   - Compose mapping: “is-a”/“descendent-of” filters become EXISTS against `TermConceptAncestor` joined to the ancestor code in the same CodeSystem.

3) **Computed/searchable display/code columns**
   - Add case-insensitive computed columns or persisted lower-case columns on `TermConcept` (CodeLower, DisplayLower) with indexes to support `=` and `contains` via `LIKE`.
   - Indexes: `(TermCodeSystemId, CodeLower)`, `(TermCodeSystemId, DisplayLower)` with includes on Display/Code.

4) **Query rewrite for compose expansion**
   - Build a single SQL query per include block:
     - Base FROM `TermConcept` tc
     - JOIN `TermCodeSystem` tcs on Id
     - JOIN `TermConceptAncestor` (for is-a filters) when needed
     - JOIN `TermConceptProperty` for property filters when needed
     - WHERE clauses for system/version, code/display (with lower-case columns), property filters, ancestor filters
   - Exclude blocks become NOT EXISTS subqueries or anti-joins using the same structures.
   - Insert results directly into `TermValueSetExpansion` via `INSERT … SELECT` with `ROW_NUMBER()` for ordinal assignment.

5) **Pre-expanded ValueSets**
   - Reuse existing `TermValueSetExpansion` when a referenced ValueSet is already expanded.
   - Fall back to SQL compute when no expansion is present.

## Schema Changes (outline)
- `TermConceptProperty`:
  - TermConceptId (bigint, FK -> TermConcept, CASCADE)
  - PropertyCode (nvarchar(100))
  - PropertyValue (nvarchar(400))
  - PropertySystem (nullable)
  - PropertyCodeValue (nullable) // for codings
  - Indexes: IX_TermConceptProperty_Code_Value (PropertyCode, PropertyValue, TermConceptId)
- `TermConceptAncestor`:
  - DescendantId (bigint, FK -> TermConcept, CASCADE)
  - AncestorId (bigint, FK -> TermConcept, CASCADE)
  - Depth (int)
  - Indexes: IX_TermConceptAncestor_Ancestor (AncestorId), IX_TermConceptAncestor_Descendant (DescendantId, AncestorId)
- `TermConcept` computed/persisted:
  - CodeLower, DisplayLower (persisted lowercase) with indexes `(TermCodeSystemId, CodeLower)` and `(TermCodeSystemId, DisplayLower)`

## Import Pipeline Updates
- During CodeSystem import:
  - Populate `TermConceptAncestor` with a BFS/DFS walk.
  - Extract properties into `TermConceptProperty` rows.
  - Set lower-case columns (or let computed columns fill).
- During ValueSet import (compose path):
  - For each include/exclude, emit SQL query using the structures above (no client-side enumerable filtering).
  - Use temp table or table-valued parameter for include/exclude results, then `INSERT` into `TermValueSetExpansion` with `ROW_NUMBER()` for ordinal.

## Capability/Perf Impact
- Filters execute in SQL with indexed lookups; reduces app RAM/CPU.
- “is-a” uses ancestor table instead of recursive client traversal.
- Property filters hit dedicated table instead of JSON parse.
- Full-system includes still heavy but stay server-side; consider paging/chunking if needed.

## Phase Plan
1) Add schema/migrations for property and ancestor tables + lowercase columns.
2) Update importer to populate new tables.
3) Rewrite compose expansion to SQL-based `INSERT … SELECT` for include/exclude with filters.
4) Add integration tests: filter (code/display/regex/property/is-a), exclude behavior, SQL vs. in-memory parity.
5) Benchmark large CodeSystems (LOINC/SNOMED) for compose performance before/after.

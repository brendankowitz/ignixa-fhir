# Feature: Semantic Search

**Status**: Proposed
**Created**: 2026-10-05

## Problem Statement

Clinical free text such as note text, `valueString` and narrative is poorly served by FHIR token and string search. Users want to find resources by meaning (`difficulty breathing` should find `dyspnea`) while keeping ordinary structured filters, tenancy and authorization intact. microsoft/fhir-server has an open proposal ([#5802](https://github.com/microsoft/fhir-server/pull/5802), [#5803](https://github.com/microsoft/fhir-server/pull/5803)) that is not moving. Ignixa can ship the capability natively and stay compatible with it.

## Constraints

- **Wire compatibility with fhir-server:** the same SearchParameter extension (`http://microsoft.com/fhir/StructureDefinition/vector-search-config`) and the same query semantics, so definitions and clients move between servers unchanged.
- **`MergeResources` and its TVPs are not modified**, so vectors cannot be committed atomically with the resource.
- **Native SQL `vector(1536)`** requires Azure SQL Database or SQL Server 2025+. Ignixa's docker-compose and CI already run SQL Server 2025.
- **Provider neutral:** `Microsoft.Extensions.AI` `IEmbeddingGenerator`, with no Azure-only abstraction in Application code.
- **Disabled by default.** When off, the feature makes no provider calls and semantic parameters behave as unknown.
- **No general reindex executor exists yet** (see [reindex](../reindex/)), so backfilling existing resources depends on it or on a dedicated job.

## Investigations
| Investigation | Status | Summary |
|--------------|--------|---------|
| [wire-compatible-port](investigations/wire-compatible-port.md) | Implemented (Slice 1) | Port fhir-server's contract with Ignixa-native internals: synchronous embed before merge, vectors in a side table written post-merge, compiler-native gating CTE plus distance ranking. |

## Decision

Proposed: [ADR-2610: Semantic Vector Search Parameters](adr-2610-semantic-vector-search.md). It moves to `docs/adr/` via `/accept-adr` after implementation.

## Status

Slice 1 (contract + synchronous SQL Server search) is implemented: the `vector-search-config`
SearchParameter extension, write-path chunking/embedding before `MergeResources`, schema version 4
(`dbo.VectorSearchParam` / `dbo.EmbeddingModel`), the SQL compiler's gating CTE and distance ranking, and
`Bundle.entry.search.score`. See `docs/site/docs/server/features/semantic-search.md` for user-facing
configuration and behavior, and the [Not yet supported](../../site/docs/server/features/semantic-search.md#not-yet-supported)
section for what remains: asynchronous indexing, backfill of pre-existing resources, and bulk `$import`.
The ADR stays Proposed until `/accept-adr` runs.


---
sidebar_position: 6
title: Semantic Search
description: Vector (semantic) search over free-text SearchParameters
---

# Semantic Search

:::caution Experimental Feature
Semantic search is disabled by default and ships as Slice 1 of
[ADR-2610](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/features/semantic-search/adr-2610-semantic-vector-search.md):
synchronous embedding on the write path, SQL Server-only storage, and cosine-ranked query. Asynchronous
indexing and a backfill mechanism for pre-existing resources are not implemented yet.
:::

Ignixa can rank search results by meaning instead of exact token/string matches. A `special`-typed
SearchParameter carrying the `vector-search-config` extension has its indexed text embedded and stored as
a vector; searching on that parameter embeds the query once and ranks matches by cosine distance,
reported as `Bundle.entry.search.score`.

## Requirements

- **Azure SQL Database, or SQL Server 2025 or later.** The feature depends on the native SQL `vector`
  type. Schema version 5 (which adds `dbo.VectorSearchParam` and `dbo.EmbeddingModel`) cannot deploy to an
  older engine: `SchemaDeployer` probes `sys.types` for `vector` before applying any DDL and fails with
  _"Ignixa schema version 5 requires a SQL engine with the native vector type (Azure SQL Database or SQL
  Server 2025+)."_ This probe runs even when `VectorSearch:Enabled` is `false` -- schema version 5 is a
  hard requirement for every tenant on the SQL Server data layer, feature flag aside.
- **The SQL Server data layer, for every active tenant.** `VectorSearch:Enabled = true` fails startup if
  any active tenant is not configured for `SqlServer` or `SqlEntityFramework` storage. The FileSystem data
  layer has no vector storage and cannot support the feature.
- **A 1536-dimension embedding model.** The only dimensionality this slice supports; both the
  `vector(1536)` column and the provider request are hard-coded to it. `Embedding.ModelName` must also be
  a model `Microsoft.ML.Tokenizers`' `TiktokenTokenizer` recognizes (e.g. `text-embedding-3-small`), since
  the same model drives write-path chunking.

## Configuration

Bound from the `VectorSearch` section (`Ignixa.Application.Features.SemanticSearch.VectorSearchOptions`):

```json
{
  "VectorSearch": {
    "Enabled": false,
    "Embedding": {
      "Endpoint": null,
      "DeploymentName": null,
      "ModelName": "text-embedding-3-small",
      "ModelVersion": null,
      "Dimensions": 1536
    },
    "Indexing": {
      "Mode": "Synchronous",
      "ChunkSizeTokens": 800,
      "ChunkOverlapTokens": 100
    },
    "Query": {
      "DistanceMetric": "cosine",
      "EmbeddingCacheMinutes": 10,
      "EmbeddingCacheMaxEntries": 1000
    }
  }
}
```

| Setting | Default | Notes |
|---|---|---|
| `Enabled` | `false` | Master switch. When `false`, no embedding generator is registered, no provider call is ever possible, and semantic parameters behave as unknown (see [Feature disabled](#feature-disabled)). |
| `Embedding.Endpoint` / `DeploymentName` / `ModelName` | — / — / `text-embedding-3-small` | Azure OpenAI deployment. `Endpoint`, `DeploymentName` and `ModelName` are required when `Enabled` is `true`. |
| `Embedding.Dimensions` | `1536` | Must equal `1536`; any other value fails validation. |
| `Indexing.Mode` | `Synchronous` | The only mode this slice supports. `Asynchronous` fails startup with _"Indexing.Mode 'Asynchronous' is not yet supported."_ |
| `Indexing.ChunkSizeTokens` / `ChunkOverlapTokens` | `800` / `100` | Default chunk window for a SearchParameter whose extension does not override `chunkSizeTokens`/`chunkOverlapTokens`. |
| `Query.DistanceMetric` | `cosine` | The only supported metric. |
| `Query.EmbeddingCacheMinutes` / `EmbeddingCacheMaxEntries` | `10` / `1000` | Bounds the dedicated query-embedding cache (see [query behavior](#query-behavior)). `0` on either disables caching. |

Every setting also accepts an environment variable override (`VectorSearch__Enabled`,
`VectorSearch__Embedding__Endpoint`, etc.), following standard ASP.NET Core configuration binding.

Authentication to Azure OpenAI uses an already-registered `Azure.Core.TokenCredential` if one exists
(e.g. a Managed Identity credential configured for another Azure client), falling back to
`DefaultAzureCredential`.

## The SearchParameter extension

A semantic SearchParameter is an ordinary `special`-typed SearchParameter whose FHIRPath expression
extracts a string (or strings), carrying one additional extension:

```
http://microsoft.com/fhir/StructureDefinition/vector-search-config
```

| Sub-extension | Type | Default | Validation |
|---|---|---|---|
| `extractionPolicy` | `code`: `firstValue` \| `concatenate` \| `perValueRow` | `concatenate` | Must be one of the three codes. |
| `maxInputTokens` | `positiveInt` | `8000` | Must be greater than 0. |
| `minimumScore` | `decimal` (0..1) | `0` | Must be between 0 and 1. |
| `chunkSizeTokens` | `positiveInt` | falls back to `VectorSearch:Indexing:ChunkSizeTokens` | When present, must be at least 16 tokens. |
| `chunkOverlapTokens` | `unsignedInt` | falls back to `VectorSearch:Indexing:ChunkOverlapTokens` | When present, must be 0 or greater and strictly less than the effective chunk size. |
| `distanceMetric` | `code` | — | If present, must be `cosine` (validated, not otherwise retained -- cosine is the only ranking this slice implements). |

`extractionPolicy` governs what gets embedded when the expression matches more than one value on a
resource: `firstValue` keeps only the first match, `concatenate` joins every match with `\n` into one
passage, and `perValueRow` embeds each match as its own chunk group. A malformed sub-extension (wrong
type, out-of-range value, unrecognized code) keeps the SearchParameter registered but **unsupported** --
the same state as the feature being disabled for that one parameter -- rather than failing activation.

Extracted text is **unescaped** before chunking and embedding, the same as every other `string`-typed
search value: an escaped FHIR search special character (`\,`, `\|`, `\$`) in the FHIRPath-extracted value
is embedded with the backslash removed, matching fhir-server's behavior.

This extension and its semantics are shared with
[microsoft/fhir-server#5802](https://github.com/microsoft/fhir-server/pull/5802) /
[#5803](https://github.com/microsoft/fhir-server/pull/5803) (see [Wire compatibility](#wire-compatibility)).

## Query behavior

- **One embedding per query.** A search's semantic query text is embedded once and then reused -- even
  across paged requests -- from a dedicated, process-local cache keyed on `(embedding model, verbatim
  query text)`, bounded by `Query.EmbeddingCacheMinutes` / `EmbeddingCacheMaxEntries`. Within that window,
  on the instance that served the first page, every later page of the same query reuses the cached
  vector. Without this, a provider that returns a slightly different vector on every call could rank
  page 2 by a different query vector than page 1, silently skipping or repeating rows at the page seam --
  across instances, or once the cache entry has expired or been evicted, that reasoning no longer holds
  and the page seam can shift.
- **Filters and authorization apply before ranking.** The semantic gate is a CTE like any other: ordinary
  filters, compartment membership, access constraints and the resource-type allow-list all intersect with
  it before anything is ranked, so a semantic term narrows a filtered search rather than searching
  everything and filtering the ranked result.
- **Score.** `score = 1 - distance / 2`, where `distance` is SQL Server's `VECTOR_DISTANCE('cosine', ...)`
  (range 0..2), so score falls in `[0, 1]`. A match's score is the minimum distance over its chunks.
- **`minimumScore` gates matches**, it does not merely sort them: a row must satisfy
  `distance <= 2 * (1 - minimumScore)` to appear in the result set at all.
- **An explicit `_sort` takes priority over relevance.** The ORDER BY is `[_sort keys…], distance ASC,
  [identity tie-break]` -- a requested sort is honored first, and distance only breaks ties within it. Omit
  `_sort` to get pure relevance order.
- **One semantic parameter per search.** A query with more than one semantic term is rejected
  (`InvalidSearchOperationException`, "Only one semantic search parameter may be specified per search."):
  the result is ranked by one distance, and two semantic conditions have no single order to rank by.
- **Modifiers and chains are rejected**, not silently dropped: no modifier is defined for a semantic
  parameter (`:text`, `:exact`, `:missing`, etc. all presuppose a typed, filterable value it does not
  have), and a chain cannot terminate in one (a vector embedding has no reference identity to chain
  through, so both `subject:Patient.semantic-text=x` and the equivalent `_has` form are rejected). Both
  reject with HTTP 400 unconditionally -- the same SHALL-reject treatment as any other unsupported
  modifier -- because silently dropping either would widen the result set instead of narrowing it.
- **Paging is offset-based** and re-uses the cached query embedding described above; keyset paging is
  rejected for a semantic search because its seek predicate cannot express distance order.

### Feature disabled

When `VectorSearch:Enabled` is `false`, a semantic SearchParameter behaves exactly like an unknown
parameter: absent from the CapabilityStatement, no embedding generator is ever invoked, lenient handling
ignores it (with an `OperationOutcome` issue), and strict handling (`Prefer: handling=strict`) rejects the
search with HTTP 400. A malformed `vector-search-config` extension gets the same unsupported treatment,
whether or not the feature is enabled.

## Not yet supported

- **`Indexing.Mode = Asynchronous`.** Only synchronous (embed-before-merge) indexing exists; a background
  indexing worker is a documented follow-up in ADR-2610, not implemented in this slice.
- **Backfill of existing resources.** Activating a semantic SearchParameter does not reindex resources
  already in the database -- only subsequent creates and updates get vectors. There is no general reindex
  executor yet to drive a backfill from.
- **Bulk `$import`.** Imported resources are not embedded; only the ordinary create/update write path
  calls the embedding generator.
- **The FileSystem data layer.** Vector persistence exists only in the SQL Server data layer; see
  [Requirements](#requirements).
- **Approximate nearest-neighbor indexing.** Ranking is an exact `VECTOR_DISTANCE` scan over the gated
  candidate set -- there is no DiskANN or other ANN index, so large candidate sets pay for every
  comparison.
- **`Binary` content or resources reachable only through a reference.** Semantic text must come from the
  resource's own FHIRPath-extracted index value; nothing follows a reference to embed linked content.
- **`$export` with a semantic `_typeFilter`.** `_typeFilter` search expressions are parsed directly by
  `SearchOptionsBuilder` for the export worker, bypassing the query-time embedding step a normal search
  goes through, so a semantic term there fails rather than producing a filtered export.

## Wire compatibility

The SearchParameter contract -- the extension URL, its sub-extensions and their defaults, `special` as the
parameter type, and `Bundle.entry.search.score` as the result -- is the same contract
[microsoft/fhir-server](https://github.com/microsoft/fhir-server) proposed in #5802/#5803. A SearchParameter
definition and a client query written against one server work unchanged against the other. What differs is
internal: Ignixa embeds synchronously before `MergeResources` and writes vectors through a separate
post-merge procedure (so a provider failure never fails a committed write), and its SQL compiler lowers a
semantic term to a gating CTE plus a ranking `CROSS APPLY` rather than hand-built SQL.

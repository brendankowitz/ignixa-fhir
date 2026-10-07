# Investigation: Wire-Compatible Port of fhir-server Semantic Search

**Feature**: semantic-search
**Status**: In Progress
**Created**: 2026-10-05

## Approach

Adopt microsoft/fhir-server's **external contract** from [#5803](https://github.com/microsoft/fhir-server/pull/5803) and [#5802](https://github.com/microsoft/fhir-server/pull/5802) unchanged, and rebuild the **internals** around Ignixa's existing seams.

| Concern | fhir-server (#5802/#5803) | Ignixa port |
|---------|---------------------------|-------------|
| Definition | `special` SP + `vector-search-config` extension | Same contract; parsed into `SearchParameterInfo.VectorConfig` on both ingestion paths (`SearchParameterNavigator`, `PackageResourceMapper`) |
| Text source | `StringSearchValue` from the SP's FHIRPath | Same: `ElementSearchIndexer` already maps `special` → `StringSearchValue` |
| Embedding | Bespoke `IEmbeddingClient`, Azure Foundry only | `Microsoft.Extensions.AI` `IEmbeddingGenerator`; Azure OpenAI shipped, deterministic generator in tests |
| Chunking | tiktoken windows, Unicode-safe | Same algorithm via `Microsoft.ML.Tokenizers` |
| Write timing | Synchronous; vectors in the `MergeResources` TVP (atomic) | Synchronous embed **before** merge (provider failure fails the write cleanly); vectors written by a separate `MergeVectorSearchParams` **after** merge (failure logged, resource committed) |
| Storage | `VectorSearchParam` + `EmbeddingModel`, 5-column PK | Same tables, as schema version 5 in the DacFx sqlproj |
| Query | `CROSS APPLY TOP(1)` in hand-built SQL; custom distance continuation token | A gating `VectorMatchSource` CTE (composes with filters and authorization through the plan graph) plus a `VectorRankSpec` `CROSS APPLY MIN(distance)` on the match page; existing OFFSET continuation |
| Score | `Bundle.entry.search.score` | Same; `SearchEntryResult.Score` written by `StreamingBundleSerializer` |
| Backfill | Existing reindex lifecycle | Open: Ignixa's reindex is not implemented (see Verdict) |

## Tradeoffs

| Pros | Cons |
|------|------|
| SearchParameters and client queries are portable between servers | Vectors are not atomic with the resource write; a post-merge failure leaves the resource missing from semantic results until repaired |
| Reuses existing extraction, merge-repository post-merge hook, plan compiler, and serializer seams | Schema v5 requires a native-vector engine even when the feature is off (SQL 2019/2022 tenants stop at v4) |
| Provider-neutral; local dev needs no Azure | Synchronous mode puts the embedding provider in the write and query availability path |
| Gating through the CTE graph keeps filters and authorization ahead of ranking, with correct counts | Exact search computes distance twice (gate + rank) and scans the filtered candidate set; no ANN |
| No custom schema-model workaround (DacFx builds `vector(1536)` natively) | Import and pre-existing resources get no vectors until a backfill mechanism exists |

## Alignment

- [x] Follows layer rules: embedding lives in Application; the DataLayer only persists vectors and lowers prepared queries; Core gains contract and AST types only.
- [x] F5 Developer Experience: disabled by default; tests use a deterministic generator; docker-compose already runs SQL Server 2025.
- [x] FHIR spec compliance: `special` parameter type and `Bundle.entry.search.score` are standard. The extension is a vendor extension shared with fhir-server.
- [x] Consistent with existing patterns: post-merge side-table writes (`SqlServerPostMergeExtensionUpdater`, `ResourceTtl`), default-throwing visitor methods (`VisitCompositeComponent`), typed plan AST nodes.

## Evidence

**Upstream (read from the PR diffs, 2026-10-05):**
- `VectorSearchIndexer` builds every passage across a batch, then makes one ordered embedding call. Count and dimension mismatches throw.
- `AzureFoundryEmbeddingClient` splits requests at 2,048 inputs or 300,000 tokens and rejects any input over 8,192 tokens.
- `SqlQueryGenerator` ranks with `CROSS APPLY (SELECT TOP 1 VECTOR_DISTANCE(...))`, gates on `distance <= 2 * (1 - minimumScore)`, and pages with a `(distance, type, sid)` token.
- #5802 needed a local `VectorColumn` descriptor because the released schema generator lacks one.

**Ignixa seams:**
- Text is extracted in Application at `CreateOrUpdateResourceHandler.cs:228-285` (and Provenance at `:340-380`) and `DeferredWriteCoordinator.cs:185-207`. Conditional create and update delegate to that handler. Import activities extract separately.
- Post-merge non-atomic writes are already established at `SqlServerMergeRepository.cs:404-424`. That method also owns `resourceSurrogateIdMap`, so the vector writer needs no extra lookup.
- `SearchIndexTables` (`SqlServerFhirRepository.cs:54-69`), `HardDeleteResource.sql` and `DeleteHistory.sql` are the cleanup enumerations a new table must join.
- The plan compiler is typed: `QueryPlan` / `MatchPageSpec` / `SortSpec` / `CteDefinition` with emitters and validators (`src/Core/Ignixa.Search.Sql`). New sort kinds throw by design until registered (`SortKeyEmitter.For`).
- User searches use OFFSET paging (`SqlServerCompiledSearchService.DefaultOffsetPage`); keyset paging is export-only. So ranking needs no new continuation-token format.
- `SearchEntryResult` has no score; `search.mode` is written at `StreamingBundleSerializer.cs:121-134` and `:370-383`.

**Spike (throwaway, 2026-10-05):** a copy of `Ignixa.DataLayer.SqlServer.Database.sqlproj` with a `vector(1536)` table, a JSON-transport TVP and a `VECTOR_DISTANCE` procedure built with Microsoft.Build.Sql 2.2.0 under the `SqlAzureV12` DSP, with no new warnings. Deployment to the SQL Server 2025 container was not exercised.

**Hazards found that the port must handle (upstream shares several):**
1. `special` values go through `SearchValueSyntaxParser.ParseScalar`, which splits on unescaped commas. Without verbatim parsing, `chest pain, nausea` would become two OR'd embeddings.
2. Each page re-embeds the query. Providers do not guarantee bit-identical vectors, so distances can drift and rows can skip or repeat at page seams. Fix: a short-TTL query-embedding cache.
3. `StringSearchParameterRowGenerator` would also write the semantic text into `StringSearchParam`, duplicating up to `maxInputTokens` of text per resource. Semantic entries must be skipped.
4. The post-merge write races concurrent updates. The procedure must insert only for surrogates that are still current and delete vectors for every version of the resource.
5. Two `SearchParameterInfo` models exist: Core, and conformance via `PackageResourceMapper`. The vector config must survive the conversion.
6. `StringSearchValue` applies `UnescapeSearchParameterValue` to indexed text, so backslash sequences in clinical text are altered before embedding. Upstream behaves the same; accepted for compatibility.

**Relation to [reindex](../../reindex/):** [event-driven-triggering](../../reindex/investigations/event-driven-triggering.md) proposes per-type `ReindexOrchestration` jobs started by `SearchParameterActivated` events with exact SP lists. Activating a semantic SP is exactly that event. Backfill could therefore be a reindex consumer (re-extract → `SemanticIndexer` → vector writer) instead of the dedicated asynchronous worker [ADR-2610](../adr-2610-semantic-vector-search.md) currently proposes.

## Alternatives Worth Investigating

- **`reindex-backfill`**: embed during the reindex orchestration rather than in a semantic-specific backfill job. This depends on reindex landing; it removes one of ADR-2610's two asynchronous mechanisms.
- **`async-indexing-outbox`**: post-merge pending-vector rows drained by a DurableTask worker (`Indexing.Mode = Asynchronous`). Writes never touch the provider, at the cost of eventual consistency.
- **`filesystem-sidecar-vectors`**: sidecar vector files written on FileSystem writes, with brute-force cosine at query time.
- **`external-vector-store`**: rejected in ADR-2610 because it duplicates identity, tenancy and authorization. Record it formally if challenged.

## Verdict
*Pending evaluation.* The contract, synchronous write path and SQL query design are viable. The slice-1 implementation plan is ready. Open question before accepting ADR-2610: should backfill and asynchronous indexing be one dedicated worker, or should backfill be delegated to the reindex feature?

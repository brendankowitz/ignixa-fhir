# Investigation: Folder-Adaptive Local CLI + MCP for Agents

**Feature**: local-cli
**Status**: In Progress
**Created**: 2026-10-14

## Target use case

Let an LLM or agent make sense of **a folder of FHIR data it did not create**, quickly, using Ignixa's capabilities (search-parameter indexing, FHIRPath, validation, SQL-on-FHIR views, summaries), through a `dotnet tool` that offers both a CLI and an MCP stdio server. There is no HTTP endpoint. The data is small enough to scan fully into memory.

The first draft of this investigation framed the problem as "a CLI-driven FHIR server over the existing file provider". An independent review (see [Review](#review-and-what-changed)) showed that framing was wrong for this use case: the existing provider requires users' files to be *copied into Ignixa's own transaction layout*, which loses file provenance and is the opposite of "point it at a folder". This version centers the design on a read-mostly, layout-adaptive catalog instead.

## Approach

Build one **catalog** from a single walk of the folder, index it once, and serve both front doors from the same in-memory index. Files are never rewritten.

```text
 folder of FHIR files ──walk/sniff──▶ Catalog ──parse once──▶ search index (cached under .ignixa/)
 (any layout)                         (type,id,file,offset,len,...)        │
                                                                           ▼
        ignixa inspect|search|get|fhirpath|validate   ◀──────────  SearchQueryInterpreter
        ignixa mcp  (stdio)                           ◀──────────  + sort / include post-passes
```

### Catalog and discovery

The tool does not detect named "layouts"; one pass classifies files and records where each resource lives:

| Input shape | Detection | Catalog entry |
|-------------|-----------|---------------|
| `Patient/123.json`, any `*.json` single resource | extension, then first ~2 KB via `Utf8JsonReader` for `resourceType` | one entry, offset 0, whole file |
| `*.ndjson` / `*.jsonl` | extension | one entry per line: byte offset + length |
| `Bundle` JSON | `resourceType == "Bundle"` | one entry per `entry[].resource`, offset/length from the reader's token positions |
| Ignixa's own `tx-*.ndjson` + `_internal` sidecars | presence of `_internal/` | later phase; reuse sidecar indices as a fast path |

Rules, each reported by `inspect` rather than silently handled:
- **Missing `id`**: path hint (`Patient/123.json`) is a fallback only; otherwise a deterministic synthetic id (hash of path + ordinal), flagged non-referenceable.
- **Duplicate ids**: last-wins by `meta.lastUpdated`, then file mtime; every conflict is recorded; `--strict` fails instead.
- **Invalid JSON / unknown `resourceType`**: recorded as catalog error rows, never dropped.
- **Mixed FHIR versions**: out of scope. `--fhir-version` (default R4) is explicit, and parse/validation failure counts per version are reported.

**Random access** is seek-and-read of `length` bytes from the recorded offset. Nothing needs the whole corpus parsed to answer a `get`.

### Index

Parse each resource once, `JsonSourceNodeFactory.Parse` → `ToElement(schemaProvider)` → `ElementSearchIndexer.Extract`. This is exactly the pattern in `StreamingImportFileActivity.cs:366-391`. Persist the entries with the existing `CompactSearchIndexConverter` under `.ignixa/` (or a user cache directory when the folder is read-only), invalidated by a `(path, size, mtime)` manifest, with an optional `FileSystemWatcher`. Search reuses `SearchQueryInterpreter` unchanged for the predicate subset it supports. `_sort`, `_include`, `_revinclude` and chaining are post-passes over the snapshot (include = key lookup through reference entries; sort = a stable key with `_lastUpdated`, then id, as tiebreak).

### Index storage: own cache vs. SQLite vs. DuckDB

The expensive part of search is FHIR search semantics, not storage: date ranges and partial-date precision, token `system|code`, string normalization, `:not`, `:missing` and composites. `SearchQueryInterpreter` already implements these over the in-memory `(ResourceKey, SearchIndexEntry[])` shape. At the target size a linear in-memory scan is fast enough, so the on-disk cache is only a **startup optimization** (skip re-parse and re-index), not the query engine.

| Option | Role | Verdict |
|--------|------|---------|
| **Own cache format** | Persist `SearchIndexEntry` lists; reload instead of re-indexing | **v1.** Start with the existing `CompactSearchIndexConverter` JSON (no new format to own); move to a binary encoding (MemoryPack/MessagePack or custom) only if P0 shows load time matters. |
| **SQLite** (`Microsoft.Data.Sqlite`) | Search index as tables, queried with SQL | **Not v1.** Requires re-expressing FHIR search semantics in SQL. The only generator, `Ignixa.Search.Sql`, emits T-SQL for the SQL Server schema and is alpha and unwired (`src/Core/Ignixa.Search.Sql/README.md`). That would be a third copy of search semantics beside the in-memory interpreter and the SQL Server backend. |
| **DuckDB** (DuckDB.NET) | Columnar analytics over SQL-on-FHIR view output | **Optional add-on (P2/P3), not the index.** Aggregates ("encounters per year by class") are where agents get the most from SQL. Path: ViewDefinitions via `Ignixa.SqlOnFhir` → Parquet (`Parquet.Net` is already a dependency) → a read-only `sql_query` MCP tool. Ships a per-platform native library (size unmeasured), so keep it out of the base tool. |
| **Lucene.NET** | Full-text `_text` / `_content` | Only if full-text search becomes a requirement. |

Cache requirements, whichever encoding is used:
- **Invalidation key**: per-file `(path, size, mtime)`, plus FHIR version, indexer/cache-format version, and a hash of the loaded search-parameter definitions. Loading an IG with custom `SearchParameter`s changes what is indexed; a cache that ignores this returns wrong results silently.
- **Failure mode**: a cache that fails to load is rebuilt and the rebuild is reported. Never serve a partial index.

Triggers to revisit SQLite: datasets that no longer fit in memory (index must be paged), write-through with multiple concurrent processes (SQLite locking instead of a hand-rolled lock file), or incremental updates that outgrow per-file cache rewrites. Any of these turns into the larger question of a SQLite dialect for the search compiler.

### The `inspect` command

The first thing an agent needs is orientation, not search. `ignixa inspect <dir>` reports: resource types and counts, date ranges, profiles/`meta.profile` in use, FHIR-version hints, broken references, duplicate ids, parse errors, and the search parameters actually populated. It is also the natural first MCP tool.

### Interface strategy

Do not widen `IFhirRepository` / `ISearchService` in v1. Keep a narrow internal read contract (`IResourceCatalog`: enumerate keys, read bytes by key) that the CLI and MCP tools use directly. In a later phase, adapt it once to the Domain interfaces so existing handlers (`$validate`, SQL-on-FHIR, de-id, IPS) work against a folder. At that point unsupported members must throw `NotSupportedException` (no success-shaped fallbacks), history members report the single on-disk version truthfully, and the capability statement advertises read-only.

## Hosting models, revisited

| Model | Verdict for this use case |
|-------|---------------------------|
| **Libraries directly** (`Ignixa.Search`, `.FhirPath`, `.Specification`, `.Serialization`, `.Validation`) | **Phase 1.** Cheapest startup; `tools/Ignixa.Validation.Cli` already instantiates generated schema providers directly with no server stack. |
| **B. Slim Medino/Autofac host + folder adapter** | **Phase 3.** Viable: response shaping lives in Application, a non-HTTP request context factory exists, and the composition root is granular. Unlocks existing operations. |
| **A. In-process full server (`TestServer`)** | Rejected for the entry point (earlier lean). Boots OpenIddict, DB init and an index loader on every CLI call; adds an HTTP hop per verb. Remains the fallback if bundle/transaction behavior is ever needed. |
| **C. Single-owner daemon** | Deferred. Solves a write-concurrency problem a read-mostly tool does not have. |

## Tradeoffs

| Pros | Cons |
|------|------|
| Works on files as they are; no import step; provenance preserved | New provider/catalog code to own alongside the existing FileSystem layer |
| Startup cost is parse-and-index only; cacheable across invocations | Index freshness depends on mtime/watcher; external edits mid-session need a rebuild path |
| Reuses `SearchQueryInterpreter`, indexer, FHIRPath, validation unchanged | Search gaps (sort, include, chaining, compartments) still must be built |
| No write path means no locking, versioning, or transaction semantics in v1 | Read-only limits "agent edits the data" scenarios until a write-through phase |
| Narrow contract avoids implementing a 19-member repository interface up front | Two storage shapes to explain (Ignixa layout vs. discovered folder) |

## Alignment

- [x] Follows architectural layering rules: phase 1 sits in `tools/` over Core libraries; phase 3 adds `src/DataLayer/Ignixa.DataLayer.LocalFolder` implementing Domain interfaces. Nothing in Application references DataLayer implementations.
- [~] Developer Experience: `dotnet tool install` plus `ignixa mcp <dir>` is minimal. Cold-start latency for large folders is unmeasured; the cache is the mitigation.
- [~] Specification compliance: search behavior is whatever `SearchQueryInterpreter` supports plus the new post-passes. The capability statement (phase 3) must say read-only and list unsupported features.
- [x] Consistent with existing patterns: `System.CommandLine` 2.0.1, `PackAsTool`, existing MCP DTOs and `tool-design.md` limits (`_count` default 10, max 50, `_elements`).

## Evidence

Read at `b26718a`. Unless marked, verified by reading code; nothing was built, run, or benchmarked.

### The existing file provider is a poor fit for foreign folders
- Layout: bodies in `{type}/{YYYY}/{MM}/{DD}/tx-{txId}.ndjson`, per-version sidecars in `_internal/{type}/{id}/{txId}.metadata.json` holding version info and the pre-computed search index (`FileBasedFhirRepository.cs:139-173`). Search indices are produced on the write path and stored in the sidecar, so a foreign file has none until ingested.
- `GetAsync` re-parses all sidecars for the id, then parses every line of the transaction file to find the resource (`:342-362`, `:388-403`).
- Hard delete and purge throw `NotSupportedException` (`:1245-1254`); `SupportsPhysicalDeletion` is false. Expired-resource members return empty/`CompletedTask` (`:1217-1232`), which are success-shaped fallbacks.

### "InMemory" search is a per-query disk scan
- `FileBasedSearchService` calls `GetResourceMetadataAsync` per search (`:108, 225, 270`); every sidecar is parsed, the winner re-read, no cache (`FileBasedFhirRepository.cs:770-848`). The lazy predicate is enumerated three times (`FileBasedSearchService.cs:131, 160, 206`), and each page item then calls `GetAsync`.
- `Ignixa.Search.InMemory.InMemoryIndex` has no callers. `Search:IndexType` is read nowhere in `src/`; the real switch is tenant `Search.Type` (`FileBasedSearchServiceFactory.cs:62`).
- `IndexLoaderService` rescans every sidecar at server startup into `InMemoryResourceLocationIndex`, which no search code reads (only `DataLayerRegistration.cs:85` and the loader reference `IResourceLocationIndex`). It is dead startup cost for any host that reuses the full server.

### Search gaps
`SearchQueryInterpreter` throws for chained (`:135`), missing-field (`:145`), compartment (`:245`), `$everything` (`:253`), include (`:261`, bare `NotImplementedException`), sort (`:315`), `InExpression<T>` (`:323`), and `_not-referenced` (`:342`). `FileBasedSearchService` never reads `SearchOptions.Sort/Include/RevInclude`, and result order is `Directory.GetDirectories` order plus Skip/Take. Supported: string/token/date/number/reference/quantity/uri predicates, `:not`, and/or, `:missing` on search parameters, `_count`, continuation, `_total`, `_summary=count`.

### Hosting and HTTP coupling
- The full server boots headless on the file provider in `IgnixaApiFixture` under `TEST_USE_FILESYSTEM=true` (no SQL, no socket), but `Ignixa.Web/Program.cs` also runs OpenIddict and `InitializeDatabasesAsync`.
- `BundleEntryExecutor.cs:115-135` builds a `DefaultHttpContext` and requires a parent `IHttpContextAccessor`; transaction/batch bundles therefore need the HTTP pipeline.
- `FhirRequestContextFactory.CreateBackgroundContext` already provides a non-HTTP request context and is used by import and bulk-delete activities.
- Response shaping (paging links, `_elements`, Bundle assembly) lives in `Application/Features/Bundle/Serialization/StreamingBundleSerializer.cs` and `SearchResourcesHandler`, not in `Ignixa.Api`; the endpoints only invoke it. The composition root is granular (`ServiceCollectionExtensions.cs:91-116`) but lives in `Ignixa.Api`.
- `Application.csproj` has package references only; the ASP.NET `FrameworkReference` is in `Ignixa.Api.csproj`.

### MCP
- Existing tools: `search_fhir_resources`, `get_fhir_resource`, `get_fhir_resource_history`, `patch_fhir_resource`, `patch_resource_field`, `fhirpath_query_capability_statement`, package tools, `list_tenants_info`, job tools (`start_import_job`, `start_export_job`, `get_job_status`, `list_jobs`), plus a diagnostic tool. No create/update/delete tool, and no tool evaluates FHIRPath against a stored resource.
- Registered with `.WithHttpTransport()` only (`BackgroundServicesRegistration.cs:122-125`). `SearchResourcesTool.cs:138-141` throws without a request context and mutates `TenantId`; handlers also need `TenantConfiguration` (`SearchResourcesHandler.cs:59`), so a static single-tenant store is required outside HTTP.
- **Unverified**: `WithStdioServerTransport()` in `ModelContextProtocol 0.4.0-preview.3`. No restored package exists in this environment. SDK documentation indicates it is in the core `ModelContextProtocol` package, which `ModelContextProtocol.AspNetCore` references, and that tools are plain DI classes usable in a generic host. A spike must confirm the exact version.

### Defects in the existing provider (relevant only if reused)
- `GetNextVersionAsync` has `catch { return 1; }` (`:316-319`): a corrupt sidecar silently resets the version counter, in conflict with the AGENTS.md rule against swallowed exceptions and success-shaped fallbacks. Similar swallows exist at `:359-362` and `:823-826`.
- Write serialization is a process-local `SemaphoreSlim` (`:38`); two processes can assign the same version. Irrelevant for a read-only v1, relevant for the write-through phase.

### Prior art
Six `PackAsTool` CLIs exist under `tools/`. `Ignixa.SqlOnFhir.Cli run` already reads NDJSON directories by `*{resource}*.ndjson`. `docs/features/mcp-integration/investigations/tool-design.md` has the LLM response-size guidance to carry over.

## Review and what changed

An independent architecture review (run on a different model, given the draft and the target use case but not this document's conclusions) checked the draft's claims against the code. Outcome:

- All eight factual claims in the first draft were confirmed or partly confirmed; the review added the dead `IndexLoaderService`, the existing background request context, and that response shaping lives in Application.
- It disagreed with the draft's conclusions: Model A and storage options S1/S3 all assume copying files into Ignixa's layout, which does not fit "make sense of a folder I already have". It recommended the read-mostly catalog design above, with libraries first and the Domain adapter later.
- Effort figures below are the reviewer's estimate, not measurements.

## Phased plan

| Phase | Scope | Rough size (estimate) |
|-------|-------|-----------------------|
| **P0 spike** | Console under `tools/`: walk folder → catalog → index → `SearchQueryInterpreter`; report index time and memory for ~10k resources; confirm stdio transport on the pinned MCP package | 1-2 days |
| **P1** | `ignixa inspect\|search\|get\|fhirpath\|validate <dir>` over libraries only (no Application layer); catalog + sniff; index cache; sort/include post-passes | ~2 weeks |
| **P2** | Stdio MCP in the same tool: thin tools reusing `SearchResultsDto`/`ResourceEntryDto` and the `tool-design.md` limits; `inspect` as first tool | ~3-4 days |
| **P3** | `Ignixa.DataLayer.LocalFolder` adapter to `IFhirRepository`/`ISearchService` plus slim Autofac composition; unlocks `$validate`, SQL-on-FHIR, de-id, IPS; optional `Storage:Type=LocalFolder` for `Ignixa.Web` | TBD after P1 |
| **P4 (optional)** | Write-through to `{Type}/{id}.json` + reindex, lock file or single-owner process | TBD |

The reviewer's total for a read-only v1 (P0-P2) is roughly 3-4 engineer-weeks; treat as an order of magnitude until P0 reports.

## Open questions

1. How large are the folders this should handle? The design assumes up to tens of thousands of resources; beyond that the eager index needs measuring, and lazy per-type indexing may be needed.
2. Should the tool write a `.ignixa/` cache into the user's folder, or only to a user-level cache directory? Writing into a source folder surprises some users and fails on read-only mounts.
3. Is a read-only v1 acceptable, or are agent-initiated edits part of the first release?
4. Should P3 reuse existing sidecar indices when it detects an Ignixa-written folder, or keep one code path?
5. Which terminology and profile packages should `validate` load by default, and what does that cost at startup? Package loading is the main unmeasured startup cost.

## Verdict
*Pending evaluation*

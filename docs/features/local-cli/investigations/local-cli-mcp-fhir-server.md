# Investigation: Local CLI + MCP FHIR Server over the File Provider

**Feature**: local-cli
**Status**: In Progress
**Created**: 2026-10-14

## Approach

Ship a `dotnet tool` (working name `ignixa`) that treats a local directory as a FHIR server. There is no network listener. A CLI verb or an MCP tool call goes through the same code path as the matching REST interaction, against the existing FileSystem data layer, with search served from an in-memory index built by scanning the whole store.

```text
            ┌─────────────── ignixa (dotnet tool) ───────────────┐
 shell ───▶ │ CLI verbs  (read/search/create/update/patch/...)    │
 LLM   ───▶ │ MCP stdio  (search_fhir_resources, get_resource...) │
            └───────────────────────┬────────────────────────────┘
                                    ▼
                       in-process Ignixa host (no socket)
                    Application (Medino) → Domain interfaces
                                    ▼
              FileBasedFhirRepository  +  in-memory search index
                                    ▼
                            ./fhir-data/ (local folder)
```

The assumption that the dataset is small (thousands, not millions) is what makes this tractable. Every search can be a linear scan of an index held in RAM, and the hard parts of SQL search (paging stability, include/chain via joins, total counts) become simple loops.

Three hosting models are possible. They are the main decision this investigation has to inform.

| Model | Description |
|-------|-------------|
| **A. In-process full stack** | Boot the existing `Ignixa.Web` composition in-process with an in-memory transport (`TestServer`) or a Unix-domain-socket/named-pipe Kestrel. CLI verbs and MCP tools issue FHIR REST requests to it via `HttpClient`. |
| **B. Direct mediator host** | Generic host plus a slimmed composition root. CLI/MCP call Medino commands and queries directly (`SearchResourcesQuery`, etc.), skipping routing and HTTP. |
| **C. Single-owner daemon** | Either A or B, but one long-lived process owns the folder. CLI invocations and the MCP server are thin clients over a named pipe. |

## Tradeoffs

| Pros | Cons |
|------|------|
| Behavior parity with the real server if the REST pipeline is reused (Model A): conditional ops, PATCH rules, bundles, `OperationOutcome`, content negotiation, `_elements`/`_summary` | Model A drags the whole server (OpenIddict, gRPC sidecar clients, SQL registrations, conformance replay) into a CLI tool's startup and package size |
| Existing MCP tools (search, get, history, patch, FHIRPath, capability statement) already encode LLM-friendly limits (`_count` default 10, max 50) | MCP tools depend on a request context normally populated by HTTP middleware; stdio has none |
| Low operational burden: nothing to deploy, no ports, no auth surface; trust boundary is the OS user | Cross-process write safety does not exist today (see Evidence #6), so CLI and MCP running at once can corrupt version numbers |
| Natural fit with the existing `tools/` CLIs (`System.CommandLine`, `PackAsTool`) and with SQL-on-FHIR (`ignixa view run`) | The file layout is an opaque transaction log, not a folder of editable JSON; users who expect `Patient/123.json` will be surprised |
| Small-N assumption lets us implement `_include`, `_revinclude`, chaining and `_sort` as naive in-memory passes | The current "InMemory" search does not do those at all, and does not actually cache (Evidence #3, #4) |
| Same engine can back tests, demos, and offline agent sandboxes | Per-invocation CLI startup (package load, conformance replay) may make short-lived commands slow; unmeasured |

## Alignment

- [x] Follows architectural layering rules: new work lands in a new `tools/Ignixa.Local.Cli` (or similar) project plus DataLayer/Search changes; API → Application → Domain ← DataLayer stays intact. Model B needs care here because today the composition root lives in `Ignixa.Api`.
- [~] Developer Experience: `dotnet tool install -g` plus `ignixa mcp` is minimal setup. Startup latency and the folder-format question are the open risks.
- [~] Specification compliance: only if the capability statement truthfully advertises the reduced feature set (no history on some providers, no `$everything` in-memory, etc.). No success-shaped fallbacks.
- [x] Consistent with existing patterns: `System.CommandLine` 2.0.1, `PackAsTool`, `ModelContextProtocol` packages, Autofac registrations, `AppSettingsTenantConfigurationStore`.

## Evidence

All of this was read from the working tree at `b26718a`; nothing here was run or benchmarked.

### 1. The server already runs headless on the file provider

`test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs` derives from `WebApplicationFactory<Program>` and, when `TEST_USE_FILESYSTEM=true`, configures:

- `Tenants:Configurations:{0,1}:Storage:Type = FileSystem` (a per-tenant `Storage:BaseDirectory` under a temp dir),
- `DurableTask:Provider = FileSystem`, `BlobStorage:Provider = Local`,
- `Authentication/Authorization:Enabled = false`, `Experimental:Features:Mcp:Enabled = false`, package auto-load off.

So Model A is not speculative: the whole stack boots with no SQL Server and no socket. Tenant 0 (system partition) still has to be configured. Nothing here shows startup time.

### 2. What the "file provider" actually stores

`FileBasedFhirRepository` (`src/DataLayer/Ignixa.DataLayer.FileSystem/FileSystem/`):

- Resource bodies: `{type}/{YYYY}/{MM}/{DD}/tx-{txId}.ndjson` (date-partitioned, one file per transaction).
- Per-version sidecar: `_internal/{type}/{id}/{txId}.metadata.json`, holding version id, `lastModified`, deleted flag, request, and the pre-computed `SearchIndexes`.
- `GetAsync` finds the latest sidecar for the id, derives the body path from the transaction id and timestamp, then scans that NDJSON file for the id.

Consequences for the local-CLI idea:

- The store is not `Patient/123.json`. It is not hand-editable or `git diff`-friendly, and a user cannot "drop files in a folder".
- Search index entries are computed on the write path (`CreateOrUpdateResourceHandler` calls `IFhirVersionContext.GetSearchIndexer(...)`) and persisted in the sidecar. Anything ingested must go through that pipeline, or through the indexer directly, to be searchable.
- Because history is retained as sidecars, deletes and updates accumulate. `SupportsPhysicalDeletion` is `false` and `HardDeleteAsync`/`PurgeHistoryAsync` throw `NotSupportedException`, so there is no way to compact a local store today.

### 3. "InMemory" search is a per-query disk scan, not an in-memory index

`FileBasedSearchService.SearchStreamAsync` calls `FileBasedFhirRepository.GetResourceMetadataAsync(resourceType)` on every search. That method enumerates every `_internal/{type}/*` directory and, for each id, reads every `*.metadata.json` once to find the latest by `LastModified`, then reads the winner a second time. There is no cache and no invalidation. Cost per search is O(total versions of the type) file reads and JSON parses.

Separately, `Ignixa.Search.InMemory.InMemoryIndex` (the class that would index `IElement`s into a `ConcurrentDictionary`) has no callers outside its own file. `ADR-2509 inmemory-search` describes the design (key `{Type}:{param}:{value}`, `.metadata.ndjson` preloading) but the shipped code does not follow it. Also `Search:IndexType = InMemory` set in the E2E fixture is not read by any code I could find; the operative switch is the tenant's `Search.Type` checked in `FileBasedSearchServiceFactory`.

### 4. Search feature gaps in the in-memory path

`SearchQueryInterpreter` (`src/Core/Ignixa.Search/InMemory/`) throws for: `ChainedExpression`, `MissingFieldExpression`, `CompartmentSearchExpression`, `PatientEverythingExpression`, `NotReferencedExpression`, `IncludeExpression` (`NotImplementedException`), `SortExpression` (`NotImplementedException`), `InExpression<T>`. `FileBasedSearchService` itself applies no ordering, no `_include`/`_revinclude`, and paginates by skipping N items of whatever order `Directory.GetDirectories` returned. That makes paging order OS-dependent and unstable across writes.

Supported today: string/token/date/number/reference/quantity/uri predicates, `:not`, multiary and/or, `:missing` on search parameters (`VisitMissingSearchParameter`), `_count` and continuation, `_summary=count`, `_total`.

For parity with the REST surface described in the request, the missing items are `_sort`, `_include`/`_revinclude`, chaining and reverse chaining, compartment search, and `$everything`. With the small-N assumption each can be a simple in-memory pass (include = key lookup; chain = nested filter over reference entries; sort = LINQ over an extracted sort key).

### 5. The Application layer is mostly host-agnostic, with specific HTTP couplings

- `Ignixa.Application.csproj` has no ASP.NET `FrameworkReference`, but references `Microsoft.AspNetCore.Http*` packages; 14 source files mention `HttpContext`/`Microsoft.AspNetCore`.
- `BundleEntryExecutor` executes each batch/transaction entry by building a `DefaultHttpContext` and re-entering the ASP.NET pipeline. Bundles therefore need Model A (or a rewrite) to work.
- `CreateOrUpdateResourceHandler` can take its coordinator from `HttpContext.Items`.
- Handlers read `IFhirRequestContextAccessor` (35 files). The context (tenant, FHIR version, base URI, resource type) is populated by `FhirRequestContextMiddleware`/`TenantResolutionMiddleware`.
- Composition: `Ignixa.Web/Program.cs` calls `AddIgnixaApi` and `RegisterIgnixaServices`, which bundle core, SQL data layer, search, validation, package management, application, DurableTask, background jobs, experimental, and conformance registrations. There is no slim "headless" entry point; Model B means extracting one.

### 6. Concurrency: process-local locking only

`FileBasedFhirRepository` serializes writes with `SemaphoreSlim _writeLock`, and `GetNextVersionAsync` derives the next version by reading the folder. Two processes (for example an MCP server and a concurrently run CLI command) can both compute the same `versionId`. A single-owner design (Model C), a lock file, or refusing to open a store already locked by another process is needed before the CLI and MCP surfaces coexist. Lost updates here are silent, not errors.

### 7. MCP: existing tools, HTTP-only transport

- The assemblies already provide these MCP tools: `search_fhir_resources`, `get_fhir_resource`, `get_fhir_resource_history`, `patch_fhir_resource`, `patch_resource_field`, `fhirpath_query_capability_statement`, package tools (`install_`/`uninstall_`/`search_`/`list_fhir_packages`, `get_fhir_package_details`), `list_tenants_info`, and job tools (`start_export_job`, `start_import_job`, `get_job_status`, `list_jobs`), plus a diagnostic tool. There is no create, update, or delete tool, and no tool that evaluates FHIRPath against a stored resource (the FHIRPath tool queries the capability statement only).
- Registration (`AddIgnixaMcpServices`) uses `.AddMcpServer().WithHttpTransport().WithToolsFromAssembly(...)`, served at `/mcp` via `MapMcp`. Enabled by `Experimental:Features:Mcp:Enabled` (default true). Package: `ModelContextProtocol.AspNetCore 0.4.0-preview.3`.
- Tools extend `TenantAwareMcpTool` (explicit tenant → request context → single active tenant fallback), and `SearchResourcesTool` throws "FHIR request context not available - middleware may not have run" if no context exists. Over stdio there is no middleware, so a context provider is required.
- Whether `WithStdioServerTransport()` is available in this exact preview version, and how it composes with the host, has not been checked and is a spike item. I did not find a restored `ModelContextProtocol` package in this environment to inspect.
- `McpEnabledRoles` and the authorization service assume an authenticated principal. A local stdio server should run with authorization off and an explicit `--read-only` default for write tools, because the caller is an LLM acting with the user's full filesystem rights.

### 8. Prior art in the repo

- `tools/` has six CLIs (`Validation`, `SqlOnFhir`, `FhirFakes`, `DeId`, `SchemaUpgrade`, `ConformanceMatrix`) using `System.CommandLine` 2.0.1 and `PackAsTool`. None host the server stack.
- `Ignixa.SqlOnFhir.Cli run --input <ndjson file|dir>` already reads NDJSON directories; `Ignixa.FhirFakes.Cli` can generate seed data. Both are natural companions (`ignixa seed`, `ignixa view run`).
- `docs/features/mcp-integration/investigations/tool-design.md` has the LLM-oriented limits/`_elements` guidance that should carry over unchanged.

## Design notes

### Storage options

| Option | Source of truth | History | Notes |
|--------|-----------------|---------|-------|
| **S1. Existing FileSystem layout** | tx NDJSON + `_internal` sidecars | Yes | What the request names. Needs `import`/`export` to move plain files in and out. |
| S2. New plain-folder provider | `{Type}/{id}.json` | No (or via git) | Matches the "folder of FHIR resources" mental model and enables hand edits. Implements a subset of `IFhirRepository`; unsupported members must throw, and the capability statement must say so. Candidate for its own investigation. |
| S3. S1 + directory ingest/export | S1, with `ignixa import <dir>` / `ignixa export <dir>` | Yes | Cheapest way to make S1 usable with ordinary JSON files. Recommended first step. |

### In-memory search index

The simplest path that preserves existing semantics:

1. Add a process-wide, per-tenant, per-type snapshot of `(ResourceKey, IReadOnlyCollection<SearchIndexEntry>)` inside the FileSystem data layer, built lazily from the sidecars and updated write-through in `CreateOrUpdateAsync`/`DeleteAsync`/`BatchWriteAsync`. `SearchQueryInterpreter` consumes exactly this shape already, so no new predicate code is needed for the supported subset.
2. Build the snapshot in a single pass: pick the latest sidecar per id from file names/timestamps, parse once. Add a `reindex`-style rebuild path for externally modified stores.
3. Implement the gaps listed in Evidence #4 as post-filter stages over the snapshot.
4. Give `_sort` a stable default (`_lastUpdated`, then id) so continuation tokens mean something.

Rough sizing (estimate, not measured): 10k resources at ~30 index entries each is on the order of 10^5 small objects, plausibly tens of MB. Worth validating with a benchmark before committing to eager loading.

### Command / tool surface

| FHIR interaction | CLI | MCP |
|------------------|-----|-----|
| read / vread | `ignixa get Patient/123 [--version 2]` | `get_fhir_resource` (exists) |
| search | `ignixa search Patient "name=smith&_count=10" [--format json\|ndjson\|table]` | `search_fhir_resources` (exists) |
| create / update | `ignixa put <file\|->`, `ignixa post <file\|->` | new, write-gated |
| patch | `ignixa patch Patient/123 ...` | `patch_fhir_resource`, `patch_resource_field` (exist) |
| delete | `ignixa delete Patient/123` | new, write-gated |
| history | `ignixa history Patient/123` | `get_fhir_resource_history` (exists) |
| FHIRPath on a resource | `ignixa fhirpath Patient/123 "name.family"` | new (existing tool targets the capability statement only) |
| capabilities | `ignixa capabilities` | new or extend `fhirpath_query_capability_statement` |
| validate | `ignixa validate <file>` | new |
| bulk | `ignixa import <dir>`, `ignixa export <dir>` | `start_import_job` / `start_export_job` exist but go through background jobs; a synchronous local path is likely simpler |
| serve | `ignixa mcp --path ./fhir-data [--read-only]` | (the stdio server itself) |

Conventions: results on stdout, `OperationOutcome` on stderr, non-zero exit codes on FHIR errors, `--tenant` defaulting to the single configured tenant.

## Risks and open questions

1. **Startup latency.** What do conformance replay and spec/package loading cost on a cold `ignixa get`? If it is seconds, short-lived CLI invocations argue for Model C (daemon) or lazy-loading only the pieces a verb needs. Measure first.
2. **Cross-process safety** (Evidence #6). Decide between a lock file with fail-fast, a daemon, or documenting single-process use.
3. **Bundle/transaction support.** Requires the HTTP pipeline (Evidence #5). Is "no transactions in v1" acceptable?
4. **Where does the response-shaping logic live?** `_elements`, `_summary`, paging links and Bundle assembly appear to be in the API layer rather than Application; I have not confirmed this. If so, Model B has to duplicate or relocate it.
5. **Packaging.** `Microsoft.AspNetCore.App` framework reference is acceptable for a tool, but trimming/AOT is unlikely. Size and install time need a number.
6. **Auth model.** Confirm that disabling authorization is correct for stdio and that MCP write tools default to off.
7. **Multi-tenancy.** Single-tenant default should be zero-config. Multi-tenant stores are out of scope for v1, and tenant partition `0` must remain unreachable.
8. **FHIR version.** The tenant's `FhirVersion` is fixed at configuration time; the CLI needs `--fhir-version` (STU3, R4, R4B, R5) and must refuse to open a store with a mismatched version.

## Suggested next steps

1. **Spike (timeboxed):** a throwaway console project that boots Model A with `TestServer` over a FileSystem tenant, issues one `GET /Patient?name=x`, and prints cold and warm timings. Reuse the configuration keys from `IgnixaApiFixture`.
2. **Spike:** stdio MCP in the same project, wired to the existing tools, with a minimal `IFhirRequestContext` provider. Confirms the transport API in `ModelContextProtocol 0.4.0-preview.3`.
3. **Prerequisite work independent of hosting choice:** cached in-memory snapshot in the FileSystem search path, deterministic ordering, `_sort`, `_include`/`_revinclude`, and chained search, each with tests following the project's xUnit/Shouldly conventions and run against the supported FHIR versions.
4. Draft the cross-process locking decision as a short ADR before any write verb ships.

## Verdict
*Pending evaluation*

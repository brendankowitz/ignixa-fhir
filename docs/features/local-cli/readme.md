# Feature: Local CLI + MCP for Folders of FHIR Data

A `dotnet tool` (prototype working name **cli-on-fhir**) that lets LLMs and agents quickly make sense of a folder of FHIR data using Ignixa's search indexing, FHIRPath, validation and related capabilities. It offers a CLI and an MCP stdio server, with no HTTP endpoint. A read-mostly, layout-adaptive catalog indexes files where they are rather than importing them into the server's storage layout.

## Investigations

| Investigation | Status | Created | Description |
|--------------|--------|---------|-------------|
| [local-cli-mcp-fhir-server](investigations/local-cli-mcp-fhir-server.md) | In Progress | 2026-10-14 | Folder-adaptive catalog + in-memory index, hosting options, index storage, search gaps, phased plan |
| [agent-tool-surface](investigations/agent-tool-surface.md) | In Progress | 2026-10-15 | Formal capability set for agents (orient, navigate, query, terminology, check, transform), response envelope, terminology store, search extensions from fhir-candle, prototype stance |

## Candidate investigations (not yet started)

- **write-through-and-locking**: agent-initiated edits to `{Type}/{id}.json`, reindexing, and cross-process safety (the existing file provider's write lock is process-local).
- **upstream-candle-inmemory-search**: move fhir-candle's include, chaining, `_has`, `_sort`, compartment and matching-fix work into `Ignixa.Search.InMemory` behind opt-in options (outlined in agent-tool-surface).
- **terminology-store**: local terminology store and native loaders (SNOMED CT RF2, LOINC, RxNorm, ICD-10-CM), likely SQLite-backed for large systems; candidate basis for a non-SQL-Server `ITerminologyService`.
- **sqlite-index-store**: SQLite as the resource search index for datasets that outgrow memory or need multi-process write-through; implies a SQLite dialect of `Ignixa.Search.Sql`.
- **duckdb-analytics**: DuckDB over SQL-on-FHIR Parquet output, exposed as a read-only `sql_query` MCP tool for agent analytics.
- **folder-validation-and-profiles**: default package/terminology loading for `validate` and its startup cost.

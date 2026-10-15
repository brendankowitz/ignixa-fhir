# Feature: Local CLI + MCP for Folders of FHIR Data

A `dotnet tool` that lets LLMs and agents quickly make sense of a folder of FHIR data using Ignixa's search indexing, FHIRPath, validation and related capabilities. It offers a CLI and an MCP stdio server, with no HTTP endpoint. A read-mostly, layout-adaptive catalog indexes files where they are rather than importing them into the server's storage layout.

## Investigations

| Investigation | Status | Created | Description |
|--------------|--------|---------|-------------|
| [local-cli-mcp-fhir-server](investigations/local-cli-mcp-fhir-server.md) | In Progress | 2026-10-14 | Folder-adaptive catalog + in-memory index, hosting options, search gaps, MCP/CLI surface, phased plan |

## Candidate investigations (not yet started)

- **write-through-and-locking**: agent-initiated edits to `{Type}/{id}.json`, reindexing, and cross-process safety (the existing file provider's write lock is process-local).
- **search-gap-closure**: `_sort`, `_include`/`_revinclude`, chaining and compartments over an in-memory snapshot, shared with the existing InMemory search path.
- **sqlite-index-store**: SQLite as the search index for datasets that outgrow memory or need multi-process write-through; implies a SQLite dialect of `Ignixa.Search.Sql`.
- **duckdb-analytics**: DuckDB over SQL-on-FHIR Parquet output, exposed as a read-only `sql_query` MCP tool for agent analytics.
- **folder-validation-and-profiles**: default package/terminology loading for `validate` and its startup cost.

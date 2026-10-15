# cli-on-fhir (prototype)

Explore a local folder of FHIR data from the command line or from an agent over MCP (stdio). No HTTP endpoint, no database: the folder is catalogued where it is and indexed in memory with Ignixa's search indexer.

Status: alpha prototype. Design and roadmap: [`docs/features/local-cli/`](../../docs/features/local-cli/readme.md).

## Usage

```bash
# Overview of a folder: resource types and counts, patients, problems found, build cost
cli-on-fhir inspect ./my-fhir-data --fhir-version r4

# Serve the folder to an MCP client over stdio
cli-on-fhir mcp ./my-fhir-data
```

Example MCP client configuration (Claude Code, `.mcp.json`):

```json
{
  "mcpServers": {
    "fhir-folder": { "command": "cli-on-fhir", "args": ["mcp", "./my-fhir-data"] }
  }
}
```

## What it reads

- `*.json`: a single resource, or a Bundle whose `entry[].resource` items are catalogued individually.
- `*.ndjson`, `*.jsonl`: one resource per line.
- The folder is never modified. A `.ignixa/` directory inside it is skipped by the catalog but nothing writes there.

Problems (invalid JSON, objects without `resourceType`, duplicate ids, resource types unknown to the chosen FHIR version) are reported in `issues`, never dropped silently.

## Index cache

Search indexes are persisted per resource type so a new process does not pay for indexing again. The cache lives outside the data folder, in `$XDG_CACHE_HOME/cli-on-fhir` (else `~/.cache/cli-on-fhir`, or `%LOCALAPPDATA%\cli-on-fhir` on Windows), in one subdirectory per folder and FHIR version.

- `--cache-dir <path>` moves the cache root; `--no-cache` neither reads nor writes it. A cache directory inside the data folder (or a data folder inside the cache root) is refused with an `index-cache-unavailable` issue (links are resolved and case is ignored when comparing), because the folder is never written and cache files would be read as data.
- A manifest records the cache format, FHIR version, tool build, the module versions of the indexing assemblies, a hash of the search parameter definitions and every scanned file's size and last-write time. Any difference discards the whole cache and is reported as an `index-cache-rebuilt` issue with the reason and the number of changed files.
- An unreadable or inconsistent cache file is rebuilt and reported as `index-cache-corrupt`; an unwritable cache directory is reported once as `index-cache-unavailable`; a type holding a value the cache cannot reproduce exactly is left uncached and reported as `index-cache-unsupported-value`. Neither fails the call.
- `inspect --build-index` reports per type whether the index came from the cache or was built, with load, build and write times.

## Tools

| MCP tool | CLI verb | Status |
|----------|----------|--------|
| `folder_inspect` | `inspect` | Implemented |

`freshness.staleFiles` is recounted by listing the folder and comparing each file's size and last-write time with what the catalog recorded (the count is reused for 2 seconds so back-to-back tools share one walk). If the folder can no longer be listed the value is null and a `freshness-unavailable` warning says why.

Every response uses the shared envelope: `facts`, `derived` (always null), `evidence`, `omitted`, `issues`, `freshness`.

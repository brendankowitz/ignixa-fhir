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
| `search_fhir_resources` | `search <path> <type> [query]` | Implemented (no `_sort`, `_include`, chains or `_has` yet: they fail as `search-not-supported`) |
| `read_resources` | `get <path> <ref>...` | Implemented |

Search evaluates FHIR queries with a copy of fhir-candle's interpreter (see `Search/Candle/`), which matches multi-field criteria such as `system|code` within one index entry and compares both bounds of periods and partial dates. A parameter that does not filter is a `parameter-ignored` warning; a request that cannot be answered fails with an `error` issue (exit code 1 on the CLI, `isError` over MCP) and no facts.

The 50 KB cap applies to the decoded tool-result text, i.e. the whole serialized envelope (resources, evidence, issues, omitted), not to the JSON-RPC frame, which escapes that text again and is larger. The CLI prints the same compact JSON by default; `--pretty` indents it (and so exceeds the cap).

What a search may leave unapplied is decided in one place (`Search/ParameterPolicy.cs`):

- A parameter that would change which resources match or which page is returned fails the request as `search-not-supported`: the filters `_text`, `_content`, `_query`, `_list`, `_filter`, `_has` and `_not-referenced`; `_sort`, `_include`, `_revinclude`; chains; continuation tokens (`ct`, `after`; page with `offset`); `_type` unless it names exactly the searched type; and anything the parser recognises but cannot evaluate (composite `ap`/`sa`/`eb`, unsupported modifiers).
- Only these are ignored, each with a `parameter-ignored` warning naming it: `_format`, `_pretty`, `_contained`, `_containedType`, `_includesCount`, `_includesContinuationToken`, `_summary` values other than `count`, `_type` naming the searched type, a parameter with no value, and names the search pipeline does not recognise (as FHIR allows servers to do for unknown parameters). `param=|code` matches a code with no system.

`_lastUpdated` is indexed from `meta.lastUpdated` only when it is a FHIR instant (date, time and timezone); anything else is reported as an `index-extraction-failed` issue and the resource does not match `_lastUpdated`.

`freshness.staleFiles` is recounted by listing the folder and comparing each file's size and last-write time with what the catalog recorded (the count is reused for 2 seconds so back-to-back tools share one walk). If the folder can no longer be listed the value is null and a `freshness-unavailable` warning says why.

Every response uses the shared envelope: `facts`, `derived` (always null), `evidence`, `omitted`, `absent`, `issues`, `freshness` (`indexBuiltAt`, `staleFiles`). All of them are always present, as `null` where there is no value (for example `facts` of a failed call or `indexBuiltAt` when the folder cannot be read).

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
- An unreadable or inconsistent cache file is rebuilt and reported as `index-cache-corrupt`; an unwritable cache directory is reported once as `index-cache-unavailable`. Neither fails the call. Values are persisted in Ignixa's lossless compact search value format (`Ignixa.Search.Serialization`), the same one FileSystem sidecars use.
- `inspect --build-index` reports per type whether the index came from the cache or was built, with load, build and write times.

## Tools

| MCP tool | CLI verb | Status |
|----------|----------|--------|
| `folder_inspect` | `inspect` | Implemented |
| `search_fhir_resources` | `search <path> <type> [query]` | Implemented, including chains, `_has`, `_sort`, `_include` and `_revinclude` |
| `read_resources` | `get <path> <ref>...` | Implemented |

Search parses queries with Ignixa's own search pipeline and evaluates them with Ignixa's in-memory interpreter (`Ignixa.Search.InMemory.SearchQueryInterpreter`), which matches multi-field criteria such as `system|code` within one index entry (and, for composites, one value of each component), compares the bound of a period, partial date or range that the FHIR prefix table names, and folds accents in string search. The same engine (`Ignixa.Search.InMemory.InMemorySearchEngine`, every feature enabled) evaluates:

- forward chains to any depth (`subject:Patient.name=Smith`; untyped `subject.gender=female` when only one target type defines the parameter, otherwise the call fails naming the typed forms) and reverse chains (`_has:Observation:subject:code=8867-4`), over the indexes of the types they name, built on first use;
- `_sort` on date, number, quantity, string, token and uri parameters and `_id`, several keys and `-` for descending. A resource with several values sorts by its lowest ascending and its highest descending; one with no value comes last either way; ties fall back to `_lastUpdated` (oldest first) and then id, so paging is stable. A sort key that cannot be ordered fails the call;
- `_include` and `_revinclude`, with `:iterate` and `Type:*`, for the returned page only. Included resources come back whole (not trimmed by `elements`) in `facts.included`, at most 50 (resolution stops at the cap, so `:iterate` cannot run away); they are not counted in `total` or `returned`, have their own evidence, and anything left out (the cap or the size budget) is listed in `omitted`. An include that can add nothing to the search (`Observation?_include=Patient:organization`, or a `_revinclude` whose parameter never references the searched type) is an `include-not-applicable` warning. A reference to another server, or to a resource the folder does not hold, includes and chains to nothing; references that are not local `Type/id` references (`urn:uuid` inside Bundles, absolute URLs, bare ids, and canonicals under a parameter that also selects references or under `Type:*` and `*`) are not followed and are counted, per reference parameter followed, in a `references-unresolved` information issue, so an empty chain or include is not mistaken for a complete answer.

The diagnostics of every index a search read (the searched type and any type a chain or include read) are reported in `issues`, since a resource that could not be indexed is silently absent from a chain or include otherwise. A request that cannot be answered fails with an `error` issue (exit code 1 on the CLI, `isError` over MCP) and no facts.

The 50 KB cap applies to the decoded tool-result text, i.e. the whole serialized envelope (resources, evidence, issues, omitted), not to the JSON-RPC frame, which escapes that text again and is larger. The CLI prints the same compact JSON by default; `--pretty` indents it (and so exceeds the cap).

What a search may leave unapplied is decided in one place (`Search/ParameterPolicy.cs`):

- A parameter that would change which resources match or which page is returned fails the request as `search-not-supported`: the filters `_text`, `_content`, `_query`, `_list`, `_filter` and `_not-referenced`, and any other parameter the type defines but the pipeline does not evaluate (such as `_source`); continuation tokens (`ct`, `after`; page with `offset`); `_type` unless it names exactly the searched type; a `_sort` key that cannot be ordered; and anything the parser recognises but cannot evaluate (unsupported modifiers, ambiguous chains).
- A parameter name the pipeline does not recognise, including a wrong-case one (`Family`, `_lastupdated`) or one inside a chain (`general-practitioner:Practitioner.familly`, checked against the chain's target type), fails the request as `unknown-search-parameter` with a "did you mean" hint and the type's parameter names; a chain link that can never resolve (`_has:Observation:subject:code` on Observation, which `subject` never references) fails as `invalid-search-request`; `_offset` and `_page` point at the `offset` argument. Pass `lenient` (MCP) or `--lenient` (CLI) to ignore such names with a `parameter-ignored` warning instead. **Lenient mode can widen results**: a misspelt filter is dropped and the search returns more than was asked for, so check `issues` before trusting the total.
- Only these are ignored, each with a `parameter-ignored` warning naming it, in both modes: `_format`, `_pretty`, `_contained`, `_containedType`, `_includesCount`, `_includesContinuationToken`, `_summary` values other than `count`, `_type` naming the searched type, and a parameter with no value. `param=|code` matches a code with no system.
- Date `ap` widens by 10% of the distance between the value and the current time (Ignixa's table, shared with the SQL backend), so an `ap` result can change as time passes; other prefixes do not depend on the clock.

`_lastUpdated` is indexed (by `Ignixa.Search.InMemory.LastUpdatedSearchIndexer`) from `meta.lastUpdated` only when it is a FHIR instant (date, time and timezone); anything else is reported as an `index-extraction-failed` issue and the resource does not match `_lastUpdated`.

The folder index is Ignixa's own (`SearchIndexerFactory`) plus the in-memory `_lastUpdated` entry, so it holds what the server's index holds: ContactPoint is indexed as its value with no system, as the search page's token mapping specifies (`telecom=555-1234`; `telecom=phone|555-1234` does not match, use the `phone` or `email` parameter); a Quantity whose `unit` differs from its `code` is also indexed under the unit (`value-quantity=185||lbs`); a `urn:` reference (a Bundle's `urn:uuid` fullUrl) is kept as an untyped reference, so `subject=urn:uuid:...` matches it and chains and includes can report it; a `canonical` under a reference parameter (R5 `_profile` over `meta.profile`, `instantiates-canonical`) is added in memory only (`CanonicalReferenceSearchIndexer`; the server stores nothing for it until #430) and matched as a canonical: `url` matches every version, `url|1.0` only that one; a chain, `_has` or include through a parameter that selects only canonicals fails as `search-not-supported` whatever the data, while one through a parameter that also selects references (R5 `RequestOrchestration-participant`) follows the references and counts the canonicals in `references-unresolved`; and a Range indexes as a number or quantity range (R4 `probability`).

`freshness.staleFiles` is recounted by listing the folder and comparing each file's size and last-write time with what the catalog recorded (the count is reused for 2 seconds so back-to-back tools share one walk). If the folder can no longer be listed the value is null and a `freshness-unavailable` warning says why.

Every response uses the shared envelope: `facts`, `derived` (always null), `evidence`, `omitted`, `absent`, `issues`, `freshness` (`indexBuiltAt`, `staleFiles`). All of them are always present, as `null` where there is no value (for example `facts` of a failed call or `indexBuiltAt` when the folder cannot be read).

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
- The folder is never modified. `.ignixa/` is reserved for a future index cache and is skipped.

Problems (invalid JSON, objects without `resourceType`, duplicate ids, resource types unknown to the chosen FHIR version) are reported in `issues`, never dropped silently.

## Tools

| MCP tool | CLI verb | Status |
|----------|----------|--------|
| `folder_inspect` | `inspect` | Implemented |

Every response uses the shared envelope: `facts`, `derived` (always null), `evidence`, `omitted`, `issues`, `freshness`.

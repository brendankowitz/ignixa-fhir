# Feature: Local CLI + MCP FHIR Server

A CLI-driven, local-first FHIR server: no network listener, a folder of FHIR resources as the data store, an in-memory search index, and two front doors (a `dotnet tool` CLI and an MCP stdio server) that expose the same REST-equivalent interactions (read, vread, search, create, update, patch, delete, history, operations).

## Investigations

| Investigation | Status | Created | Description |
|--------------|--------|---------|-------------|
| [local-cli-mcp-fhir-server](investigations/local-cli-mcp-fhir-server.md) | In Progress | 2026-10-14 | Hosting models, file-provider gaps, and MCP/CLI surface for a headless local server |

## Candidate investigations (not yet started)

- **plain-folder-provider**: a storage provider whose source of truth is `{Type}/{id}.json` (human-editable, git-diffable) instead of the transaction/sidecar layout.
- **library-first-no-server-stack**: a thin library over `Ignixa.Search` + `Ignixa.FhirPath` that bypasses the Application layer entirely.
- **daemon-and-thin-clients**: single-owner background process; CLI and MCP connect over a named pipe / Unix domain socket.

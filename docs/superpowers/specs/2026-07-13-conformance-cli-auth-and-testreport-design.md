# Conformance CLI auth header and TestReport output

## Summary
Add authentication support to `ignixa-matrix run` and make FHIR `TestReport` its default output:

- `--auth-header <value>` sets an authentication header for all HTTP requests made during the run.
- `--format <fhir|json>` selects the shape of the `--out` file. `fhir` (the default) writes a
  `Bundle` of `TestReport` resources; `json` writes this tool's native per-impl report.

## User experience

`--out` is always the report file — the format is chosen by `--format`, not by a second path option.

```bash
ignixa-matrix run \
  --server https://example.org/fhir \
  --tests ./testscripts \
  --impl my-server \
  --out ./reports/my-server.json \
  --auth-header "Bearer abc123"
```

The auth option accepts either:

- a raw credential such as `Bearer abc123`
- a full header declaration such as `Authorization: Bearer abc123` or `X-Api-Key: abc123`

If a raw value is supplied without a header name, the CLI applies it as the `Authorization` header.
An HTTP header name cannot contain whitespace, so the text before the first colon is treated as a
header name only when it has none; anything else is a bare credential. This supports arbitrary
schemes (`Negotiate`, `NTLM`, `AWS4-HMAC-SHA256`) and credentials containing colons without
enumerating scheme names.

### `--format`

| Value | Output |
|-------|--------|
| `fhir` (default) | A `Bundle` (`type: collection`, with `timestamp`) of `TestReport` resources — one entry per executed TestScript, each with a `TestReport/<slug>` `fullUrl`. |
| `json` | The native per-impl report (`ImplReport`) — the shape `merge` consumes to build the matrix. |

**This changes the default.** Runs that feed `ignixa-matrix merge` must now pass `--format json`;
`merge` deserializes `ImplReport` and rejects a `TestReport` Bundle with a JSON error and a non-zero
exit rather than silently producing an incorrect matrix.

The emitted `TestReport` matches the shape `ignixa-lab`'s frontend produces (`frontend/src/lib/testReport.ts`),
so the two tools' output is interchangeable. Blob-permalink `testScript.reference` values are out of
scope: `--tests` is an arbitrary folder path and the CLI has no equivalent of ignixa-lab's
SourceLink-stamped package revision, so the script is identified by `testScript.display` only.

## Implementation notes

- Extend `tools/Ignixa.ConformanceMatrix.Cli/Commands/RunCommand.cs` with the new options.
- Apply the auth header to the shared `HttpClient` used by the TestScript engine so every request
  inherits it. An `--auth-header` that parses to an empty value throws rather than returning quietly —
  running the suite unauthenticated would report every test as a legitimate 401 failure.
- Capture each executed `TestScriptReport` and serialize it via `TestReportResourceGenerator`, passing
  a `TestReportContext` carrying the impl name (`tester`), server URL (`server` participant), and the
  suite-relative file path (`testScript.display`).
- Always write a `Bundle`, even for a single script, so consumers do not have to branch on
  `resourceType`.
- Keep `--format json` output byte-compatible with the existing `ImplReport` shape.

## Core change

`TestReportResourceGenerator.Generate` did not emit `TestReport.testScript`, which is 1..1 in R4 —
every consumer produced an invalid resource, not just this CLI. It now always emits a display-only
`testScript` Reference, plus `score` and a `test-engine` participant. The new `TestReportContext`
parameter is optional, so existing `Generate(report)` call sites are unaffected.

A relative file path belongs in `testScript.display`, not `testScript.reference`: a relative
`Reference.reference` is parsed as `[type]/[id]`, so `Search/intervals.json` would be read as a
resource of type `Search`.

## Testing

- parsing/normalizing the auth header input, including a custom scheme whose credential contains a colon
- bundling generated `TestReport` resources, including `fullUrl` slugs and the Bundle `timestamp`
- `testScript` fallback to the script name when no context is supplied
- `score` as a percentage of passing tests, and the empty-test-list case
- `participant` omitting the server entry when no server URI is supplied

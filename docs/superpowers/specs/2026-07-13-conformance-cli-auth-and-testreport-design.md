# Conformance CLI auth header and TestReport output

## Summary
Add two opt-in CLI features to `ignixa-matrix run`:

- `--auth-header <value>` sets an authentication header for all HTTP requests made during the run.
- `--test-report <path>` writes the executed TestScript results as a FHIR `TestReport` JSON file (or a `Bundle` of `TestReport` resources when more than one script is executed).

## User experience

The existing matrix report output remains unchanged. The new options are optional and do not alter the default behavior of the command.

Examples:

```bash
ignixa-matrix run \
  --server https://example.org/fhir \
  --tests ./testscripts \
  --impl my-server \
  --auth-header "Bearer abc123" \
  --test-report ./reports/test-report.json
```

The auth option accepts either:

- a raw header value such as `Bearer abc123`
- a full header declaration such as `Authorization: Bearer abc123`

If a raw value is supplied without a header name, the CLI applies it as the `Authorization` header.

## Implementation notes

- Extend `tools/Ignixa.ConformanceMatrix.Cli/Commands/RunCommand.cs` with the new options.
- Apply the auth header to the shared `HttpClient` used by the TestScript engine so every request inherits the configured header.
- Capture each executed `TestScriptReport` and serialize it via the existing `TestReportResourceGenerator`.
- If multiple scripts are executed, write a `Bundle` with `type=collection` and one `entry.resource` per `TestReport`.
- Keep the current `--out` JSON matrix output behavior intact.

## Testing

Add tests for:

- parsing/normalizing the auth header input
- bundling one or more generated `TestReport` resources into the output payload

# Ignixa.ConformanceMatrix.Cli

`ignixa-matrix` runs folders of FHIR [TestScript](https://hl7.org/fhir/testscript.html) conformance
suites against a live FHIR server and merges per-implementation reports into a publishable
conformance matrix.

## Installation

```bash
dotnet tool install -g Ignixa.ConformanceMatrix.Cli
```

## Usage

Run a conformance suite against a server. `--out` is the report file; `--format` selects its shape,
defaulting to a FHIR `Bundle` of `TestReport` resources — one per executed TestScript:

```bash
ignixa-matrix run --server https://your-fhir-server --tests ./src/Core/Ignixa.TestScript.Suites/testscripts \
  --impl my-server --out ./reports/my-server.json
```

To authenticate requests, supply an auth header value:

```bash
ignixa-matrix run --server https://your-fhir-server --tests ./src/Core/Ignixa.TestScript.Suites/testscripts \
  --impl my-server --out ./reports/my-server.json \
  --auth-header "Bearer <token>"
```

`merge` consumes this tool's native per-implementation report rather than FHIR `TestReport`, so pass
`--format json` when the run feeds the matrix:

```bash
ignixa-matrix run --server https://your-fhir-server --tests ./src/Core/Ignixa.TestScript.Suites/testscripts \
  --impl my-server --out ./reports/my-server.json --format json
```

Merge per-implementation reports into the matrix output (`runs/` + `index.json`):

```bash
ignixa-matrix merge --results ./reports --out ./matrix \
  --commit "$(git rev-parse HEAD)" --branch main
```

### `--format`

| Value | Output |
|-------|--------|
| `fhir` (default) | A FHIR `Bundle` (`type: collection`) — a `TestReport` per executed TestScript, plus an `OperationOutcome` per script that could not be run. |
| `json` | This tool's native per-implementation report — the shape `merge` reads. |

## Behavior

- `run` exits non-zero when any test fails **or errors** — an engine or transport error is never
  reported as a pass. Crashed scripts are recorded as `error` cells rather than aborting the run,
  and parse warnings are printed per file.
- Scripts that never ran — a file that failed to parse, or one whose execution threw — have no
  `TestReport` to speak for them, so under `--format fhir` each is recorded as an `OperationOutcome`
  entry: `structure` for a parse failure, `exception` for an evaluator error, with the file name in
  `diagnostics`. Without these, a suite whose scripts all failed to parse would write a `Bundle`
  with no entries, indistinguishable from a clean run of nothing.
- `--fhir-version` sets the `fhirVersion` parameter on the `Accept` header for version-gated suites.
- `merge` replaces an existing run with the same id instead of duplicating it, and refuses to
  proceed when a report file is unreadable.

### Exit codes

`run` distinguishes a broken invocation from a completed run with failures, so CI can tell "nothing
ran" from "the suite ran and 3 tests failed":

| Code | Meaning |
|------|---------|
| `0` | Every test passed or was skipped. |
| `1` | The suite ran; at least one test failed or errored. |
| `2` | Usage error — an unusable `--tests`, `--server`, `--auth-header`, or `--out`. Nothing ran. |
| `3` | Unexpected internal failure. The full exception, including its stack and inner chain, is printed to stderr. |

`--out` is validated **before** the suite runs, so a bad path fails fast rather than discarding a
completed run against a live server.

## `serve`

Hosts TestScripts as a local load-test runner: a Kestrel listener that parses every `*.json` under
`--tests` once at startup and evaluates one on demand per `POST /run`, instead of running a suite
once and exiting. It is designed to run as a sidecar — spawned once per load-generator instance
(e.g. an Azure Load Testing / Locust engine) and driven over `127.0.0.1`.

```bash
ignixa-matrix serve --tests ./src/Core/Ignixa.TestScript.Suites/testscripts --port 5599
```

| Option | Default | Meaning |
|--------|---------|---------|
| `--tests` | *(required)* | Folder containing TestScript `.json` files, scanned recursively. |
| `--port` | `5599` | TCP port to listen on. |
| `--host-ip` | `127.0.0.1` | IP address to bind the listener to. |
| `--fhir-version` | *(none)* | Default FHIR version forwarded to every evaluation and the Accept header; a per-call `fhirVersion` in the `/run` request body overrides it. |
| `--auth-header` | *(none)* | Static auth header applied to FHIR requests (e.g. `Bearer <token>`). Ignored when `FHIR_TOKEN_URL` configures client-credentials token auth. |

### Endpoints

- `GET /healthz` — `{ "status": "ok", "scripts": <count>, "invalidScripts": <count> }`. Locust's
  `test_start` listener polls this before spawning users.
- `GET /testscripts` — every loaded script: `{ "id", "name", "file", "valid", "error" }`. A script
  that failed to parse is still listed, with `valid: false` and its parse error, rather than
  silently dropped.
- `POST /run` — evaluate one TestScript against a target server:

  ```json
  {
    "testScriptId": "PatientSearch",
    "fhirBaseUrl": "https://example.fhir.azurehealthcareapis.com",
    "mode": "performance",
    "fhirVersion": "4.0",
    "options": { "runSetup": true, "runTeardown": true, "assertions": "full" }
  }
  ```

  Returns `{ "passed", "testScriptId", "durationMs", "failedAssertionCount", "summary", "operations": [...] }`,
  where each entry in `operations` carries `name`, `method`, `path`, `statusCode`, `durationMs`,
  `responseBytes`, and `passed` — one per FHIR operation the evaluator executed (setup, then each
  test case, then teardown, in order). This is what a locustfile fires as per-operation
  `events.request.fire()` samples.

  `options.assertions` accepts `"full"` (default) or `"none"` (test actions other than operations
  are skipped). `"status-only"` is part of the contract but is a Phase 3 feature not yet
  implemented — it is rejected with `400` rather than silently treated as `"full"`.

  Error responses are `{ "error": "..." }`: `400` for a bad request body, `404` for an unknown
  `testScriptId`, `422` when the identified script failed to parse, `500` on an unexpected
  evaluator failure.

### FHIR target caching

One `HttpClient` (and its CapabilityStatement, fetched at most once) is cached per distinct
`(fhirBaseUrl, fhirVersion)` pair for the life of the process, so repeated `/run` calls against the
same server reuse both rather than re-establishing them per request.

### Authentication

Two mutually exclusive auth modes, in order of precedence:

1. **Client-credentials token auth**, enabled by setting `FHIR_TOKEN_URL`. The runner acquires and
   caches a bearer token (refreshed 60s before its reported expiry, single-flighted so concurrent
   requests near expiry share one refresh) and applies it to every FHIR request. When this is set,
   `--auth-header` / `FHIR_AUTH_HEADER` are ignored.
2. **Static auth header** — `--auth-header`, or its `FHIR_AUTH_HEADER` environment equivalent when
   the flag is omitted — applied verbatim to every FHIR request, same parsing rules as `run`.

| Environment variable | Meaning |
|-----------------------|---------|
| `FHIR_TOKEN_URL` | OAuth2 token endpoint. Setting this enables client-credentials auth. |
| `FHIR_CLIENT_ID` | Client id for the token request. |
| `FHIR_CLIENT_SECRET` | Client secret for the token request. Never logged. |
| `FHIR_SCOPES` | Space-separated scopes (optional). |
| `FHIR_AUTH_HEADER` | Static auth header, used when `--auth-header` is not passed and `FHIR_TOKEN_URL` is unset. |

Built on the [Ignixa.TestScript](https://www.nuget.org/packages/Ignixa.TestScript) execution engine.

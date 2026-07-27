# Ignixa.ConformanceMatrix.Runner

`ignixa-matrix-runner` hosts folders of FHIR [TestScript](https://hl7.org/fhir/testscript.html)
suites as a local load-test runner: a Kestrel listener that parses every `*.json` under `--tests`
once at startup and evaluates one on demand per `POST /run`, instead of running a suite once and
exiting. It is designed to run as a sidecar — spawned once per load-generator instance (e.g. an
Azure Load Testing / Locust engine) and driven over `127.0.0.1`.

It is the load-generation companion to the [`ignixa-matrix`](../Ignixa.ConformanceMatrix.Cli/README.md)
conformance CLI and shares its TestScript engine, auth-header parsing rules, and exit-code contract.

## Installation

```bash
dotnet tool install -g Ignixa.ConformanceMatrix.Runner
```

Unlike `ignixa-matrix`, this tool requires the **ASP.NET Core runtime** (`Microsoft.AspNetCore.App`)
for its Kestrel host — present on any machine with the .NET SDK; runtime-only hosts need
`aspnetcore-runtime` rather than just `dotnet-runtime`.

## `serve`

```bash
ignixa-matrix-runner serve --tests ./src/Core/Ignixa.TestScript.Suites/testscripts --port 5599
```

| Option | Default | Meaning |
|--------|---------|---------|
| `--tests` | *(required)* | Folder containing TestScript `.json` files, scanned recursively. |
| `--port` | `5599` | TCP port to listen on. |
| `--host-ip` | `127.0.0.1` | IP address to bind the listener to. Non-loopback addresses are refused unless `--allow-remote-hosts` is passed. |
| `--allow-remote-hosts` | *(off)* | Opt in to binding a non-loopback `--host-ip`. See the security note below. |
| `--fhir-version` | *(none)* | Default FHIR version forwarded to every evaluation and the Accept header; a per-call `fhirVersion` in the `/run` request body overrides it. |
| `--auth-header` | *(none)* | Static auth header applied to FHIR requests (e.g. `Bearer <token>`). Ignored when `FHIR_TOKEN_URL` configures client-credentials token auth. |

### Security: the listener trusts every caller

`/run` has **no authentication of its own**, and the request body chooses the `fhirBaseUrl` the
runner sends its (authenticated) FHIR traffic to. Anyone who can reach the listener can therefore
drive requests carrying this runner's configured credentials at any server they name. The runner
refuses to start on a non-loopback `--host-ip` for that reason; `--allow-remote-hosts` overrides
the guard and is only appropriate on a network segment where every reachable caller is trusted —
e.g. an isolated load-agent subnet. Never expose the listener to an untrusted network.

### Endpoints

- `GET /healthz` — `{ "status": "ok", "scripts": <count>, "invalidScripts": <count> }`. Locust's
  `test_start` listener polls this before spawning users.
- `GET /testscripts` — every loaded script: `{ "id", "name", "file", "valid", "error" }`. A script
  that failed to parse is still listed, with `valid: false` and its parse error, rather than
  silently dropped. Script ids are matched case-insensitively by `/run`.
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
  are skipped; setup and teardown assertions deliberately keep running, so a broken precondition
  still fails loudly instead of skewing every measured operation). `"status-only"` is part of the
  contract but is a Phase 3 feature not yet implemented — it is rejected with `400` rather than
  silently treated as `"full"`.

  Error responses are `{ "error": "..." }`: `400` for a bad request body, `404` for an unknown
  `testScriptId`, `422` when the identified script failed to parse, `500` on an unexpected
  evaluator failure (the full exception is logged to the runner console; the response body carries
  only the exception type and message).

### FHIR target caching

One `HttpClient` is cached per distinct `(fhirBaseUrl, fhirVersion)` pair for the life of the
process, so repeated `/run` calls against the same server reuse pooled connections rather than
re-establishing them per request. Pooled connections are recycled every few minutes so a multi-hour
run follows DNS when the target scales or fails over.

The target's CapabilityStatement (which feeds `requiresCapability` gating) is fetched from
`/metadata` once per target on first use by a script that gates on it, and the raw JSON is reused
for subsequent runs — each run parses its own instance, so concurrent evaluations never share
mutable state. A failed fetch is not cached: gating fails open for that run only and the fetch is
retried on the next, so a runner started before the FHIR server finished warming up recovers on
its own.

### Authentication

Two mutually exclusive auth modes, in order of precedence:

1. **Client-credentials token auth**, enabled by setting `FHIR_TOKEN_URL`. The runner acquires and
   caches a bearer token (refreshed 60s before its reported expiry, single-flighted so concurrent
   requests near expiry share one refresh) and applies it to every FHIR request. When this is set,
   `--auth-header` / `FHIR_AUTH_HEADER` are ignored.
2. **Static auth header** — `--auth-header`, or its `FHIR_AUTH_HEADER` environment equivalent when
   the flag is omitted — applied verbatim to every FHIR request, same parsing rules as
   `ignixa-matrix run`.

Both modes fail at startup rather than on the first `/run` call: a malformed static header is
rejected the same way `run` rejects it, and setting `FHIR_TOKEN_URL` with a blank
`FHIR_CLIENT_ID`/`FHIR_CLIENT_SECRET` (or a non-absolute token URL) is a usage error. A
well-formed but *wrong* secret still surfaces on first use; that failure is logged to the runner
console.

| Environment variable | Meaning |
|-----------------------|---------|
| `FHIR_TOKEN_URL` | OAuth2 token endpoint. Setting this enables client-credentials auth. |
| `FHIR_CLIENT_ID` | Client id for the token request. |
| `FHIR_CLIENT_SECRET` | Client secret for the token request. Never logged. |
| `FHIR_SCOPES` | Space-separated scopes (optional). |
| `FHIR_AUTH_HEADER` | Static auth header, used when `--auth-header` is not passed and `FHIR_TOKEN_URL` is unset. |

Built on the [Ignixa.TestScript](https://www.nuget.org/packages/Ignixa.TestScript) execution engine.

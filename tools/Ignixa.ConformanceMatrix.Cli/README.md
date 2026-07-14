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
ignixa-matrix run --server https://your-fhir-server --tests ./conformance-tests \
  --impl my-server --out ./reports/my-server.json
```

To authenticate requests, supply an auth header value:

```bash
ignixa-matrix run --server https://your-fhir-server --tests ./conformance-tests \
  --impl my-server --out ./reports/my-server.json \
  --auth-header "Bearer <token>"
```

`merge` consumes this tool's native per-implementation report rather than FHIR `TestReport`, so pass
`--format json` when the run feeds the matrix:

```bash
ignixa-matrix run --server https://your-fhir-server --tests ./conformance-tests \
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

Built on the [Ignixa.TestScript](https://www.nuget.org/packages/Ignixa.TestScript) execution engine.

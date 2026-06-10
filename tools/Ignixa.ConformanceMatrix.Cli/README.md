# Ignixa.ConformanceMatrix.Cli

`ignixa-matrix` runs folders of FHIR [TestScript](https://hl7.org/fhir/testscript.html) conformance
suites against a live FHIR server and merges per-implementation reports into a publishable
conformance matrix.

## Installation

```bash
dotnet tool install -g Ignixa.ConformanceMatrix.Cli
```

## Usage

Run a conformance suite against a server, producing a per-implementation report:

```bash
ignixa-matrix run --server https://your-fhir-server --tests ./conformance-tests \
  --impl my-server --out ./reports/my-server.json
```

Merge per-implementation reports into the matrix output (`runs/` + `index.json`):

```bash
ignixa-matrix merge --results ./reports --out ./matrix \
  --commit "$(git rev-parse HEAD)" --branch main
```

## Behavior

- `run` exits non-zero when any test fails **or errors** — an engine or transport error is never
  reported as a pass. Crashed scripts are recorded as `error` cells rather than aborting the run,
  and parse warnings are printed per file.
- `--fhir-version` sets the `fhirVersion` parameter on the `Accept` header for version-gated suites.
- `merge` replaces an existing run with the same id instead of duplicating it, and refuses to
  proceed when a report file is unreadable.

Built on the [Ignixa.TestScript](https://www.nuget.org/packages/Ignixa.TestScript) execution engine.

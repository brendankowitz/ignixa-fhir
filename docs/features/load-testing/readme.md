# Feature: Load Testing

Run FHIR TestScript suites as load/stress workloads under Azure Load Testing, reusing the
`Ignixa.TestScript` engine and the `ignixa-matrix` conformance tooling instead of maintaining
parallel JMeter scripts.

## Investigations

| Investigation | Status | Date | Description |
|---------------|--------|------|-------------|
| [azure-load-testing-locust](investigations/azure-load-testing-locust.md) | In progress — Phase 1 implemented | 2026-07-22 | Plan: co-locate a TestScript runner (serve mode on the matrix CLI) on Azure Load Testing Locust engines; per-operation metrics via Locust request events |

## Goal

One set of TestScript resources drives conformance testing (`ignixa-matrix run`), smoke gates in
perf pipelines, and high-scale load generation through Azure Load Testing — with FHIR-operation
granular latency reported in the Azure Load Testing dashboard.

## Current State

Phase 1 of the investigation is implemented: `ignixa-matrix serve` hosts TestScripts as a local
load-test runner (an Azure Load Testing / Locust sidecar).

| Endpoint | Purpose |
|----------|---------|
| `GET /healthz` | Listener readiness plus loaded/invalid script counts |
| `GET /testscripts` | Lists every loaded script (id, name, file, parse validity) |
| `POST /run` | Executes one script by id against a request-supplied `fhirBaseUrl`, returning per-operation timings |

Also shipped: a self-contained linux-x64 publish profile for the runner binary, OAuth2
client-credentials token auth, and the generic Azure Load Testing artifact (locustfile, packaging
scripts, sample ALT config) under
[`tools/Ignixa.ConformanceMatrix.Cli/loadtest/`](../../../tools/Ignixa.ConformanceMatrix.Cli/loadtest/README.md).
See the [investigation](investigations/azure-load-testing-locust.md) for the full plan and phase
breakdown. For a live, end-to-end walkthrough (deploy a FHIR server, enable auth, run under ALT),
see the [Azure E2E runbook](azure-e2e-runbook.md).

Of the Phase 0 spikes, both are now answered against live Azure. **Spike A (binary exec on ALT
engines) passed** — the co-located sidecar topology works: the runner binary uploaded in the
artifact zip spawns once per Locust engine and serves `/run` over localhost, with per-FHIR-operation
stats in the ALT dashboard and (validated with security enabled) engine-side token acquisition from
a Key Vault secret. The Container Apps fallback is not needed. **Spike B (evaluator parallel safety)**
is confirmed safe by `TestScriptEvaluatorConcurrencyTests` in `Ignixa.TestScript.Tests`.

Phases 3-4 (performance-mode knobs, codegen fallback) remain.

## See Also

- [Architecture](architecture.md) — how Locust communicates with the runner and engine (diagrams)
- [Azure E2E runbook](azure-e2e-runbook.md) — deploy a secured FHIR server and run it under ALT
- [TestScript Feature](../testscript/readme.md) — the execution engine
- [Conformance Matrix Feature](../conformance-matrix/readme.md) — `ignixa-matrix` CLI this builds on
- [FhirFaker Feature](../fhir-faker/readme.md) — synthetic data generation for fixtures

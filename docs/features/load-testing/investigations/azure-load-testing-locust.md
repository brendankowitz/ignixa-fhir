# Running FHIR TestScripts in Azure Load Testing — Plan

> Status: proposed, researched 2026-07-22. **Phase 1 implemented 2026-07-22** (see Phase 1 below).
> Supersedes an earlier brainstorm ("FHIR TestScript Execution with Azure Load Testing and
> Locust"); migrated here from fhir-server working notes since the runner work lands in this repo.

## Summary

Azure Load Testing (ALT) still only supports two engines — Apache JMeter and Locust — with no
bring-your-own-engine model. There is still no Python FHIR TestScript execution engine worth
building on (the only open-source engine is Crucible's Ruby one). So the original conclusion
holds: Locust is the entry point, and the Ignixa-based .NET engine does the TestScript work.

Two research findings change the *shape* of the recommendation versus the earlier doc:

1. **The .NET runner can likely be co-located on the ALT engine instances themselves**, instead
   of deployed as a separate service. Locust scripts on ALT run as ordinary Python in a dedicated
   per-customer Linux container; arbitrary code execution on the engines (subprocess, filesystem)
   is possible by design. Uploading a self-contained linux-x64 .NET binary as a test artifact and
   spawning it once per engine at test start gives a runner *sidecar* that scales linearly with
   engine count — eliminating the two biggest cons of the "wrap the engine behind an API" option
   (runner-as-bottleneck, separate deployment/scaling). This needs a small spike to confirm
   (it is not an officially supported scenario).

2. **Per-operation metrics can flow into the ALT dashboard natively.** Locust's
   `events.request.fire()` API lets a script report arbitrary "requests" with their own name,
   latency, and pass/fail — the same mechanism used for non-HTTP protocols like gRPC. The runner
   returns per-FHIR-operation timings (measured at its own HttpClient layer), and the locustfile
   fires one event per operation. Result: ALT sampler statistics at FHIR-operation granularity,
   directly comparable to today's JMX persona output — not one opaque "TestScript passed" blob.
   Runner overhead is separable: `overhead = e2e script duration − Σ per-operation latencies`.

The Ignixa engine's architecture supports this well: parse and evaluate are separate phases
(`TestScriptParser` → `TestScriptDefinition` → `TestScriptEvaluator.ExecuteAsync`), so scripts
are parsed once and executed many times, and the pluggable `ITestRequestProvider` /
`HttpTestRequestProvider` seam is the natural place to capture per-operation timing without
touching the evaluator.

## Verified facts (July 2026)

### Azure Load Testing

- Supported engines: JMeter and Locust only; no generic custom-engine support.
  Locust support is GA and Microsoft now backs Locust's maintenance.
- Locust runs in **LocalRunner mode on each engine instance** (no master/worker across engines);
  ALT aggregates results across engines.
- Recommended max **~500 Locust users per engine instance**; engine is "healthy" if avg CPU/memory
  stay under 75%. Engine count = total users / 500 as a starting point.
- Artifacts: main locustfile, supporting `.py` files, `requirements.txt` (pip-installed before the
  run), Locust `.conf` file, CSV data files, and **zip artifacts: up to 5 zips × 50 MB each,
  max 1000 files per zip, 1 GB uncompressed, auto-extracted** — enough headroom to ship a
  self-contained .NET runner (~40–80 MB trimmed).
- Config/auth: environment variables and Key Vault–backed secrets (resource managed identity reads
  the vault; values surface to the script as env vars). Client certificates supported. VNet
  injection available for private endpoints. Multi-region load generation supported.
- CI/CD: YAML test config (`engineInstances`, `env` incl. `LOCUST_USERS`, `LOCUST_SPAWN_RATE`,
  `LOCUST_RUN_TIME`) with GitHub Actions / Azure Pipelines tasks.

### FHIR TestScript ecosystem

- No mature Python TestScript engine exists (unchanged from the earlier doc). Crucible's engine is
  Ruby; Touchstone is a hosted platform. Python FHIR libraries are building blocks only.
- Our engine: `Ignixa.TestScript` (library, in the ignixa-fhir repo) — three-phase
  parse/evaluate/report, produces FHIR `TestReport` or JUnit XML, `ITestRequestProvider`
  abstraction over HTTP, fixture providers (`InlineFixtureProvider`, `FhirFakes`), packaged
  TestScript suites (`Ignixa.TestScript.Suites`), and an xUnit adapter.
- **Two execution hosts already exist on top of the engine** (the "conformance matrix" family):
  - **`ignixa-matrix` CLI** (`tools/Ignixa.ConformanceMatrix.Cli` in ignixa-fhir, published as a
    dotnet global tool on NuGet). `run` executes folders of TestScripts against a live server
    (`--server`, `--auth-header`, `--fhir-version`), emitting a FHIR `TestReport` Bundle or a
    native JSON report; `merge` builds a publishable cross-implementation pass/fail matrix
    (fhir262-style). CI-grade exit codes distinguish "nothing ran" / "ran with failures" /
    "usage error" / "internal failure".
  - **Ignixa Lab backend** (ignixa-lab repo): a .NET 10 isolated-worker Azure Functions API
    exposing a TestScript conformance runner (`TestScriptRunner`, `HttpEvaluatorFactory`) with 87
    bundled suites across 9 categories, capability-aware gating via `CapabilityStatement`, and
    full HTTP request/response capture. Report JSON is interchangeable with ignixa-fhir's
    conformance dashboard artifact.
  Neither is load-shaped — the CLI is one-shot/sequential and the Lab API captures full HTTP
  traffic per step — but between them, suite loading, auth, report mapping, and the error
  taxonomy already exist. The load runner is an adaptation, not a green-field host.

## Recommended architecture

```text
Azure Load Testing (Locust engine instances, N×)
└── each engine instance:
    ├── locustfile.py  (per-user task: POST to localhost runner, then fire
    │                   events.request.fire() per FHIR operation result)
    └── TestScript LoadRunner  (self-contained .NET binary, spawned once at
        │                       test start, listening on 127.0.0.1)
        ├── Ignixa.TestScript engine (parse-once cache, per-run context)
        ├── instrumented ITestRequestProvider (per-op timing/status/bytes)
        └── token acquisition + cache (client credentials, SMART scopes)
                ↓ HTTPS
        FHIR service under test
```

Fallback topology (if the co-location spike fails): the same runner binary in a container on
Azure Container Apps behind internal ingress, scaled to ≥ engine count; locustfile points at it
instead of localhost. Everything else in the plan is identical — the runner contract and
locustfile don't care where the runner lives.

### Why this beats the earlier Option C (separate runner service)

| Earlier concern (Option C) | Co-located runner |
| --- | --- |
| Runner service may become the bottleneck | One runner per engine; capacity scales with engine count automatically |
| Additional deployment and scaling complexity | No extra infrastructure; runner ships as a test artifact |
| Results measure FHIR service + runner + extra network hop | localhost hop is ~zero; per-op latencies measured at runner's HttpClient |
| Correlating Locust metrics with TestScript results | Per-operation `events.request.fire()` puts FHIR-op stats in the ALT dashboard |

Option A (Python interpreter) and Option D (subprocess **per iteration**) from the earlier doc are
dropped: A duplicates the engine and will drift; D's per-iteration process startup is fatal — but
a *persistent* subprocess spawned once per test is exactly the co-located sidecar above.
Option B (codegen to native Locust) is deferred to Phase 4 and may never be needed.

## Runner API contract

`POST http://127.0.0.1:5599/run`

```json
{
  "testScriptId": "PatientSearch",
  "fhirBaseUrl": "https://example.fhir.azurehealthcareapis.com",
  "mode": "performance",          // "conformance" | "performance"
  "options": {
    "runSetup": true,             // fixture-aware setup control
    "runTeardown": true,
    "assertions": "status-only"   // "full" | "status-only" | "none"
  }
}
```

Response:

```json
{
  "passed": true,
  "testScriptId": "PatientSearch",
  "durationMs": 1234,
  "failedAssertionCount": 0,
  "summary": "Passed",
  "operations": [
    {
      "name": "Patient search",
      "method": "GET",
      "path": "Patient?name=…",
      "statusCode": 200,
      "durationMs": 42,
      "responseBytes": 18234,
      "passed": true
    }
  ]
}
```

Also: `GET /healthz` (Locust waits on this before starting users), `GET /testscripts`
(enumerate loaded scripts for validation/persona checks).

## Locustfile sketch

```python
import json, os, subprocess, time, requests
from locust import HttpUser, task, between, events

RUNNER_PORT = 5599
RUNNER_URL = f"http://127.0.0.1:{RUNNER_PORT}"

@events.test_start.add_listener
def start_runner(environment, **kwargs):
    # Runner binary uploaded as zip artifact; ALT auto-extracts next to the script.
    binary = os.path.join(os.path.dirname(__file__), "runner", "TestScriptLoadRunner")
    os.chmod(binary, 0o755)
    subprocess.Popen([binary, "--port", str(RUNNER_PORT)])
    for _ in range(60):                      # wait for /healthz
        try:
            if requests.get(f"{RUNNER_URL}/healthz", timeout=1).ok:
                return
        except requests.ConnectionError:
            time.sleep(1)
    raise RuntimeError("TestScript runner failed to start")

class FhirTestScriptUser(HttpUser):
    host = RUNNER_URL
    wait_time = between(0.1, 1.0)
    # Persona = weighted TestScript mix, from env var so one locustfile serves all personas
    scripts = json.loads(os.environ["TESTSCRIPT_MIX"])   # {"PatientSearch": 5, "ConditionalCreate": 1}

    @task
    def run_testscript(self):
        script_id = self.pick_weighted()
        started = time.perf_counter()
        resp = self.client.post("/run", json={
            "testScriptId": script_id,
            "fhirBaseUrl": os.environ["FHIR_BASE_URL"],
            "mode": os.environ.get("RUN_MODE", "performance"),
        }, name=f"TestScript/{script_id}")
        result = resp.json()

        # Surface each FHIR operation as its own ALT sampler entry
        for op in result.get("operations", []):
            events.request.fire(
                request_type=op["method"],
                name=f'{op["name"]} [{script_id}]',
                response_time=op["durationMs"],
                response_length=op.get("responseBytes", 0),
                exception=None if op["passed"] else Exception(f'{op["statusCode"]}'),
            )
        # e2e entry (includes runner overhead) — compare against sum of op latencies
        events.request.fire(
            request_type="SCRIPT",
            name=f"e2e [{script_id}]",
            response_time=(time.perf_counter() - started) * 1000,
            response_length=0,
            exception=None if result.get("passed") else Exception(result.get("summary", "failed")),
        )
```

(The `self.client.post` to `/run` itself also appears in stats as `TestScript/<id>` — keep or
suppress once the per-op events are proven out.)

## Where this code lives

Split by change-cadence — engine-coupled code with the engine (this repo), test-definition
assets with the consumer's perf pipeline. Nothing goes in the system under test (e.g.
microsoft/fhir-server keeps no load-test assets today either).

- **The load runner → this repo**, as a `serve` mode on the existing
  `tools/Ignixa.ConformanceMatrix.Cli` (or a sibling tool sharing its internals). The runner
  decorates `ITestRequestProvider`, caches parsed `TestScriptDefinition`s, and the Phase 3
  performance-mode knobs will need engine changes — atomic same-repo PRs matter while the engine
  is pre-1.0. The distribution channel already exists: `ignixa-matrix` is a published dotnet
  global tool on NuGet.
- **Load-test assets → the consumer's perf-pipeline repo** (for fhir-server, wherever the JMX
  files live today): locustfile, ALT YAML configs, persona definitions, golden-dataset param
  CSVs, artifact packaging, pipeline wiring, and target-specific perf TestScripts. That repo pins
  a specific runner version. Generic conformance TestScripts belong in
  `Ignixa.TestScript.Suites` here instead. (The generic locustfile template could graduate into
  this repo once stable, shipped alongside the tool.)
- **Governance:** official pipelines should consume the runner from a controlled feed at a
  pinned version (or build from a pinned commit) rather than pulling from a personal GitHub
  repo, until the Ignixa projects move to an organizational home.

## Test data strategy

The existing perf pipeline provisions each run a fresh environment: new resource group → restore
a copy of a golden SQL database → deploy the OSS FHIR server → configure → smoke test → load
test → share results. Keep that model — it solves the hard problem (a realistically sized,
consistent dataset) and makes in-run mutation harmless, since every run starts from the same
restored copy.

TestScripts then split into two data patterns:

- **Read personas reference golden data, not fixtures.** Search/read scripts should hit known
  data in the golden dataset via TestScript variables (known patient IDs, names, identifiers
  guaranteed to match), the same way the JMX scripts use CSV parameter files today. The Ignixa
  engine already models parametrization (`ParametrizeDefinition`), so the runner can load ID/param
  pools (CSV shipped in the artifact zip, or queried from the server at startup) and bind them
  into script variables per iteration. Setup/teardown for these scripts should be empty or
  skipped (performance-mode knob) — no per-iteration fixture creation.
- **Write personas create their own data and don't clean up.** Create/update/transaction scripts
  mutate the restored copy; teardown per iteration is optional (deletes add load without adding
  realism). Cross-run consistency comes from the restore, not from teardown. If in-run growth
  must be bounded for long soak tests, that's a persona design choice, not an engine feature.

Two pipeline wins fall out of reusing TestScripts here:

- **The smoke-test step and the load-test step can share the same scripts.** The post-deploy
  smoke gate is already built: `ignixa-matrix run --server <env> --tests <suites> --out
  smoke-report.json` with CI-grade exit codes (0 = pass, 1 = ran with failures, 2/3 = broken
  invocation). Then the identical TestScripts run in *performance* mode under Locust for load.
  One artifact, two pipeline steps, no drift between what's smoke-tested and what's load-tested.
- **A preflight "data readiness" TestScript** can assert the golden dataset restored correctly
  (expected resource counts, known IDs resolvable, search params reindexed) before load starts —
  turning today's implicit "data copy worked" assumption into an explicit gate.

Pipeline step mapping: "Upload JMX Files" → "Upload Locust artifact zip (locustfile + runner
binary + TestScripts + param CSVs)"; everything else in the pipeline stays as-is.

## Phases

### Phase 0 — Spikes (days)

- **Spike A: binary exec on ALT engines.** Upload a hello-world self-contained linux-x64 binary
  (publish with `--self-contained -p:PublishSingleFile=true -p:InvariantGlobalization=true` to
  avoid ICU/libssl surprises) in a zip artifact; locustfile chmods and runs it, logs output.
  Pass/fail decides co-located vs Container Apps topology. Also record engine OS/arch/pyversion.
- **Spike B: evaluator parallel safety.** In one process, run N concurrent
  `TestScriptEvaluator.ExecuteAsync` against a local FHIR server; look for shared mutable state
  (fixture providers, `TestScriptContext`, static caches) and measure per-execution cost.
  This answers the first two open questions from the earlier doc empirically.

  **Done (2026-07-22): safe.** `TestScriptEvaluatorConcurrencyTests` in `Ignixa.TestScript.Tests`
  runs 32 concurrent executions over one shared provider with unique-id correlation (plus a
  shared-single-evaluator variant) — all runs pass with zero cross-run bleed, stable across
  repeated runs. Why: the evaluator holds no mutable instance state (fresh recorder + immutable
  `TestScriptContext` per call), fixture providers are stateless, and the FHIRPath static caches
  use `ConcurrentDictionary`. Per-execution cost at the in-memory floor is ~1–2 ms; real
  serve-mode latency is dominated by HTTP round-trips. One flag for Phase 3:
  `ResourceJsonNode.ToElement()` memoizes per instance without synchronization — safe today only
  because contexts deep-clone every body; a fixture-reuse knob that shares `ResourceJsonNode`
  instances across concurrent executions would race on that cache and must keep cloning (or add
  synchronization).

### Phase 1 — Load-serve mode on the conformance-matrix tooling (~1 week)

**Implemented.** `ignixa-matrix serve` (`tools/Ignixa.ConformanceMatrix.Cli/Commands/ServeCommand.cs`
+ the `Serving/` folder), the `linux-x64-sidecar` publish profile
(`Properties/PublishProfiles/linux-x64-sidecar.pubxml`), and the generic Locust artifact under
`tools/Ignixa.ConformanceMatrix.Cli/loadtest/` (locustfile, packaging scripts, sample ALT config —
see the feature readme's Current State section) — the locustfile template graduating into this
repo is exactly what the "Where this code lives" section below anticipated. One design delta from
the plan below: per-operation timing comes from the evaluator's own report (`ActionResult` with
`Kind=Operation` carries `Duration` + `HttpExchange`) rather than a timing decorator around
`ITestRequestProvider` — either way, no evaluator changes were needed. The `options` knobs
(`runSetup`/`runTeardown`/`assertions`) work today via definition rewriting; `assertions:
"status-only"` is rejected with 400 until Phase 3, and `mode` is validated and recorded but doesn't
change behavior yet.

Extend `ignixa-matrix` with a `serve` command (or a sibling `Ignixa.TestScript.LoadRunner`
project sharing its internals) rather than building a new host. The CLI already has suite
loading with parse-error taxonomy, auth-header support, and TestReport/native report mapping;
what's new is the load-shaped execution path:

- Kestrel listener on localhost exposing `/run`, `/healthz`, `/testscripts`.
- Parse-once cache of `TestScriptDefinition` per script; per-run evaluator/context (the CLI
  currently parses and runs one-shot).
- Timing decorator around `HttpTestRequestProvider` (implements `ITestRequestProvider`) capturing
  method/path/status/duration/bytes per operation — no evaluator changes needed. Full HTTP
  capture (as in the Lab API) stays **off** in performance mode.
- Token acquisition (client credentials, SMART scopes per persona) with caching and refresh —
  the CLI's static `--auth-header` isn't enough for long load runs; config via env vars so ALT
  Key Vault secrets flow straight in.
- Publish profile for self-contained linux-x64 single-file; CI packaging step producing the
  ALT artifact zip (runner + testscripts + locustfile + requirements.txt + locust.conf).

The Ignixa Lab Functions API is prior art for a remote runner but not the fallback of choice for
load (Functions cold starts, per-step full capture); the Container Apps fallback should host the
same `serve` binary.

### Phase 2 — ALT integration (1 week)

- ALT test definitions (YAML) per persona: `TESTSCRIPT_MIX`, `FHIR_BASE_URL`, `RUN_MODE`, users,
  spawn rate, engines; Key Vault secret refs for client credentials.
- Run at low/moderate load against a test environment; validate ALT dashboard shows per-operation
  sampler stats; compare a persona head-to-head against its existing JMX equivalent (latency
  distributions should match at the same op mix — this is the correctness gate for the bridge).
- Wire into the existing perf pipeline alongside (not replacing) JMX tests: same resource-group +
  golden-SQL-restore + deploy steps; swap the "Upload JMX Files" step for the artifact zip upload;
  optionally switch the smoke-test step to the same TestScripts in conformance mode.
- Add the preflight data-readiness TestScript as a gate between smoke test and load test.

### Phase 3 — Performance mode (1–2 weeks, overlaps Phase 2)

- Implement the `options` knobs: assertion level (full / status-only / none), setup/teardown
  control, fixture reuse across iterations (setup once per user or per test-run instead of per
  iteration where the script allows).
- Measure runner ceiling: max TestScript executions/sec per engine at <75% engine CPU; derive
  users-per-engine guidance for our scripts (the generic ALT guidance is 500/engine; ours will be
  lower since each "user request" fans out into a whole script).
- Decide Locust user counts / engine counts for the target load levels; document overhead
  (e2e − Σ ops) so dashboards can subtract runner cost.

### Phase 4 — Only if needed: codegen for extreme scale

If Phase 3 shows the runner can't reach a required load level even with engines scaled out,
generate native Locust `FastHttpUser` code from TestScript operations (earlier doc's Option B)
for pure traffic generation, keeping full-fidelity runs at lower concurrency. Decision point,
not committed work — the per-engine sidecar may make this unnecessary.

## Earlier open questions — current answers

- *Engine parallel-safe?* → **Yes, verified empirically** (Spike B result above): no mutable
  evaluator state, immutable per-run context, thread-safe static caches.
- *Optional setup/teardown, fixture reuse?* → Phase 3 `options` knobs; engine already separates
  fixture provision (`IFixtureProvider`) from evaluation.
- *Per-operation timing?* → Yes, via `ITestRequestProvider` decorator; surfaced through
  `events.request.fire()` into ALT sampler statistics.
- *Compact failure summaries?* → `summary` + `failedAssertionCount` in the response; full
  `TestReport`/JUnit XML optionally written to blob storage for post-run analysis (ALT does not
  collect arbitrary output files).
- *Auth handling?* → Runner-controlled client-credentials with token cache; secrets via ALT
  Key Vault integration → env vars. Locust never touches FHIR tokens.
- *Measure runner overhead separately?* → Yes: e2e event minus per-op latencies.
- *Persona grouping?* → `TESTSCRIPT_MIX` weighted map per ALT test definition, mirroring the JMX
  persona structure.

## Risks

- **Binary exec on ALT engines is undocumented/unsupported** and could break with a service
  update. Mitigation: topology-agnostic runner + Container Apps fallback kept working in CI.
- **Runner cost per iteration** (full script per "request") means fewer users per engine than
  plain HTTP tests; budget engines accordingly and lean on performance mode.
- **ALT quota** (~5,000 test runs per resource lifetime reported) — watch if runs are automated
  at high frequency; use separate ALT resources per purpose if needed.
- **Ignixa engine is pre-1.0 / actively evolving** (currently being integrated into fhir-server);
  pin versions in the runner and track breaking changes.

## Sources

- [Quickstart: Create a load test with Locust — Microsoft Learn](https://learn.microsoft.com/en-us/azure/app-testing/load-testing/quickstart-create-run-load-test-with-locust)
- [Configure high-scale load tests — Microsoft Learn](https://learn.microsoft.com/en-us/azure/app-testing/load-testing/how-to-high-scale-load)
- [Use secrets & environment variables — Microsoft Learn](https://learn.microsoft.com/en-us/azure/app-testing/load-testing/how-to-parameterize-load-tests)
- [Load test authenticated endpoints — Microsoft Learn](https://learn.microsoft.com/en-us/azure/app-testing/load-testing/how-to-test-secured-endpoints)
- [Run Locust-based tests in Azure Load Testing — Microsoft Community Hub](https://techcommunity.microsoft.com/blog/appsonazureblog/run-locust-based-tests-in-azure-load-testing/4389373)
- [Multi-region load tests and Locust support announcement — Microsoft Community Hub](https://techcommunity.microsoft.com/blog/appsonazureblog/announcing-multi-region-load-tests-and-support-for-locust-framework-in-azure-loa/4145411)
- [Locust + Azure = High Performance — Lars Holmberg (Locust maintainer), May 2026](https://medium.com/locust/locust-azure-high-performance-c1de593bb08e)
- [Testing non-HTTP systems (events.request.fire pattern) — Locust docs](https://docs.locust.io/en/stable/testing-other-systems.html)
- [Extracting sensitive information from Azure Load Testing — NetSPI (demonstrates arbitrary code exec on engines)](https://www.netspi.com/blog/technical-blog/cloud-pentesting/extracting-sensitive-information-azure-load-testing/)
- [Crucible Ruby TestScript engine (only OSS TestScript engine found)](https://github.com/fhir-crucible/testscript-engine)
- [ignixa-fhir repo — Ignixa.TestScript engine + ignixa-matrix CLI source](https://github.com/brendankowitz/ignixa-fhir)
- [ignixa-lab repo — Azure Functions TestScript conformance runner API](https://github.com/brendankowitz/ignixa-lab)

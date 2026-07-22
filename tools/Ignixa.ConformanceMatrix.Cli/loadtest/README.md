# Azure Load Testing (ALT) — FHIR TestScript Artifact

Load testing framework for FHIR TestScripts on Azure Load Testing using Locust and the Ignixa TestScript engine.

## Artifact Layout

An ALT test uses four uploads from this folder:

| Upload | ALT role |
|--------|----------|
| `locustfile.py` | Test plan (`testPlan`) |
| `requirements.txt` | Configuration file (pip-installed before the run) |
| `locust.conf` | Configuration file |
| `alt-testscript-artifact.zip` | Zip artifact, auto-extracted next to the locustfile |

The zip (built by the packaging scripts) contains only the runner and the suites —
the loose files above are deliberately *not* inside it, since ALT extracts the zip
next to the uploaded test plan and a stale copy would clobber it:

```
alt-testscript-artifact.zip
├── runner/
│   └── ignixa-matrix            (Self-contained Linux x64 binary)
└── testscripts/                 (FHIR TestScript suites by category)
    ├── CRUD/
    ├── Search/
    ├── Operations/
    └── ...
```

After extraction the locustfile finds `./runner/ignixa-matrix` and `./testscripts`
relative to its own location.

## Files in This Directory

| File | Purpose |
|------|---------|
| **locustfile.py** | Locust load-test harness that spawns the runner, distributes weighted TestScript selections, and reports per-operation metrics to ALT. |
| **requirements.txt** | Python package dependencies (only `requests`; Locust is provided by ALT). |
| **locust.conf** | Locust configuration template with commented defaults. ALT overrides via `LOCUST_USERS`, `LOCUST_SPAWN_RATE`, `LOCUST_RUN_TIME` env vars. |
| **alt-load-test.sample.yaml** | Template ALT test configuration (YAML) for your perf-pipeline repo. Shows environment setup, secrets, failure criteria, and multi-region config. |
| **package-alt-artifact.ps1** | PowerShell script to package the artifact zip (Windows). Publishes the runner binary, bundles TestScripts, and validates the 50 MB size limit. |
| **package-alt-artifact.sh** | Bash equivalent of the PowerShell script (macOS/Linux). |
| **README.md** | This file. |

## How the Locustfile Starts the Runner

1. At test start (before any users spawn), Locust calls the `@events.test_start` listener.
2. The script resolves the runner binary path relative to itself: `runner/ignixa-matrix`.
3. The binary is made executable with `chmod(0o755)`.
4. A subprocess is spawned with:
   ```bash
   ./runner/ignixa-matrix serve --tests ./testscripts --port 5599
   ```
5. The script polls `GET http://127.0.0.1:5599/healthz` (up to 60 seconds) until the runner is ready.
6. If the runner process exits early, the tail of `runner.log` (its redirected output) is reported and the test fails immediately.
7. At test stop, the runner process is terminated gracefully (SIGTERM → SIGKILL if needed).

## Packaging the Artifact

### PowerShell (Windows)

```powershell
cd C:\repos\ignixa-fhir\tools\Ignixa.ConformanceMatrix.Cli\loadtest
.\package-alt-artifact.ps1
# Creates: C:\repos\ignixa-fhir\artifacts\alt-testscript-artifact.zip
```

Or specify a custom output path:

```powershell
.\package-alt-artifact.ps1 -OutputPath "./my-artifact.zip"
```

### Bash (macOS/Linux)

```bash
cd tools/Ignixa.ConformanceMatrix.Cli/loadtest
chmod +x package-alt-artifact.sh
./package-alt-artifact.sh
# Creates: ./artifacts/alt-testscript-artifact.zip
```

Or specify a custom path:

```bash
./package-alt-artifact.sh --output ./my-artifact.zip
```

## Environment Variables

Required by the locustfile. Set these in the ALT test configuration (YAML) or as environment variables:

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| **TESTSCRIPT_MIX** | Yes | — | JSON object mapping script IDs to integer weights. Example: `{"PatientSearch": 80, "ConditionalCreate": 20}`. Used to distribute load across TestScripts. |
| **FHIR_BASE_URL** | Yes | — | FHIR service base URL (no trailing slash). Example: `https://example.fhir.azurehealthcareapis.com`. |
| **RUN_MODE** | No | `performance` | Execution mode label: `"performance"` or `"conformance"`. Recorded per run; behavior is controlled by `TESTSCRIPT_OPTIONS`. |
| **RUNNER_PORT** | No | `5599` | Port on which the co-located runner listens. Usually left at default. |
| **TESTSCRIPT_OPTIONS** | No | — | JSON object merged into the `/run` request body as `"options"`. Example: `{"runSetup": true, "runTeardown": false, "assertions": "none"}`. `assertions` accepts `"full"` or `"none"` (`"status-only"` arrives with Phase 3). |

### Authentication / Token Acquisition

The runner supports client-credentials flow. Set these as secrets in ALT (stored in Key Vault):

| Variable | Required | Description |
|----------|----------|-------------|
| **FHIR_CLIENT_ID** | Conditional | OAuth2/SMART client ID. |
| **FHIR_CLIENT_SECRET** | Conditional | OAuth2/SMART client secret. |
| **FHIR_TOKEN_URL** | Conditional | OAuth2/SMART token endpoint. |
| **FHIR_SCOPES** | No | Space-separated SMART scopes (e.g., `"system/Patient.r system/Patient.c"`). |
| **FHIR_AUTH_HEADER** | No | Pre-computed Bearer token if client-credentials is not available. Mutually exclusive with the above. |

## Sample ALT Configuration

See `alt-load-test.sample.yaml` for a complete example:

```yaml
testId: fhir-testscript-example
displayName: "FHIR TestScript Load Test"
testType: Locust
testPlan: locustfile.py
configurationFiles:
  - requirements.txt
  - locust.conf
zipArtifacts:
  - alt-testscript-artifact.zip
engineInstances: 2
env:
  - name: TESTSCRIPT_MIX
    value: '{"PatientSearch": 80, "ConditionalCreate": 20}'
  - name: FHIR_BASE_URL
    value: "https://example.fhir.azurehealthcareapis.com"
  - name: RUN_MODE
    value: "performance"
failureCriteria:
  - avg(response_time_ms) > 500
  - percentage(error) > 5
```

## Persona-Specific Assets

Per the [investigation document](#reference), persona definitions, data CSVs, and ALT YAML configurations are maintained in **your perf-pipeline repository**, not here. This repo ships:

- **Reusable**: The runner binary, generic locustfile template, and conformance TestScripts (in `Ignixa.TestScript.Suites`).
- **Persona-specific**: TESTSCRIPT_MIX values, parameter CSVs, load level tuning, ALT YAML configs per persona/scenario.

Example perf-pipeline repo structure:

```
your-perf-repo/
├── load-tests/
│   ├── alt/
│   │   ├── personas.yaml           # ALT configs per persona (based on alt-load-test.sample.yaml)
│   │   ├── package-artifact.sh     # Wrapper to fetch runner + build zip
│   │   └── params/
│   │       ├── patient-ids.csv
│   │       └── demographics.csv
│   └── jmeter/
│       └── ...
├── .github/workflows/
│   └── load-test.yml               # CI pipeline calling package-artifact.sh + ALT run
└── README.md
```

## Azure Load Testing Limits

- **Zip size**: Max 50 MB per artifact; up to 5 zips per test.
- **Files per zip**: Max 1000 files.
- **Uncompressed**: Max 1 GB per zip.
- **Engines**: Recommended ~500 Locust users per engine instance.

If your artifact exceeds 50 MB:
- Split testscripts across multiple zips (e.g., `testscripts-patient.zip`, `testscripts-observation.zip`).
- Increase compression in the publish step (use `-p:EnableCompressionInSingleFile=true`).
- Omit unused TestScript suites.

The packaging scripts check the final size and warn if it exceeds the limit.

## Testing Locally

To validate the setup before uploading to ALT:

```bash
cd tools/Ignixa.ConformanceMatrix.Cli/loadtest

# Build the artifact
./package-alt-artifact.sh

# Extract and inspect (the zip is written to <repo-root>/artifacts)
mkdir /tmp/test-artifact
unzip -d /tmp/test-artifact ../../../artifacts/alt-testscript-artifact.zip

# Test the runner binary (if on Linux or WSL)
chmod +x /tmp/test-artifact/runner/ignixa-matrix
/tmp/test-artifact/runner/ignixa-matrix serve --tests /tmp/test-artifact/testscripts --port 5600 &
sleep 2
curl http://127.0.0.1:5600/healthz
curl http://127.0.0.1:5600/testscripts
```

Or run Locust locally (requires Python 3.7+):

```bash
export TESTSCRIPT_MIX='{"PatientSearch": 100}'
export FHIR_BASE_URL="http://localhost:8080"
export RUN_MODE="conformance"

pip install -r requirements.txt
locust -f locustfile.py --host http://127.0.0.1:5599 --users 1 --spawn-rate 1 --run-time 1m
```

## Reference

See the full investigation and architecture:
[Azure Load Testing — FHIR TestScript Runner Plan](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/features/load-testing/investigations/azure-load-testing-locust.md)

### Key Sections

- **Verified facts**: ALT capabilities, Locust support, artifact limits.
- **Recommended architecture**: Co-located sidecar runner vs. separate service.
- **Runner API contract**: POST `/run` request/response schema.
- **Phases 1–4**: Roadmap from runner development through performance optimization.
- **Test data strategy**: Read personas (golden data + params) vs. write personas (fresh data).

## Troubleshooting

### Runner fails to start

**Error**: "TestScript runner failed to start after 60 seconds"

**Possible causes**:
- Binary not included in the zip (check extraction).
- Port 5599 is already in use (ALT engine or another process).
- Binary lacks execute permissions (should be set by `chmod(0o755)`).
- Missing .NET runtime dependencies (use `PublishSingleFile` with `InvariantGlobalization`).

**Check**:
```bash
# Extract and inspect
unzip alt-testscript-artifact.zip -d /tmp/alt
ls -la /tmp/alt/runner/ignixa-matrix
file /tmp/alt/runner/ignixa-matrix
/tmp/alt/runner/ignixa-matrix serve --tests /tmp/alt/testscripts --port 5600
```

### High runner overhead

If `e2e` latency is much higher than the sum of per-operation latencies, the runner may be spending time on:
- Setup/teardown (especially with `runSetup: true` for read personas).
- Fixture resolution.
- Token acquisition (network round-trip).

**Mitigation**:
- Set `TESTSCRIPT_OPTIONS: '{"runSetup": false, "runTeardown": false, "assertions": "none"}'`.
- Reuse fixtures across iterations (Phase 3 feature).
- Pre-warm the token cache.

### TestScript not found

**Error**: "TestScript 'PatientSearch' not found"

**Check**:
1. The script ID is spelled correctly in `TESTSCRIPT_MIX` — `GET /testscripts` on a locally started runner lists every loaded id.
2. The TestScript file exists under `testscripts/` (e.g., `testscripts/Search/PatientSearch.json`).
3. The file is valid JSON the parser accepts (`/testscripts` also reports per-file parse errors).

## Contributing

Improvements to the locustfile, packaging scripts, or ALT configuration are welcome. Please:

1. Test locally before pushing.
2. Update this README if you add new environment variables or features.
3. Keep the template (`alt-load-test.sample.yaml`) aligned with real usage.
4. Pin the runner version in your perf-pipeline repo's packaging script.

## License

See the main ignixa-fhir repository.

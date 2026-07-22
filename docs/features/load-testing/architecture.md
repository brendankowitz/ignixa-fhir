# How Locust communicates with the Ignixa engine

On each Azure Load Testing (ALT) engine instance, the locustfile spawns `ignixa-matrix serve` as
a **co-located sidecar** and drives it over localhost. The runner — not Locust — carries the FHIR
traffic, holds the tokens, and returns per-operation timings that the locustfile surfaces as ALT
sampler entries.

Two hops with very different cost: Locust → runner is a `127.0.0.1` POST (effectively free); the
measured latency is the runner → FHIR HTTPS hop, timed at the runner's own HttpClient.

## Topology — one sidecar per engine

```mermaid
flowchart TB
  subgraph host["ALT Locust engine instance (× N)"]
    direction TB
    L["locustfile.py<br/>FhirTestScriptUser"]
    subgraph box["ignixa-matrix serve · 127.0.0.1:5599"]
      direction TB
      R["Kestrel host<br/>/healthz · /testscripts · /run"]
      E["Ignixa.TestScript engine<br/>parse-once · evaluate-many"]
      R --- E
    end
    L -->|"spawn @ test_start"| R
    L -->|"POST /run"| R
    R -.->|"RunResponse: per-op timings"| L
  end
  KV[("Key Vault<br/>fhir-client-secret")]
  TOK["Token endpoint<br/>/connect/token"]
  FHIR["FHIR server<br/>under test"]
  ALT["ALT sampler<br/>dashboard"]

  KV -.->|"secret → env var (ALT identity)"| box
  E ==>|"HTTPS + Bearer"| FHIR
  E -.->|"token (cached)"| TOK
  L -.->|"events.request.fire()"| ALT

  linkStyle 0 stroke:#10b981,stroke-width:2px
  linkStyle 1 stroke:#10b981,stroke-width:2.5px
  linkStyle 2 stroke:#a78bfa,stroke-width:2px,stroke-dasharray:5 4
  linkStyle 3 stroke:#f5a623,stroke-width:2px,stroke-dasharray:5 4
  linkStyle 4 stroke:#38bdf8,stroke-width:3px
  linkStyle 5 stroke:#f5a623,stroke-width:2px,stroke-dasharray:5 4
  linkStyle 6 stroke:#a78bfa,stroke-width:2px,stroke-dasharray:5 4
```

Channels: **green** = localhost (Locust ⇄ runner); **blue** = FHIR HTTPS traffic; **amber** =
auth/secret; **violet** = metrics.

## Protocol — start to finish

```mermaid
sequenceDiagram
  autonumber
  participant L as locustfile.py
  participant R as runner :5599
  participant E as Ignixa engine
  participant T as Token endpoint
  participant F as FHIR server

  rect rgb(235,240,247)
  note over L,R: test_start — once per engine
  L->>R: spawn ignixa-matrix serve --tests … --port 5599
  R->>R: parse every TestScript once (registry cache)
  L->>R: GET /healthz (poll until ready)
  R-->>L: 200 {scripts, invalidScripts}
  end

  rect rgb(250,244,230)
  note over L,F: preflight auth — direct probe, bypasses the runner
  L->>T: POST client_credentials (Key Vault secret)
  T-->>L: access_token
  L->>F: GET /Patient (Bearer)
  F-->>L: 200 → "preflight auth OK"
  end

  rect rgb(233,246,238)
  note over L,F: each task iteration — per virtual user
  L->>R: POST /run {testScriptId, fhirBaseUrl, mode, options}
  R->>E: ExecuteAsync(parsed definition)
  loop each FHIR operation in the script
    opt token missing or near expiry
      E->>T: client_credentials (token handler)
      T-->>E: access_token — cached, 60s buffer
    end
    E->>F: HTTPS request + Bearer
    F-->>E: response — status · body · bytes
  end
  E-->>R: TestScriptReport — per-op duration + exchange
  R-->>L: RunResponse {passed, operations[], durationMs}
  L->>L: events.request.fire() per op + e2e event → ALT
  end

  rect rgb(250,235,238)
  note over L,R: test_stop
  L->>R: terminate subprocess
  end
```

## Why it's shaped this way

- **Parse once, run many.** Every TestScript is parsed at `serve` startup into a registry cache;
  `/run` evaluates an already-parsed `TestScriptDefinition`, so parsing never taxes the hot path.
- **Locust never holds a FHIR token.** The runner's `ClientCredentialsTokenHandler` acquires and
  caches the token (60s expiry buffer, single-flight refresh). Locust only touches auth in the
  `test_start` preflight probe, which fails the run fast if the target's auth is misconfigured.
- **Per-operation metrics.** The engine records each operation's duration and HTTP exchange in the
  `TestScriptReport`; `RunResponseMapper` turns that into `operations[]`, and the locustfile fires
  one `events.request.fire()` per operation plus an e2e event — so ALT charts FHIR-operation
  latency directly rather than one opaque "script passed" blob.

Source: `tools/Ignixa.ConformanceMatrix.Cli/loadtest/locustfile.py`,
`tools/Ignixa.ConformanceMatrix.Cli/Serving/RunnerHost.cs`,
`Serving/ClientCredentialsTokenHandler.cs`. See the [runbook](azure-e2e-runbook.md) for the live
end-to-end setup.

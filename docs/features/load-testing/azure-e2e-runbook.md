# Runbook: authenticated Azure Load Testing E2E

How to stand up a real FHIR server, enable authentication, and run the `ignixa-matrix serve`
load runner against it under Azure Load Testing (ALT). This is the end-to-end validation of the
[load-testing plan](investigations/azure-load-testing-locust.md) — Spike A (binary exec on ALT
engines) plus Phase 2 (ALT integration) with security enabled.

Validated 2026-07-22 on the `fhir-server-sandbox` subscription: a 5-user / 3-minute run against
a secured OSS FHIR server returned **0 failures across ~2,000 samples**, with the runner
acquiring tokens on the engine from a Key Vault-backed secret.

## Topology confirmed

The co-located sidecar works: the runner binary uploaded in the artifact zip is spawned once per
Locust engine and serves `/run` over localhost. No separate runner service (Container Apps
fallback) was needed. Per-FHIR-operation sampler stats appear in ALT alongside the `e2e [script]`
and `TestScript/<id>` entries.

## Prerequisites

- Subscription **Contributor** (role propagation can lag minutes after a grant).
- `Microsoft.LoadTestService` registered: `az provider register -n Microsoft.LoadTestService`
  (a few minutes; needs the permission above).
- Azure CLI `load` extension: `az extension add --name load`.

## 1. Deploy the FHIR server

Use the OSS template
(`microsoft/fhir-server/samples/templates/default-azuredeploy-docker.json`).

- **SQL, not Cosmos**, when the subscription has 0 VM quota for Cosmos/App-Service SKUs:
  `solutionType=FhirServerSqlServer sqlDatabaseComputeTier=Standard sqlSchemaAutomaticUpdatesEnabled=auto`.
- **Pick a region with App Service plan (S2) quota.** A sandbox often reports
  `InternalSubscriptionIsOverQuotaForSku (Current Limit (Total VMs): 0)` in the obvious regions.
  Probe with `az deployment group validate` across regions before committing; `westus2` worked
  when `eastus`/`eastus2`/`southcentralus` did not.

Verify anonymously first (security is off by default): `POST /Observation` should return `201`.

## 2. Enable authentication (the three App Service Linux fixes)

The corp tenant's app-management policy blocked creating an Entra client-credentials app (no
secrets, no short-lived certs), so this runbook uses the FHIR server's **in-process
DevelopmentIdentityProvider** — the server issues its own tokens at `/connect/token`. This is a
sandbox convenience, **not production auth** (production points `Authority`/`Audience` at real
Entra and only *validates* tokens, which avoids fixes 1 and 2 entirely).

App settings:

```
FhirServer__Security__Enabled=true
FhirServer__Security__Authorization__Enabled=true
FhirServer__Security__Authentication__Authority=https://<app>.azurewebsites.net
FhirServer__Security__Authentication__Audience=fhir-api
DevelopmentIdentityProvider__Enabled=true
DevelopmentIdentityProvider__ClientApplications__0__Id=<client-id>
DevelopmentIdentityProvider__ClientApplications__0__Roles__0=globalAdmin
```

Then the three fixes without which the site never becomes healthy or rejects valid tokens:

1. **Cert store on the CIFS `$HOME` mount** → container never passes the warmup probe.
   OpenIddict's `AddDevelopmentSigningCertificate()` writes to
   `$HOME/.dotnet/corefx/cryptography/x509stores/my`; on App Service `$HOME=/home` is a CIFS
   share owned by a different uid than the image's `nonroot` user →
   `CryptographicException: The owner of '...x509stores/my' is not the current user`.
   **Fix — override the startup command so HOME is a writable local path:**
   ```
   az webapp config set -g <rg> -n <app> \
     --startup-file "sh -c 'export HOME=/tmp && cd /app && exec dotnet Microsoft.Health.Fhir.Web.dll'"
   ```
   `HOME` cannot be set as an app setting (reserved — the API returns 400).
   `WEBSITES_ENABLE_APP_SERVICE_STORAGE=false` alone did **not** fix it.

2. **HTTP issuer behind TLS termination** → a valid token still 401s. App Service terminates TLS
   and forwards to the container as HTTP:8080, so OpenIddict stamps issuer
   `http://<app>.azurewebsites.net/` while validation expects `https://`. Decode a token and
   compare `iss` to the configured Authority to confirm. **Fix:**
   `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (rebuilds the scheme from `X-Forwarded-Proto`).

3. **Slow cold start** — `WEBSITES_CONTAINER_START_TIME_LIMIT=1800`. A plain `az webapp restart`
   sometimes reused the old container; a `stop` then `start` forces a clean recreate.

Confirm the chain: anonymous `GET /Patient` → `401`; a client-credentials token from
`/connect/token` (**client_secret == client_id**, `scope=fhir-api`) → authenticated
`GET /Patient` → `200`; the token's `iss` is `https://`.

**Diagnosis tip:** `az webapp log download`, unzip, read `LogFiles/*_docker.log` for the real
exception. The platform's "container didn't respond on 8080" message is a symptom, not the cause.

## 3. ALT resource, identity, Key Vault

```
az load create -n <alt> -g <rg> -l <region>
az load update -n <alt> -g <rg> --identity-type SystemAssigned   # note the principalId
az keyvault create -n <kv> -g <rg> -l <region> --enable-rbac-authorization true
az role assignment create --assignee <alt-principalId> --role "Key Vault Secrets User" --scope <kv-id>
az keyvault secret set --vault-name <kv> --name fhir-client-secret --value <client-secret>
```

The runner reads the secret as the `FHIR_CLIENT_SECRET` env var; ALT resolves the Key Vault
reference at run time using the resource's system-assigned identity.

## 4. Package the artifact

`tools/Ignixa.ConformanceMatrix.Cli/loadtest/package-alt-artifact.ps1` (or `.sh`) publishes the
linux-x64 runner and zips it with the suites. The zip sits right at ALT's **50 MB/zip** limit
with all 86 bundled suites — trim suites if you add more. Upload `locustfile.py`,
`requirements.txt`, `locust.conf` as the test plan + configuration files (they are deliberately
**not** in the zip — ALT auto-extracts the zip next to the locustfile and a stale copy would
clobber it).

## 5. Configure and run

Use `alt-load-test.sample.yaml` as the template. Authenticated `env` + `secrets`:

```yaml
env:
  - {name: TESTSCRIPT_MIX,  value: '{"create": 2, "read": 3, "metadata": 1}'}
  - {name: FHIR_BASE_URL,   value: "https://<app>.azurewebsites.net"}
  - {name: RUN_MODE,        value: "performance"}
  - {name: FHIR_TOKEN_URL,  value: "https://<app>.azurewebsites.net/connect/token"}
  - {name: FHIR_CLIENT_ID,  value: "<client-id>"}
  - {name: FHIR_SCOPES,     value: "fhir-api"}
secrets:
  - {name: FHIR_CLIENT_SECRET, value: "https://<kv>.vault.azure.net/secrets/fhir-client-secret/<version>"}
```

The locustfile runs a **preflight auth gate** at `test_start`: it acquires one token and does one
authenticated read, aborting the whole run in ~2s with the token's `iss`/`aud` if auth is
misconfigured — so a broken target fails clearly instead of producing a wall of 401s mid-run. On
a healthy target it logs `preflight auth OK`.

ALT config gotchas that reject the request outright (the run silently never starts):
- `LOCUST_RUN_TIME` must be an **integer** (`"180"`, not `"180s"`).
- test-run `--display-name` ≤ **50 chars**; test `description` ≤ **100 chars**.
- omit `keyVaultReferenceIdentity` for a system-assigned identity (a literal `SystemAssigned`
  is rejected — it wants a resource id).

```
az load test create -r <alt> -g <rg> --test-id <id> --load-test-config-file config.yaml
az load test-run create -r <alt> -g <rg> --test-id <id> --test-run-id <run-id> --display-name "<=50 chars"
```

## 6. Read results

```
az load test-run download-files -r <alt> -g <rg> --test-run-id <run-id> --path out --result --log --force
```

`csv/engine1_results.csv` has one row per sampler entry; `logs/engine1_worker.log` shows the
preflight line and Locust lifecycle. Per-FHIR-operation events carry responseCode `0` (Locust
custom events set no HTTP code) — filter on `success` for pass/fail. The `/run` POSTs carry `200`.

**Observed:** token auth adds negligible per-operation latency (read p50 ~840ms authenticated vs
~865ms anonymous) — the runner caches the token after first acquisition, so only the preflight
pays the token round-trip.

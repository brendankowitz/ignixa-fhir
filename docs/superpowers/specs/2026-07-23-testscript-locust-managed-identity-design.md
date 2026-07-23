# TestScript Locust Managed Identity Design

## Context

Ignixa compiles FHIR `TestScript` resources into self-contained Locust artifacts for Azure Load Testing. The generated runtime currently accepts an arbitrary `IGNIXA_AUTH_HEADER`. That mechanism can carry a static bearer token, but it requires external token management and cannot refresh expiring credentials during a load test.

Azure Load Testing can assign a system-assigned or user-assigned managed identity to each load engine. Microsoft documents managed identity authentication for secured endpoints and requires the test script to acquire an access token from the engine environment. The target API must trust Microsoft Entra tokens issued for the configured audience.

The existing Ignixa Azure E2E target uses a custom OpenIddict `/connect/token` client-credentials flow. It does not currently trust Microsoft Entra tokens, so it cannot validate this design until its authentication configuration changes.

## Goals

- Authenticate generated Locust requests with an Azure Load Testing engine managed identity.
- Support system-assigned and user-assigned managed identities.
- Refresh tokens safely during long-running tests.
- Apply authentication consistently to capability discovery, fixtures, and TestScript operations.
- Preserve unauthenticated local and Azure test scenarios.
- Keep credentials and authentication configuration out of the TestScript IR and generated diagnostics.
- Fail explicitly without leaking tokens, scopes, or identity identifiers.

## Non-Goals

- Service-principal client secrets or custom OAuth client-credentials flows.
- Static authorization-header injection.
- Interactive or developer credentials such as Azure CLI authentication.
- Automatic configuration of Microsoft Entra applications, API permissions, or Azure role assignments.
- Validation against the existing OpenIddict Azure E2E target.

## Configuration

The generated runtime reads authentication settings only from environment variables.

| Variable | Required | Meaning |
| --- | --- | --- |
| `IGNIXA_AUTH_MODE` | No | `none` by default; `managed-identity` enables managed identity authentication. |
| `IGNIXA_AUTH_SCOPE` | In managed identity mode | Microsoft Entra token scope for the target API, normally `<application-id-uri>/.default`. |
| `IGNIXA_MANAGED_IDENTITY_CLIENT_ID` | No | Client ID of a user-assigned managed identity. Omit it to use the system-assigned identity selected for the load engine. |

Values are supplied through Azure Load Testing environment variables, not embedded into generated artifacts. Scope and client ID are configuration rather than secrets, but the runtime still omits their values from errors and metrics.

`IGNIXA_AUTH_HEADER` is removed. Its presence does not provide a fallback authentication path.

## Architecture

Authentication remains inside `ignixa_testscript_runtime.py`, the shared runtime copied into every generated artifact.

The runtime introduces a small token-provider boundary:

- A no-auth provider returns no authorization header.
- A managed-identity provider owns one `azure.identity.ManagedIdentityCredential` per Locust worker process.
- HTTP execution asks the selected provider for request authentication rather than reading a global header.

The provider is created during worker startup and shared by all virtual users in that worker. It is not serialized into the IR and does not alter TestScript semantics.

`azure-identity` is pinned in generated `requirements.txt`. The runtime constructs `ManagedIdentityCredential` with the optional user-assigned client ID. It does not use `DefaultAzureCredential`, so local developer credentials cannot silently replace the managed identity.

## Token Lifecycle

Worker startup performs these steps:

1. Parse and validate authentication configuration.
2. Create the selected provider.
3. In managed identity mode, acquire a token before load generation begins.
4. Perform authenticated capability discovery.

The managed-identity provider caches the returned `AccessToken` per worker. A lock prevents concurrent virtual users from starting duplicate refreshes. The provider refreshes the token when it has five minutes or less remaining, with a second cache check after acquiring the lock.

Every outbound FHIR request obtains authentication through the provider, including:

- capability discovery;
- ordinary TestScript operations;
- fixture autocreate requests; and
- fixture autodelete requests.

An explicit TestScript `Authorization` header does not override managed identity authentication. Allowing that override would restore arbitrary static-token behavior under another name. Other TestScript headers retain their current precedence rules.

Worker shutdown closes the Azure credential. Unauthenticated mode has no token lifecycle.

## HTTP Failure Semantics

Token acquisition failure during startup aborts that worker before it generates load. A refresh failure during execution fails the current semantic operation through the existing `TESTSCRIPT_OPERATION` failure path; no HTTP request is emitted.

An HTTP 401 response:

- remains the result of the current TestScript operation;
- invalidates the provider cache;
- is not retried automatically; and
- causes the next operation to acquire a new token.

Not replaying the request preserves the TestScript operation count, avoids duplicating mutations, and keeps Azure Load Testing metrics honest.

Authentication errors contain a stable message and the exception type only. They do not include the token, scope value, identity client ID, credential response body, or authorization header.

Unknown authentication modes, a missing scope, or an empty configured value fail startup with a configuration error. The runtime does not silently fall back to unauthenticated requests.

## Security Properties

- No client secret exists in the generated artifact or load-test configuration.
- Tokens remain in worker memory and are never written to metrics, diagnostics, or logs.
- Only `ManagedIdentityCredential` is used, preventing fallback to workstation or pipeline identities.
- The target controls authorization for the managed identity through Microsoft Entra application roles, scopes, or its own claims policy.
- User-assigned identity selection is explicit through the configured client ID and Azure Load Testing engine identity assignment.

## Testing

Python contract tests cover:

- unauthenticated default behavior;
- rejected unknown modes and empty values;
- required scope validation;
- system-assigned credential construction;
- user-assigned credential construction;
- startup token acquisition;
- cached token reuse;
- refresh within the five-minute window;
- refresh locking and duplicate-acquisition prevention;
- capability, fixture, and operation header application;
- prevention of TestScript authorization-header overrides;
- 401 invalidation without request replay;
- startup and in-run failure reporting without sensitive values; and
- credential disposal.

Tests use fake credentials, tokens, clocks, and HTTP clients. They do not require Azure or a live metadata endpoint.

The generated-artifact loopback smoke test remains unauthenticated and proves that `none` mode requires no Azure environment. Python 3.9 CI installs the generated requirements and runs the full runtime suite.

## Azure Load Testing Configuration

Deployment documentation will require:

1. Assign a system-assigned or user-assigned managed identity to the Azure Load Testing resource.
2. Grant that identity access to the target API.
3. Select the identity for the load-test engine, including `referenceIdentities` when configuring the test as code.
4. Set `IGNIXA_AUTH_MODE=managed-identity`.
5. Set `IGNIXA_AUTH_SCOPE` to the target API's `/.default` scope.
6. Set `IGNIXA_MANAGED_IDENTITY_CLIENT_ID` only for a user-assigned identity.

Azure Load Testing currently disables multi-region load distribution when managed identities are used for authentication. Documentation must state this constraint.

## Deferred Azure Validation

The current `ignixa-alt-e2e-ecow` endpoint cannot accept managed identity tokens because it uses a custom OpenIddict issuer. Its Azure smoke test is deferred until the endpoint trusts Microsoft Entra tokens for an application ID URI and authorizes the selected load-test identity.

Deferral is an environment limitation, not a fallback requirement. The compiler and runtime will not add service-principal or static-header authentication to accommodate that target.

## Acceptance Criteria

- Generated tests run unchanged against unauthenticated endpoints.
- Managed identity mode fails before load starts when configuration or initial token acquisition is invalid.
- A valid system-assigned or user-assigned identity supplies bearer authentication to every runtime HTTP path.
- Tokens are reused and refreshed without a concurrent refresh stampede.
- A 401 is recorded once, invalidates the cached token, and is never replayed automatically.
- No auth token, scope value, or identity identifier appears in generated files, diagnostics, metrics, or runtime errors.
- Python 3.9 runtime and generated-artifact tests pass with the pinned dependency set.
- User documentation explains Azure configuration, target prerequisites, and the multi-region limitation.

## References

- [Authenticate Azure Load Testing endpoints with managed identity](https://learn.microsoft.com/en-us/azure/app-testing/load-testing/how-to-test-secured-endpoints#authenticate-with-a-managed-identity)
- [Use managed identities for Azure Load Testing](https://learn.microsoft.com/en-us/azure/app-testing/load-testing/how-to-use-a-managed-identity)
- [Parameterize Azure load tests](https://learn.microsoft.com/en-us/azure/app-testing/load-testing/how-to-parameterize-load-tests)

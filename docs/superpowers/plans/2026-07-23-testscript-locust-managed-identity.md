# TestScript Locust Managed Identity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace generated Locust static-header authentication with optional Azure managed identity authentication that caches and refreshes tokens safely across every FHIR HTTP path.

**Architecture:** The shared generated Python runtime owns one authentication provider per Locust worker. `none` mode is the default; `managed-identity` mode creates only `azure.identity.ManagedIdentityCredential`, acquires a token before load starts, refreshes it five minutes before expiry under a lock, and applies it after TestScript headers so scripts cannot override the bearer token. Authentication failures use existing semantic operation reporting, while HTTP 401 responses invalidate the cache without replaying requests.

**Tech Stack:** Python 3.9, Locust 2.33.2, `azure-identity` 1.25.3, `requests` 2.32.3, xUnit, Shouldly, Python `unittest`

---

## File Map

| File | Responsibility |
| --- | --- |
| `src/Core/Ignixa.TestScript.Locust/Python/ignixa_testscript_runtime.py` | Parse auth configuration, own the worker credential/token cache, authenticate all HTTP requests, invalidate on 401, and close the credential. |
| `src/Core/Ignixa.TestScript.Locust/Python/requirements.txt` | Pin `azure-identity==1.25.3` for generated artifacts and CI. |
| `test/Ignixa.TestScript.Locust.Tests/Python/test_runtime_authentication.py` | Unit-test configuration, credential selection, token caching/refresh, concurrency, sanitization, and disposal. |
| `test/Ignixa.TestScript.Locust.Tests/Python/test_runtime_assertions.py` | Update engine-startup contracts from static headers to managed identity. |
| `test/Ignixa.TestScript.Locust.Tests/Python/test_runtime_operations.py` | Replace static-header request tests with managed-identity coverage for operations, polling, and fixtures. |
| `test/Ignixa.TestScript.Locust.Tests/Python/test_generated_artifact.py` | Preserve the unauthenticated local smoke and verify the generated Azure Identity pin. |
| `test/Ignixa.TestScript.Locust.Tests/Artifacts/LocustArtifactWriterTests.cs` | Lock the exact generated requirements content. |
| `docs/site/docs/core-sdk/testscript.md` | Document Locust compilation, runtime boundaries, and managed identity configuration. |
| `docs/features/testscript/investigations/azure-load-testing.md` | Record implementation evidence and the deferred Azure smoke constraint. |

### Task 1: Build the managed identity provider

**Files:**
- Create: `test/Ignixa.TestScript.Locust.Tests/Python/test_runtime_authentication.py`
- Modify: `src/Core/Ignixa.TestScript.Locust/Python/ignixa_testscript_runtime.py:1-27,368-427`

- [ ] **Step 1: Write failing configuration and token lifecycle tests**

Create `test_runtime_authentication.py` with fake credentials and deterministic clocks:

```python
import os
import threading
import unittest
from unittest.mock import patch

import fakes


def authorization(token):
    return "Bearer" + " " + token


class FakeAccessToken:
    def __init__(self, token, expires_on):
        self.token = token
        self.expires_on = expires_on


class FakeCredential:
    def __init__(self, tokens=None, error=None):
        self.tokens = list(tokens or [])
        self.error = error
        self.scopes = []
        self.closed = False

    def get_token(self, scope):
        self.scopes.append(scope)
        if self.error is not None:
            raise self.error
        return self.tokens.pop(0)

    def close(self):
        self.closed = True


class ManagedIdentityConfigurationTests(unittest.TestCase):
    def setUp(self):
        self.runtime = fakes.load_runtime()
        self.env = patch.dict(os.environ, {}, clear=True)
        self.env.start()

    def tearDown(self):
        self.env.stop()

    def test_auth_mode_defaults_to_none(self):
        provider = self.runtime._create_auth_provider()
        self.assertEqual("_NoAuthProvider", type(provider).__name__)

    def test_managed_identity_requires_non_empty_scope(self):
        os.environ["IGNIXA_AUTH_MODE"] = "managed-identity"
        with self.assertRaisesRegex(RuntimeError, "IGNIXA_AUTH_SCOPE"):
            self.runtime._create_auth_provider()

    def test_unknown_and_empty_modes_fail(self):
        for mode in ("", "client-credentials"):
            os.environ["IGNIXA_AUTH_MODE"] = mode
            with self.subTest(mode=mode):
                with self.assertRaises(RuntimeError):
                    self.runtime._create_auth_provider()

    def test_legacy_static_header_is_rejected_without_echoing_value(self):
        os.environ["IGNIXA_AUTH_HEADER"] = "Authorization: secret-value"
        with self.assertRaises(RuntimeError) as caught:
            self.runtime._create_auth_provider()
        self.assertNotIn("secret-value", str(caught.exception))

    def test_user_assigned_client_id_is_passed_to_credential_factory(self):
        os.environ.update(
            {
                "IGNIXA_AUTH_MODE": "managed-identity",
                "IGNIXA_AUTH_SCOPE": "api://fhir/.default",
                "IGNIXA_MANAGED_IDENTITY_CLIENT_ID": "identity-client-id",
            }
        )
        calls = []
        credential = FakeCredential([FakeAccessToken("token", 3600)])
        self.runtime._create_managed_identity_credential = (
            lambda client_id: calls.append(client_id) or credential
        )
        provider = self.runtime._create_auth_provider()
        self.assertEqual(["identity-client-id"], calls)
        provider.close()

    def test_system_assigned_identity_omits_client_id(self):
        os.environ.update(
            {
                "IGNIXA_AUTH_MODE": "managed-identity",
                "IGNIXA_AUTH_SCOPE": "api://fhir/.default",
            }
        )
        calls = []
        credential = FakeCredential([FakeAccessToken("token", 3600)])
        self.runtime._create_managed_identity_credential = (
            lambda client_id: calls.append(client_id) or credential
        )
        provider = self.runtime._create_auth_provider()
        self.assertEqual([None], calls)
        provider.close()

    def test_credential_creation_error_is_sanitized(self):
        os.environ.update(
            {
                "IGNIXA_AUTH_MODE": "managed-identity",
                "IGNIXA_AUTH_SCOPE": "api://fhir/.default",
                "IGNIXA_MANAGED_IDENTITY_CLIENT_ID": "identity-client-id",
            }
        )

        def fail(_client_id):
            raise ValueError("identity-client-id")

        self.runtime._create_managed_identity_credential = fail
        with self.assertRaises(RuntimeError) as caught:
            self.runtime._create_auth_provider()
        self.assertIn("ValueError", str(caught.exception))
        self.assertNotIn("identity-client-id", str(caught.exception))


class ManagedIdentityTokenTests(unittest.TestCase):
    def setUp(self):
        self.runtime = fakes.load_runtime()
        self.now = 1000

    def _provider(self, credential):
        return self.runtime._ManagedIdentityAuthProvider(
            "api://fhir/.default",
            credential,
            lambda: self.now,
        )

    def test_cached_token_is_reused_until_refresh_window(self):
        credential = FakeCredential([FakeAccessToken("first", 2000)])
        provider = self._provider(credential)
        self.assertEqual(authorization("first"), provider.authorization_value())
        self.assertEqual(authorization("first"), provider.authorization_value())
        self.assertEqual(["api://fhir/.default"], credential.scopes)

    def test_token_refreshes_with_five_minutes_remaining(self):
        credential = FakeCredential(
            [FakeAccessToken("first", 1400), FakeAccessToken("second", 3000)]
        )
        provider = self._provider(credential)
        self.assertEqual(authorization("first"), provider.authorization_value())
        self.now = 1100
        self.assertEqual(authorization("second"), provider.authorization_value())
        self.assertEqual(2, len(credential.scopes))

    def test_concurrent_refresh_acquires_one_token(self):
        credential = FakeCredential([FakeAccessToken("shared", 3000)])
        provider = self._provider(credential)
        barrier = threading.Barrier(3)
        values = []

        def acquire():
            barrier.wait()
            values.append(provider.authorization_value())

        threads = [threading.Thread(target=acquire) for _ in range(2)]
        for thread in threads:
            thread.start()
        barrier.wait()
        for thread in threads:
            thread.join(timeout=5)

        self.assertEqual(
            [authorization("shared"), authorization("shared")],
            sorted(values),
        )
        self.assertEqual(1, len(credential.scopes))

    def test_acquisition_error_is_sanitized(self):
        credential = FakeCredential(error=ValueError("scope=secret token=secret"))
        provider = self._provider(credential)
        with self.assertRaises(RuntimeError) as caught:
            provider.authorization_value()
        message = str(caught.exception)
        self.assertIn("ValueError", message)
        self.assertNotIn("scope=secret", message)
        self.assertNotIn("token=secret", message)

    def test_invalidate_discards_cached_token_and_close_disposes_credential(self):
        credential = FakeCredential(
            [FakeAccessToken("first", 3000), FakeAccessToken("second", 4000)]
        )
        provider = self._provider(credential)
        self.assertEqual(authorization("first"), provider.authorization_value())
        provider.invalidate()
        self.assertEqual(authorization("second"), provider.authorization_value())
        provider.close()
        self.assertTrue(credential.closed)


if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 2: Run the new test module to verify RED**

Run:

```powershell
py -3.9 -m unittest discover -s test\Ignixa.TestScript.Locust.Tests\Python -p "test_runtime_authentication.py" -v
```

Expected: FAIL because `_create_auth_provider` and `_ManagedIdentityAuthProvider` do not exist.

- [ ] **Step 3: Implement configuration parsing and provider classes**

Add imports for `threading` and `time`, replace `_parse_auth_header`, and define:

```python
_AUTH_REFRESH_WINDOW_SECONDS = 300
_AUTHORIZATION_SCHEME = "Bearer"


class _NoAuthProvider:
    def initialize(self):
        return None

    def authorization_value(self):
        return None

    def invalidate(self):
        return None

    def close(self):
        return None


class _ManagedIdentityAuthProvider:
    def __init__(self, scope, credential, clock=time.time):
        self._scope = scope
        self._credential = credential
        self._clock = clock
        self._token = None
        self._lock = threading.Lock()

    def initialize(self):
        self.authorization_value()

    def authorization_value(self):
        token = self._token
        if self._is_fresh(token):
            return f"{_AUTHORIZATION_SCHEME} {token.token}"
        with self._lock:
            token = self._token
            if not self._is_fresh(token):
                try:
                    token = self._credential.get_token(self._scope)
                except Exception as exc:
                    raise RuntimeError(
                        "Managed identity token acquisition failed "
                        f"({type(exc).__name__})"
                    ) from exc
                self._token = token
            return f"{_AUTHORIZATION_SCHEME} {token.token}"

    def _is_fresh(self, token):
        return (
            token is not None
            and token.expires_on - self._clock() > _AUTH_REFRESH_WINDOW_SECONDS
        )

    def invalidate(self):
        with self._lock:
            self._token = None

    def close(self):
        self._credential.close()


_AUTH_PROVIDER = _NoAuthProvider()


def _non_empty_env(name, required=False):
    value = os.environ.get(name)
    if value is None:
        if required:
            raise RuntimeError(f"{name} is required for managed identity authentication")
        return None
    value = value.strip()
    if not value:
        raise RuntimeError(f"{name} must not be empty")
    return value


def _create_managed_identity_credential(client_id):
    from azure.identity import ManagedIdentityCredential

    if client_id is None:
        return ManagedIdentityCredential()
    return ManagedIdentityCredential(client_id=client_id)


def _create_auth_provider():
    if "IGNIXA_AUTH_HEADER" in os.environ:
        raise RuntimeError(
            "IGNIXA_AUTH_HEADER is unsupported; configure managed identity authentication"
        )
    mode = _non_empty_env("IGNIXA_AUTH_MODE") or "none"
    if mode == "none":
        return _NoAuthProvider()
    if mode != "managed-identity":
        raise RuntimeError(f"Unsupported IGNIXA_AUTH_MODE '{mode}'")
    scope = _non_empty_env("IGNIXA_AUTH_SCOPE", required=True)
    client_id = _non_empty_env("IGNIXA_MANAGED_IDENTITY_CLIENT_ID")
    try:
        credential = _create_managed_identity_credential(client_id)
    except Exception as exc:
        raise RuntimeError(
            "Managed identity credential creation failed "
            f"({type(exc).__name__})"
        ) from exc
    return _ManagedIdentityAuthProvider(scope, credential)
```

- [ ] **Step 4: Run provider tests to verify GREEN**

Run the Step 2 command.

Expected: all `test_runtime_authentication.py` tests PASS on Python 3.9.

- [ ] **Step 5: Commit the provider foundation after explicit user approval and controller review**

```powershell
git add src\Core\Ignixa.TestScript.Locust\Python\ignixa_testscript_runtime.py test\Ignixa.TestScript.Locust.Tests\Python\test_runtime_authentication.py
git commit -m "Add Locust managed identity token provider" `
  -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" `
  -m "Copilot-Session: 51cdcc9f-e427-404b-add7-32cadd089520"
```

### Task 2: Wire authentication into engine lifecycle

**Files:**
- Modify: `test/Ignixa.TestScript.Locust.Tests/Python/test_runtime_authentication.py`
- Modify: `test/Ignixa.TestScript.Locust.Tests/Python/test_runtime_assertions.py:673-783`
- Modify: `src/Core/Ignixa.TestScript.Locust/Python/ignixa_testscript_runtime.py:132-157,187-255`

- [ ] **Step 1: Add failing startup, metadata, and shutdown tests**

Add tests that patch `_create_managed_identity_credential`, then assert:

```python
class ManagedIdentityEngineTests(unittest.TestCase):
    def setUp(self):
        self.runtime = fakes.load_runtime()
        self.env = patch.dict(
            os.environ,
            {
                "IGNIXA_AUTH_MODE": "managed-identity",
                "IGNIXA_AUTH_SCOPE": "api://fhir/.default",
            },
            clear=True,
        )
        self.env.start()
        self.environment = fakes.FakeEnvironment(host="http://fhir.test")

    def tearDown(self):
        self.runtime.clear_engine()
        self.env.stop()

    def test_startup_acquires_token_before_authenticated_metadata_fetch(self):
        credential = FakeCredential([FakeAccessToken("access-token", 9999999999)])
        self.runtime._create_managed_identity_credential = lambda client_id: credential
        session = fakes.FakeRequestsSession(
            response=fakes.FakeMetadataResponse(json_data={"resourceType": "CapabilityStatement"})
        )
        with patch("requests.Session", return_value=session):
            self.runtime.initialize_engine(
                {"schemaVersion": "1.0", "metadata": {"source": "test"}, "tests": []},
                self.environment,
            )
        self.assertEqual(["api://fhir/.default"], credential.scopes)
        self.assertEqual(
            authorization("access-token"),
            session.get_calls[0]["headers"]["Authorization"],
        )

    def test_token_failure_fails_closed_before_metadata_request(self):
        credential = FakeCredential(error=ValueError("sensitive-response"))
        self.runtime._create_managed_identity_credential = lambda client_id: credential
        session = fakes.FakeRequestsSession(
            response=fakes.FakeMetadataResponse(json_data={})
        )
        with patch("requests.Session", return_value=session):
            with self.assertRaises(RuntimeError) as caught:
                self.runtime.initialize_engine(
                    {"schemaVersion": "1.0", "metadata": {"source": "test"}, "tests": []},
                    self.environment,
                )
        self.assertEqual([], session.get_calls)
        self.assertFalse(self.runtime._SUITE_ALLOWED)
        self.assertTrue(credential.closed)
        self.assertIn("ValueError", str(caught.exception))
        self.assertNotIn("sensitive-response", str(caught.exception))

    def test_clear_engine_closes_credential_and_restores_no_auth(self):
        credential = FakeCredential([FakeAccessToken("token", 9999999999)])
        self.runtime._create_managed_identity_credential = lambda client_id: credential
        session = fakes.FakeRequestsSession(
            response=fakes.FakeMetadataResponse(json_data={})
        )
        with patch("requests.Session", return_value=session):
            self.runtime.initialize_engine(
                {"schemaVersion": "1.0", "metadata": {"source": "test"}, "tests": []},
                self.environment,
            )
        self.runtime.clear_engine()
        self.assertTrue(credential.closed)
        self.assertIsNone(self.runtime._AUTH_PROVIDER.authorization_value())
```

In `test_runtime_assertions.py`, remove the `IGNIXA_AUTH_HEADER` setup and the two static-header tests. Keep target resolution and fail-open metadata I/O tests unchanged.

- [ ] **Step 2: Run lifecycle-focused tests to verify RED**

```powershell
py -3.9 -m unittest discover -s test\Ignixa.TestScript.Locust.Tests\Python -p "test_runtime_authentication.py" -v
py -3.9 -m unittest discover -s test\Ignixa.TestScript.Locust.Tests\Python -p "test_runtime_assertions.py" -v
```

Expected: managed identity engine tests FAIL because startup does not create, initialize, use, or close the provider.

- [ ] **Step 3: Implement fail-closed engine ownership**

Change `_fetch_capability(host, auth)` to `_fetch_capability(host)` and build headers through the current provider:

```python
def _apply_authentication(headers):
    value = _AUTH_PROVIDER.authorization_value()
    if value is not None:
        headers["Authorization"] = value
    return headers
```

At the start of `initialize_engine`, close any prior provider and reset to `_NoAuthProvider`. After host resolution:

```python
global _AUTH_PROVIDER

provider = _create_auth_provider()
try:
    provider.initialize()
except RuntimeError:
    provider.close()
    _SUITE_ALLOWED = False
    _TEST_DECISIONS = {
        test["id"]: False
        for test in document.get("tests", [])
        if test.get("id") is not None
    }
    raise
_AUTH_PROVIDER = provider
capability = _fetch_capability(host)
```

`_fetch_capability` applies authentication before `session.get`. Its existing `RequestException` and `ValueError` handling remains fail-open; authentication exceptions continue to propagate.

`clear_engine` closes the current provider in `try/finally`, installs `_NoAuthProvider`, and resets capability decisions and ordinals even when credential disposal fails.

- [ ] **Step 4: Run lifecycle-focused tests to verify GREEN**

Run both Step 2 commands.

Expected: all authentication and assertion runtime tests PASS.

- [ ] **Step 5: Commit engine lifecycle wiring after explicit user approval and controller review**

```powershell
git add src\Core\Ignixa.TestScript.Locust\Python\ignixa_testscript_runtime.py test\Ignixa.TestScript.Locust.Tests\Python\test_runtime_authentication.py test\Ignixa.TestScript.Locust.Tests\Python\test_runtime_assertions.py
git commit -m "Initialize Locust managed identity per worker" `
  -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" `
  -m "Copilot-Session: 51cdcc9f-e427-404b-add7-32cadd089520"
```

### Task 3: Authenticate operations, polling, and fixtures

**Files:**
- Modify: `test/Ignixa.TestScript.Locust.Tests/Python/test_runtime_operations.py:491-604`
- Modify: `test/Ignixa.TestScript.Locust.Tests/Python/test_runtime_authentication.py`
- Modify: `src/Core/Ignixa.TestScript.Locust/Python/ignixa_testscript_runtime.py:392-427,479-484,546-610,778-962`

- [ ] **Step 1: Replace static-header tests with failing managed-identity request tests**

Delete `AuthHeaderParsingTests` and replace `AuthHeaderApplicationTests` with tests that install a deterministic provider directly:

```python
class AuthHeaderApplicationTests(RuntimeOperationsTestCase):
    def setUp(self):
        super().setUp()
        credential = type(
            "Credential",
            (),
            {
                "get_token": lambda self, scope: type(
                    "Token", (), {"token": "managed-token", "expires_on": 9999999999}
                )(),
                "close": lambda self: None,
            },
        )()
        self.runtime._AUTH_PROVIDER = self.runtime._ManagedIdentityAuthProvider(
            "api://fhir/.default", credential
        )

    def test_managed_identity_authenticates_ordinary_operation(self):
        document = _document()
        user, client = make_user()
        context = new_context(self.runtime, document)
        client.queue_response(fakes.FakeResponse(status_code=200, json_data={}))
        action = _operation(
            "op-1",
            type="read",
            resource="Patient",
            request_id="op-1",
            response_id="op-1",
        )
        run_operation(self, self.runtime, document, user, context, action)
        self.assertEqual(
            "Bearer" + " " + "managed-token",
            client.calls[0]["headers"]["Authorization"],
        )

    def test_script_authorization_header_cannot_override_managed_identity(self):
        document = _document()
        user, client = make_user()
        context = new_context(self.runtime, document)
        client.queue_response(fakes.FakeResponse(status_code=200, json_data={}))
        action = _operation(
            "op-1",
            type="read",
            resource="Patient",
            request_id="op-1",
            response_id="op-1",
            headers=[_header("authorization", "script-token")],
        )
        run_operation(self, self.runtime, document, user, context, action)
        self.assertEqual(
            "Bearer" + " " + "managed-token",
            client.calls[0]["headers"]["Authorization"],
        )

    def test_401_invalidates_token_without_replaying_request(self):
        document = _document()
        user, client = make_user()
        context = new_context(self.runtime, document)
        client.queue_response(fakes.FakeResponse(status_code=401, json_data={}))
        action = _operation(
            "op-1",
            type="read",
            resource="Patient",
            request_id="op-1",
            response_id="op-1",
        )
        with patch.object(self.runtime._AUTH_PROVIDER, "invalidate") as invalidate:
            run_operation(self, self.runtime, document, user, context, action)
        self.assertEqual(1, len(client.calls))
        invalidate.assert_called_once_with()
```

Retain and adapt the existing fixture autocreate/autodelete tests to set `_AUTH_PROVIDER` instead of patching `IGNIXA_AUTH_HEADER`.

Add this polling refresh test:

```python
def test_polling_reauthenticates_and_refreshes_between_attempts(self):
    now = [1000]
    credential = type(
        "Credential",
        (),
        {
            "tokens": [
                type("Token", (), {"token": "token-1", "expires_on": 1400})(),
                type("Token", (), {"token": "token-2", "expires_on": 3000})(),
            ],
            "get_token": lambda self, scope: self.tokens.pop(0),
            "close": lambda self: None,
        },
    )()
    self.runtime._AUTH_PROVIDER = self.runtime._ManagedIdentityAuthProvider(
        "api://fhir/.default", credential, lambda: now[0]
    )
    document = _document()
    user, client = make_user()
    context = new_context(self.runtime, document)
    client.queue_response(fakes.FakeResponse(status_code=202, json_data={}))
    client.queue_response(fakes.FakeResponse(status_code=200, json_data={}))
    action = _operation(
        "op-1",
        type="read",
        resource="Patient",
        request_id="op-1",
        response_id="op-1",
        wait_for={"pollingStatusCode": 202, "maxAttempts": 2, "intervalMs": 1},
    )

    def advance(_seconds):
        now[0] = 1100

    with patch("gevent.sleep", side_effect=advance):
        run_operation(self, self.runtime, document, user, context, action)

    self.assertEqual(
        "Bearer" + " " + "token-1",
        client.calls[0]["headers"]["Authorization"],
    )
    self.assertEqual(
        "Bearer" + " " + "token-2",
        client.calls[1]["headers"]["Authorization"],
    )
```

Add this refresh-failure test:

```python
def test_token_failure_emits_sanitized_semantic_failure_without_http(self):
    credential = type(
        "Credential",
        (),
        {
            "get_token": lambda self, scope: (_ for _ in ()).throw(
                ValueError("secret credential response")
            ),
            "close": lambda self: None,
        },
    )()
    self.runtime._AUTH_PROVIDER = self.runtime._ManagedIdentityAuthProvider(
        "api://fhir/.default", credential
    )
    document = _document()
    user, client = make_user()
    context = new_context(self.runtime, document)
    action = _operation(
        "op-1",
        type="read",
        resource="Patient",
        request_id="op-1",
        response_id="op-1",
    )
    result = run_operation(self, self.runtime, document, user, context, action)

    self.assertTrue(result["failed"])
    self.assertEqual([], client.calls)
    event = user.environment.events.request.items[0]
    self.assertEqual("TESTSCRIPT_OPERATION", event["request_type"])
    self.assertNotIn("secret", str(event["exception"]))
```

- [ ] **Step 2: Run operation tests to verify RED**

```powershell
py -3.9 -m unittest discover -s test\Ignixa.TestScript.Locust.Tests\Python -p "test_runtime_operations.py" -v
```

Expected: managed identity application, override prevention, polling refresh, and 401 invalidation tests FAIL.

- [ ] **Step 3: Apply authentication immediately before each HTTP attempt**

Keep `_build_headers` responsible for IR headers only. After all custom TestScript headers are resolved, call `_apply_authentication(headers)` so `Authorization` is always the managed identity value.

In `_perform_request`, make a fresh case-insensitive copy and reapply auth before every request:

```python
request_headers = _new_headers(headers)
_apply_authentication(request_headers)
```

Pass `dict(request_headers)` to `user.client.request`. After a received response:

```python
if _response_status(response) == 401:
    _AUTH_PROVIDER.invalidate()
```

Do not issue another request after invalidation.

Wrap request building and `_perform_request_with_polling` in `_execute_operation` so a provider `RuntimeError` emits one source-qualified `TESTSCRIPT_OPERATION` failure and returns a failed operation.

Use `_apply_authentication` for fixture autocreate and autodelete headers. Catch provider `RuntimeError` in each fixture function, emit one fixture-scoped semantic failure, and send no HTTP request.

- [ ] **Step 4: Run all Python runtime tests**

```powershell
py -3.9 -m unittest discover -s test\Ignixa.TestScript.Locust.Tests\Python -p "test_*.py" -v
```

Expected: all Python runtime tests PASS; obsolete static-header parsing/application tests are gone, while the single regression proving the legacy setting is rejected remains.

- [ ] **Step 5: Commit HTTP authentication after explicit user approval and controller review**

```powershell
git add src\Core\Ignixa.TestScript.Locust\Python\ignixa_testscript_runtime.py test\Ignixa.TestScript.Locust.Tests\Python\test_runtime_authentication.py test\Ignixa.TestScript.Locust.Tests\Python\test_runtime_operations.py
git commit -m "Authenticate Locust FHIR requests with managed identity" `
  -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" `
  -m "Copilot-Session: 51cdcc9f-e427-404b-add7-32cadd089520"
```

### Task 4: Pin Azure Identity and preserve generated-artifact smoke

**Files:**
- Modify: `test/Ignixa.TestScript.Locust.Tests/Artifacts/LocustArtifactWriterTests.cs:20-21`
- Modify: `test/Ignixa.TestScript.Locust.Tests/Python/test_generated_artifact.py:73-80,293-301,319-335`
- Modify: `src/Core/Ignixa.TestScript.Locust/Python/requirements.txt`

- [ ] **Step 1: Write failing generated requirements assertions**

Change the C# exact-content contract to:

```csharp
private const string ExpectedRequirementsText =
    "locust==2.33.2\n"
    + "fhirpathpy==2.1.0\n"
    + "requests==2.32.3\n"
    + "azure-identity==1.25.3\n";
```

In `test_generated_artifact.py`, assert:

```python
self.assertIn(
    "azure-identity==1.25.3",
    requirements.splitlines(),
    "generated requirements must pin Azure Identity for managed identity authentication",
)
```

Compile with sentinel authentication configuration in the subprocess environment and prove it is not copied into any artifact:

```python
compile_env = os.environ.copy()
compile_env["IGNIXA_AUTH_MODE"] = "managed-identity"
compile_env["IGNIXA_AUTH_SCOPE"] = "api://sentinel-fhir/.default"
compile_env["IGNIXA_MANAGED_IDENTITY_CLIENT_ID"] = "sentinel-client-id"
result = subprocess.run(
    command,
    cwd=str(_REPO_ROOT),
    env=compile_env,
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
    universal_newlines=True,
    timeout=900,
)
```

Add:

```python
def test_authentication_values_are_not_embedded_in_artifact(self):
    artifact_text = "\n".join(
        path.read_text(encoding="utf-8")
        for path in self._artifact_dir.iterdir()
        if path.suffix in {".json", ".py", ".txt"}
    )
    self.assertNotIn("api://sentinel-fhir/.default", artifact_text)
    self.assertNotIn("sentinel-client-id", artifact_text)
```

Replace `_ENV_AUTH_HEADER` with `_ENV_AUTH_MODE`, `_ENV_AUTH_SCOPE`, and `_ENV_MANAGED_IDENTITY_CLIENT_ID` in `_MANAGED_ENV_KEYS`. During the loopback run, remove all three keys so the generated runtime proves default `none` mode.

```python
_ENV_AUTH_MODE = "IGNIXA_AUTH_MODE"
_ENV_AUTH_SCOPE = "IGNIXA_AUTH_SCOPE"
_ENV_MANAGED_IDENTITY_CLIENT_ID = "IGNIXA_MANAGED_IDENTITY_CLIENT_ID"
_MANAGED_ENV_KEYS = (
    _ENV_BASE_URL,
    _ENV_WAIT_MIN,
    _ENV_WAIT_MAX,
    _ENV_FIXTURE_SEED,
    _ENV_AUTH_MODE,
    _ENV_AUTH_SCOPE,
    _ENV_MANAGED_IDENTITY_CLIENT_ID,
)
```

Before importing the generated artifact:

```python
os.environ.pop(_ENV_AUTH_MODE, None)
os.environ.pop(_ENV_AUTH_SCOPE, None)
os.environ.pop(_ENV_MANAGED_IDENTITY_CLIENT_ID, None)
```

- [ ] **Step 2: Run artifact tests to verify RED**

```powershell
dotnet test test\Ignixa.TestScript.Locust.Tests\Ignixa.TestScript.Locust.Tests.csproj --filter "FullyQualifiedName~LocustArtifactWriterTests"
py -3.9 -m unittest discover -s test\Ignixa.TestScript.Locust.Tests\Python -p "test_generated_artifact.py" -v
```

Expected: both commands FAIL because generated requirements do not contain `azure-identity==1.25.3`.

- [ ] **Step 3: Add the pinned dependency**

Append exactly:

```text
azure-identity==1.25.3
```

to `src/Core/Ignixa.TestScript.Locust/Python/requirements.txt`, preserving LF endings and the final newline.

- [ ] **Step 4: Install the locked requirements and verify GREEN**

```powershell
py -3.9 -m pip install --disable-pip-version-check -r src\Core\Ignixa.TestScript.Locust\Python\requirements.txt
dotnet test test\Ignixa.TestScript.Locust.Tests\Ignixa.TestScript.Locust.Tests.csproj --filter "FullyQualifiedName~LocustArtifactWriterTests"
py -3.9 -m unittest discover -s test\Ignixa.TestScript.Locust.Tests\Python -p "test_generated_artifact.py" -v
```

Expected: package installation succeeds on Python 3.9.19 and both test commands PASS.

- [ ] **Step 5: Commit dependency and smoke coverage after explicit user approval and controller review**

```powershell
git add src\Core\Ignixa.TestScript.Locust\Python\requirements.txt test\Ignixa.TestScript.Locust.Tests\Artifacts\LocustArtifactWriterTests.cs test\Ignixa.TestScript.Locust.Tests\Python\test_generated_artifact.py
git commit -m "Pin Azure Identity for generated Locust tests" `
  -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" `
  -m "Copilot-Session: 51cdcc9f-e427-404b-add7-32cadd089520"
```

### Task 5: Document compilation and managed identity deployment

**Files:**
- Modify: `docs/site/docs/core-sdk/testscript.md:270-310`
- Modify: `docs/features/testscript/investigations/azure-load-testing.md:7-183`

- [ ] **Step 1: Add the Locust compilation guide**

Add `## Compile TestScript for Azure Load Testing` before `## Published FHIR Conformance Report`. Include this exact command:

````markdown
## Compile TestScript for Azure Load Testing

Compile a parsed TestScript into the flat five-file Locust artifact accepted by Azure Load Testing:

```bash
ignixa-matrix compile-locust \
  --test path/to/TestScript.json \
  --out artifacts/testscript-load \
  --fhir-version 4.0 \
  --fixture-variants 100
```

The output contains `testscript.ir.json`, `diagnostics.json`, `locustfile.py`,
`ignixa_testscript_runtime.py`, and `requirements.txt`. Upload all five files together.
Each virtual-user iteration executes one complete setup/test/teardown flow with isolated
variables and fixtures.

The generated workload targets current Ignixa evaluator parity, not every behavior in the
HL7 TestScript specification. It supports the compiler's declared operation/assertion
subset, Ignixa parameterization and capability extensions, bounded FhirFakes pools, and
FHIRPath expressions accepted by the compatibility analyzer. `fhir.resources` is not a
runtime dependency or profile validator.

Azure Load Testing currently provides Python 3.9.19 and Locust 2.33.2. Generated
requirements pin `fhirpathpy==2.1.0`, `requests==2.32.3`, and `azure-identity==1.25.3`.
Run the original .NET evaluator first when an authoritative FHIR `TestReport` is required.

Set the target with `IGNIXA_BASE_URL` or Locust `--host`. Use `IGNIXA_FIXTURE_SEED`
for repeatable fixture selection and `IGNIXA_WAIT_MIN_SECONDS` /
`IGNIXA_WAIT_MAX_SECONDS` for per-iteration wait time.

HTTP metrics and synthetic `TESTSCRIPT_ASSERT` / `TESTSCRIPT_OPERATION` metrics use
source-qualified names. `diagnostics.json` maps those names back to TestScript source paths.
````

- [ ] **Step 2: Document managed identity configuration and limits**

Add a configuration table:

```markdown
### Managed identity

| Variable | Value |
| --- | --- |
| `IGNIXA_AUTH_MODE` | `none` (default) or `managed-identity` |
| `IGNIXA_AUTH_SCOPE` | Target API application ID URI with `/.default`; required for managed identity |
| `IGNIXA_MANAGED_IDENTITY_CLIENT_ID` | User-assigned identity client ID; omit for system-assigned identity |

Assign the system-assigned or user-assigned identity to the Azure Load Testing resource,
select it as the engine reference identity (`referenceIdentities` with `kind: Engine` when
the test is configured as code), and authorize it on the target API. The target must trust
Microsoft Entra tokens for the configured scope. Static authorization headers, service-principal
secrets, and developer-credential fallback are unsupported.

Azure Load Testing disables multi-region load distribution when managed identity
authentication is selected.
```

State that Azure Load Testing must assign/select the engine identity, the target must trust Microsoft Entra tokens and authorize that identity, static headers and service-principal secrets are unsupported, and Azure Load Testing disables multi-region load distribution for managed identity authentication.

Link the official secured-endpoint and managed-identity documentation from the design spec.

- [ ] **Step 3: Record implementation evidence and deferred Azure validation**

In `azure-load-testing.md`:

- preserve `**Status**: Viable`;
- update the generated artifact description to the exact five-file output;
- state that the compiler/runtime/parity/CI contracts are implemented;
- link the compiler, runtime, generated smoke, parity contract, and workflow paths;
- document that the local generated-artifact smoke passes unauthenticated;
- document managed identity as the only authenticated mode; and
- state that the Azure smoke is deferred because `ignixa-alt-e2e-ecow` trusts custom OpenIddict tokens rather than Microsoft Entra tokens.

Do not include a secret reference, credential, SAS URL, access token, or fabricated Azure run ID.

Add an implementation evidence section with this content, adjusting relative links only if Docusaurus requires it:

```markdown
## Implementation Evidence

The viable compiler path is implemented:

- `src/Core/Ignixa.TestScript.Locust/` contains the IR compiler, compatibility analyzer,
  artifact writer, Locust loader, and shared Python runtime.
- `tools/Ignixa.ConformanceMatrix.Cli/` exposes `compile-locust`.
- `test/Ignixa.TestScript.Locust.Tests/Contracts/` holds shared FHIRPath and runtime
  parity contracts consumed by .NET and Python.
- `test/Ignixa.TestScript.Locust.Tests/Python/test_generated_artifact.py` compiles the
  shipped CRUD TestScript and runs the generated five-file artifact against a deterministic
  loopback FHIR server.
- `.github/workflows/pr-build.yml` and `.github/workflows/ci.yml` install the generated
  Python 3.9 requirements and run the complete runtime contract suite.

Generated artifacts support unauthenticated targets by default and Microsoft Entra managed
identity authentication through the Azure Load Testing engine identity. Static authorization
headers and service-principal client secrets are intentionally unsupported.

The local generated-artifact smoke passes. Live Azure validation is deferred because the
current non-production target, `ignixa-alt-e2e-ecow`, trusts its custom OpenIddict issuer
rather than Microsoft Entra tokens. Validation can proceed after a non-production target
trusts an Entra application ID URI and authorizes the selected load-test managed identity.
No Azure run ID is recorded because no compatible live run has occurred.
```

- [ ] **Step 4: Check documentation diffs**

```powershell
git diff --check -- docs\site\docs\core-sdk\testscript.md docs\features\testscript\investigations\azure-load-testing.md
```

Expected: no whitespace errors.

- [ ] **Step 5: Commit documentation after explicit user approval and controller review**

```powershell
git add docs\site\docs\core-sdk\testscript.md docs\features\testscript\investigations\azure-load-testing.md
git commit -m "Document TestScript Locust managed identity" `
  -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" `
  -m "Copilot-Session: 51cdcc9f-e427-404b-add7-32cadd089520"
```

### Task 6: Final verification, review, and branch push

**Files:**
- Verify: all files changed by Tasks 1-5

- [ ] **Step 1: Confirm the legacy authentication surface is gone**

```powershell
rg "_parse_auth_header|DefaultAzureCredential" src\Core\Ignixa.TestScript.Locust docs\site\docs\core-sdk\testscript.md
rg "IGNIXA_AUTH_HEADER" src\Core\Ignixa.TestScript.Locust test\Ignixa.TestScript.Locust.Tests docs\site\docs\core-sdk\testscript.md
```

Expected: the first search has no matches — `_parse_auth_header` and `DefaultAzureCredential` do not appear in production runtime or user documentation. The second search matches only the runtime rejection constant (`_LEGACY_AUTH_HEADER_ENV = "IGNIXA_AUTH_HEADER"`) and its single use in `_create_auth_provider`, plus the regression test that proves the legacy setting is rejected without exposing its value; `IGNIXA_AUTH_HEADER` never appears as an accepted configuration path or user-visible example.

- [ ] **Step 2: Run focused Locust verification**

```powershell
dotnet test test\Ignixa.TestScript.Locust.Tests\Ignixa.TestScript.Locust.Tests.csproj
dotnet test test\Ignixa.ConformanceMatrix.Cli.Tests\Ignixa.ConformanceMatrix.Cli.Tests.csproj
py -3.9 -m unittest discover -s test\Ignixa.TestScript.Locust.Tests\Python -p "test_*.py" -v
```

Expected: all C# tests pass for every target framework and all Python 3.9 tests pass.

- [ ] **Step 3: Run repository verification**

```powershell
dotnet build All.sln
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"
git diff --check
git status --short
```

Expected: build succeeds with zero warnings/errors, all non-E2E tests pass, diff check reports no errors, and status contains no uncommitted implementation files.

- [ ] **Step 4: Request independent specification and quality review**

Use `superpowers:requesting-code-review` against:

- `docs/superpowers/specs/2026-07-23-testscript-locust-managed-identity-design.md`;
- this plan;
- the diff from `cf7fb669` through the implementation HEAD.

Required review result: no unresolved spec gaps, correctness defects, credential leaks, or Python 3.9 incompatibilities.

- [ ] **Step 5: Push the completed branch**

```powershell
git push -u origin brendankowitz-investigate-azure-load-testing
```

Expected: the remote branch is created or updated successfully at the verified implementation HEAD.

- [ ] **Step 6: Record the intentional Azure smoke deferral**

The completion handoff must state that local generated-artifact validation passed and that a live Azure managed identity smoke remains blocked until a non-production Ignixa target trusts Microsoft Entra tokens. Do not claim a live Azure run occurred.

import copy
import decimal
import hashlib
import itertools
import json
import logging
import os
import re
import threading
import socket
import time


SUPPORTED_SCHEMA_MAJOR = 1

_USER_ORDINALS = itertools.count()

_logger = logging.getLogger("ignixa.testscript")

# Lazily-loaded FHIRPath R4 model (used by the assertion/capability adapter so choice-type
# element navigation matches Ignixa's schema-aware FhirPath engine). This is the *model*, never
# a fetched CapabilityStatement, so caching it retains no per-run capability state.
_FHIRPATH_MODEL = None

_VARIABLE_PATTERN = re.compile(r"\$\{([^}]+)\}")

_MISSING_ENV = object()

_METHOD_BY_TYPE = {
    "create": "POST",
    "read": "GET",
    "vread": "GET",
    "search": "GET",
    "history": "GET",
    "capabilities": "GET",
    "conforms": "GET",
    "update": "PUT",
    "updateCreate": "PUT",
    "patch": "PATCH",
    "delete": "DELETE",
}

# Immutable capability-gate decision scaffold. Task 9 replaces these defaults
# with real CapabilityStatement-derived state and adds explicit
# initialize/clear APIs. Until then the runtime fails open: with no decisions
# recorded, the suite and every test are allowed to run. Tests may assign to
# these module attributes directly after calling ``load_runtime()``.
_SUITE_ALLOWED = True
_TEST_DECISIONS = {}


def _check_schema_version(document):
    """Raise if the document's schema major version is unsupported.

    This runs before any per-user state (such as the ordinal counter) is
    consumed, so a rejected document never advances runtime-local sequences.
    """
    major = int(document["schemaVersion"].split(".", 1)[0])
    if major != SUPPORTED_SCHEMA_MAJOR:
        raise RuntimeError(
            f"Unsupported TestScript IR schema {document['schemaVersion']}"
        )


def initialize_user(document, user):
    """Validate the document and allocate the minimal per-user state.

    Per-user state is intentionally limited to an iteration counter and a
    stable ordinal; it never retains the Locust user, execution context,
    fixture resources, requests, responses, or capability documents.
    """
    _check_schema_version(document)
    return {
        "iteration": 0,
        "ordinal": next(_USER_ORDINALS),
    }


def _new_context(document, user_state):
    """Allocate a fresh, independent execution context.

    Every call returns newly allocated dictionaries so two invocations -
    whether from the same virtual user or different ones - never share
    mutable execution state.
    """
    return {
        "variables": {
            item["name"]: item["defaultValue"]
            for item in document.get("variables", [])
            if item.get("defaultValue") is not None
        },
        "fixtures": {},
        "requests": {},
        "responses": {},
        "last_request": None,
        "last_response": None,
        "user_state": user_state,
    }


def _clone_test_context(context):
    """Clone the mutable parts of a context for a discardable test.

    Only the four dictionaries are cloned as new top-level dictionary
    objects; ``last_request``/``last_response`` references and the
    ``user_state`` reference are copied as-is. Mutations performed against
    the clone (dictionary key assignment/removal) never affect the original
    context, so parameter expansions cannot leak state to later tests.
    """
    return {
        "variables": dict(context["variables"]),
        "fixtures": dict(context["fixtures"]),
        "requests": dict(context["requests"]),
        "responses": dict(context["responses"]),
        "last_request": context["last_request"],
        "last_response": context["last_response"],
        "user_state": context["user_state"],
    }


def _apply_initial_variables(context, initial_variables):
    for name, value in initial_variables.items():
        context["variables"][name] = value


def _suite_allowed():
    return _SUITE_ALLOWED


def _test_allowed(test_id):
    return _TEST_DECISIONS.get(test_id, True)


def _apply_authentication(headers):
    authorization = _AUTH_PROVIDER.authorization_value()
    if authorization is not None:
        headers["Authorization"] = authorization


def _fetch_capability(host):
    """Fetch the target server's CapabilityStatement, failing OPEN on any I/O error.

    Performs exactly one uninstrumented ``GET {host}/metadata`` on a short-lived
    ``requests.Session`` (never the Locust user client, so it is not counted as load),
    with the active authentication provider applied and a 30s timeout. A transport
    error, HTTP error status, unparseable body, or non-dict JSON all yield ``None``
    (fail open: no capability known). Authentication acquisition failures are not
    swallowed. ``requests`` is imported lazily so the module stays importable without
    third-party dependencies present.
    """
    import requests

    url = f"{host.rstrip('/')}/metadata"
    headers = _new_headers()
    _apply_authentication(headers)

    try:
        with requests.Session() as session:
            response = session.get(url, timeout=30, headers=headers)
            response.raise_for_status()
            body = response.json()
        return body if isinstance(body, dict) else None
    except (requests.exceptions.RequestException, ValueError):
        return None


def _evaluate_capability_requirement(expression, capability, scope_id):
    """Evaluate a ``requiresCapability`` predicate against the CapabilityStatement.

    Mirrors .NET ``EvaluateCapabilityRequirement``: an absent expression or an
    unavailable capability fails OPEN (allowed); a malformed expression evaluated
    against an *available* capability fails CLOSED (disallowed). The broad ``except``
    is required because ``fhirpathpy`` raises a bare ``Exception`` for some malformed
    expressions; the failure is logged as a structured error carrying the ``scope_id``
    (the suite's stable IR identifier or the owning test's id), the offending
    expression, and the evaluator exception, so it is never silent.
    """
    if not expression:
        return True
    if capability is None:
        return True

    try:
        return bool(_evaluate_fhirpath(expression, capability, "boolean"))
    except Exception as exc:  # noqa: BLE001 - parity with .NET; logged, never silent.
        _logger.warning(
            "requiresCapability expression '%s' for scope '%s' failed to evaluate: %s",
            expression,
            scope_id,
            exc,
        )
        return False


def initialize_engine(document, environment):
    """Derive the immutable suite/test capability decisions for a run.

    Closes any prior auth provider and resets stale decisions and the per-user ordinal
    counter *before* validation, so even a failed startup leaves the engine ready to
    spawn user 0. The IR schema is validated before any auth-provider construction,
    credential acquisition, or metadata fetch. The FHIR base URL is resolved from
    ``environment.host`` first, then ``IGNIXA_BASE_URL`` (missing both is a hard error).
    Managed identity auth is initialized before metadata so one token is acquired and
    cached prior to the uninstrumented capability probe. Only the immutable suite bool
    and per-test-id decision map are retained; the fetched capability, HTTP session,
    and response are never stored.
    """
    global _AUTH_PROVIDER, _SUITE_ALLOWED, _TEST_DECISIONS, _USER_ORDINALS

    clear_engine()

    _check_schema_version(document)

    host = environment.host or os.getenv("IGNIXA_BASE_URL")
    if not host:
        raise RuntimeError(
            "No FHIR base URL available: set the Locust host (environment.host) or "
            "the IGNIXA_BASE_URL environment variable"
        )

    provider = None
    try:
        provider = _create_auth_provider()
        provider.initialize()
    except RuntimeError:
        if provider is not None:
            try:
                provider.close()
            except RuntimeError as close_exc:
                _logger.warning("%s", close_exc)
        _AUTH_PROVIDER = _NoAuthProvider()
        _SUITE_ALLOWED = False
        _TEST_DECISIONS = {
            test["id"]: False
            for test in document.get("tests", [])
            if test.get("id") is not None
        }
        raise
    _AUTH_PROVIDER = provider

    capability = _fetch_capability(host)

    suite_scope = document["metadata"]["source"]
    _SUITE_ALLOWED = _evaluate_capability_requirement(
        document.get("requiresCapability"), capability, suite_scope
    )

    decisions = {}
    for test in document.get("tests", []):
        requirement = test.get("requiresCapability")
        if requirement is not None:
            decisions[test["id"]] = _evaluate_capability_requirement(
                requirement, capability, test["id"]
            )
    _TEST_DECISIONS = decisions


def clear_engine():
    """Reset the engine's capability decisions and per-user ordinal counter.

    Called on Locust ``test_stop`` so a subsequent run starts from a clean, fail-open
    state with user ordinals restarting at 0 and no active auth provider.
    """
    global _AUTH_PROVIDER, _SUITE_ALLOWED, _TEST_DECISIONS, _USER_ORDINALS

    provider = _AUTH_PROVIDER
    failure = None
    try:
        provider.close()
    except RuntimeError as exc:
        failure = exc
    finally:
        _AUTH_PROVIDER = _NoAuthProvider()
        _SUITE_ALLOWED = True
        _TEST_DECISIONS = {}
        _USER_ORDINALS = itertools.count()

    if failure is not None:
        raise failure


def _fixture_variant_index(seed, hostname, ordinal, iteration, fixture_id, pool_length):
    """Deterministically select a fixture variant index.

    The selection hashes ``seed|hostname|ordinal|iteration|fixture_id`` (exact
    UTF-8 join, no surrounding spaces) with SHA-256, converts the full digest
    to an integer via ``int.from_bytes(digest, "big")``, and reduces it modulo
    ``pool_length``. Python's randomized ``hash()`` is never used so the
    result is stable across processes and runs.
    """
    if pool_length <= 0:
        raise RuntimeError(
            "Fixture variant pool length must be positive, got "
            f"{pool_length} for fixture '{fixture_id}'"
        )
    key = f"{seed}|{hostname}|{ordinal}|{iteration}|{fixture_id}"
    digest = hashlib.sha256(key.encode("utf-8")).digest()
    return int.from_bytes(digest, "big") % pool_length


def _materialize_fixtures(document, context, ordinal, iteration):
    """Materialize every emitted fixture's selected variant into the context.

    Each selected JSON variant is deep-copied so neither the shared IR
    document nor another execution context can observe or be affected by
    mutations performed against this context's fixture resource.
    """
    seed = os.getenv("IGNIXA_FIXTURE_SEED", "")
    hostname = socket.gethostname()
    for fixture in document.get("fixtures", []):
        fixture_id = fixture["id"]
        variants = fixture.get("variants") or []
        if not variants:
            raise RuntimeError(
                f"Fixture '{fixture_id}' has no variants to materialize"
            )
        index = _fixture_variant_index(
            seed, hostname, ordinal, iteration, fixture_id, len(variants)
        )
        context["fixtures"][fixture_id] = copy.deepcopy(variants[index])


def _resolve(template, context):
    """Substitute ``${name}`` placeholders from ``context["variables"]``.

    ``None`` and plain text without placeholders pass through unchanged. An
    undefined variable raises so callers can convert it into a single
    source-qualified semantic failure before any HTTP request is attempted.
    """
    if template is None:
        return None

    variables = context["variables"]

    def _substitute(match):
        name = match.group(1)
        if name not in variables:
            raise RuntimeError(f"Variable '{name}' is not defined")
        return str(variables[name])

    return _VARIABLE_PATTERN.sub(_substitute, template)


def _derive_method(action):
    """Return the action's IR-baked method, deriving one from ``type`` if unset."""
    method = action.get("method")
    if method:
        return method
    return _METHOD_BY_TYPE.get(action.get("type"), "POST")


def _derive_url(operation, context):
    """Derive the relative request URL for an operation.

    An explicit ``url`` always wins (fully variable-resolved). Otherwise a
    POST search collapses to the ``<resource>/_search`` form (params are
    never appended to the URL for a POST search - they become the request
    body instead). A ``$``-prefixed custom operation type is appended after
    the resource (or stands alone at the system level). Everything else is
    the resource followed by resolved params (e.g. a GET search's query
    string, or a path-suffix param such as ``/${id}``).
    """
    explicit_url = operation.get("url")
    if explicit_url is not None:
        return _resolve(explicit_url, context)

    resource = operation.get("resource") or ""
    op_type = operation.get("type") or ""
    method = operation.get("method")

    if op_type == "search" and method == "POST":
        return f"{resource}/_search"

    parameters = _resolve(operation.get("params"), context) or ""

    if op_type.startswith("$"):
        op_path = op_type if not resource else f"{resource}/{op_type}"
        return f"{op_path}{parameters}"

    return f"{resource}{parameters}"


def _metric_name(document, action_id):
    """Join the document's metadata source with an action/fixture id.

    Used for both native HTTP event names and semantic ``TESTSCRIPT_OPERATION``
    event names, so a single dashboard metric always identifies exactly one
    source-qualified action.
    """
    return f"{document['metadata']['source']}::{action_id}"


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


_AUTH_PROVIDER = _NoAuthProvider()


class _ManagedIdentityAuthProvider:
    def __init__(self, scope, credential, clock=time.time):
        self._scope = scope
        self._credential = credential
        self._clock = clock
        self._lock = threading.Lock()
        self._cached_access_token = None

    def initialize(self):
        return self.authorization_value()

    def authorization_value(self):
        token = self._cached_access_token
        if token is not None and token.expires_on - self._clock() > _AUTH_REFRESH_WINDOW_SECONDS:
            return f"{_AUTHORIZATION_SCHEME} {token.token}"

        with self._lock:
            token = self._cached_access_token
            if token is not None and token.expires_on - self._clock() > _AUTH_REFRESH_WINDOW_SECONDS:
                return f"{_AUTHORIZATION_SCHEME} {token.token}"

            try:
                token = self._credential.get_token(self._scope)
            except Exception as exc:  # noqa: BLE001 - stable wrapper around credential acquisition failures.
                raise RuntimeError(
                    f"Failed to acquire managed identity token ({type(exc).__name__})"
                ) from exc

            self._cached_access_token = token
            return f"{_AUTHORIZATION_SCHEME} {token.token}"

    def invalidate(self):
        with self._lock:
            self._cached_access_token = None

    def close(self):
        close = getattr(self._credential, "close", None)
        if close is None:
            return None

        try:
            close()
        except Exception as exc:  # noqa: BLE001 - stable wrapper around credential disposal failures.
            raise RuntimeError(
                f"Failed to close managed identity credential ({type(exc).__name__})"
            ) from exc
        return None


def _non_empty_env(name, required=False):
    raw = os.environ.get(name, _MISSING_ENV)
    if raw is _MISSING_ENV:
        if required:
            raise RuntimeError(f"Missing required environment variable {name}")
        return None

    value = raw.strip()
    if not value:
        raise RuntimeError(f"Environment variable {name} must be non-empty")
    return value


def _create_managed_identity_credential(client_id):
    from azure.identity import ManagedIdentityCredential

    if client_id is None:
        return ManagedIdentityCredential()
    return ManagedIdentityCredential(client_id=client_id)


def _create_auth_provider():
    if "IGNIXA_AUTH_HEADER" in os.environ:
        raise RuntimeError("IGNIXA_AUTH_HEADER is no longer supported")

    mode = _non_empty_env("IGNIXA_AUTH_MODE") or "none"
    if mode == "none":
        return _NoAuthProvider()

    if mode != "managed-identity":
        raise RuntimeError("IGNIXA_AUTH_MODE must be 'none' or 'managed-identity'")

    scope = _non_empty_env("IGNIXA_AUTH_SCOPE", required=True)
    client_id = _non_empty_env("IGNIXA_MANAGED_IDENTITY_CLIENT_ID")

    try:
        credential = _create_managed_identity_credential(client_id)
    except Exception as exc:  # noqa: BLE001 - stable wrapper around credential creation failures.
        raise RuntimeError(
            f"Failed to create managed identity credential ({type(exc).__name__})"
        ) from exc

    return _ManagedIdentityAuthProvider(scope, credential)


def _parse_auth_header():
    """Parse ``IGNIXA_AUTH_HEADER`` into a ``(name, value)`` pair.

    The environment key being entirely absent means "no auth header" (returns
    ``None``). Any other value - including an explicit empty string - is
    parsed as ``Name: value`` (split on the first colon only, so colons may
    appear in the value itself); a missing separator or an empty stripped
    name/value is malformed and raises, rather than being treated as unset.
    """
    raw = os.environ.get("IGNIXA_AUTH_HEADER", _MISSING_ENV)
    if raw is _MISSING_ENV:
        return None

    name, separator, value = raw.partition(":")
    name = name.strip()
    value = value.strip()
    if not separator or not name or not value:
        raise RuntimeError(
            "IGNIXA_AUTH_HEADER must be 'Header-Name: value' with a "
            f"non-empty name and value, got {raw!r}"
        )
    return (name, value)


def _new_headers(initial=None):
    """Return an empty (or seeded) case-insensitive header mapping.

    ``requests.structures.CaseInsensitiveDict`` is imported lazily so the
    module stays importable without third-party dependencies installed.
    """
    from requests.structures import CaseInsensitiveDict

    return CaseInsensitiveDict(initial or {})


def _build_headers(action, context):
    """Build request headers in the locked precedence order.

    Order: the parsed auth header first, then the IR's baked
    ``accept``/``contentType`` fields, then resolved custom script headers -
    each later step may override an earlier one's key case-insensitively.
    """
    headers = _new_headers()

    auth = _parse_auth_header()
    if auth is not None:
        auth_name, auth_value = auth
        headers[auth_name] = auth_value

    if action.get("accept"):
        headers["Accept"] = action["accept"]
    if action.get("contentType"):
        headers["Content-Type"] = action["contentType"]

    for header in action.get("headers") or []:
        field = _resolve(header["field"], context)
        value = _resolve(header["value"], context)
        headers[field] = value

    return headers


def _lookup_source_body(source_id, context):
    """Resolve a ``sourceId`` to a body dict, checking fixtures then responses.

    Returns ``(body_or_none, found)``. ``found`` is ``False`` only when
    neither a fixture nor a response is known under ``source_id`` at all;
    a known response with no parseable JSON body still counts as found (and
    simply contributes no body), matching "malformed JSON is no body".
    """
    if source_id in context["fixtures"]:
        return context["fixtures"][source_id], True
    if source_id in context["responses"]:
        return _response_json_or_none(context["responses"][source_id]), True
    return None, False


def _resolve_body(action, context, headers):
    """Resolve the request body bytes and finalize the Content-Type header.

    Mutates ``headers`` (a freshly built, non-shared mapping) in place to
    reflect the final Content-Type decision: forced form type for a POST
    search, default/custom FHIR JSON type for a sourceId body, or complete
    removal (preserving every other header) when there is no body at all.
    """
    method = action.get("method")
    op_type = action.get("type")

    if op_type == "search" and method == "POST":
        raw_params = _resolve(action.get("params"), context) or ""
        form_body = raw_params.lstrip("?")
        headers["Content-Type"] = "application/x-www-form-urlencoded; charset=utf-8"
        return form_body.encode("utf-8")

    source_id = action.get("sourceId")
    if source_id is not None:
        resource, found = _lookup_source_body(source_id, context)
        if not found:
            raise RuntimeError(
                f"sourceId '{source_id}' refers to no known fixture or response"
            )
        if resource is not None:
            if not action.get("contentType"):
                headers["Content-Type"] = "application/fhir+json; charset=utf-8"
            return json.dumps(resource, separators=(",", ":")).encode("utf-8")

    if "Content-Type" in headers:
        del headers["Content-Type"]
    return None


def _build_request(action, context):
    """Derive the URL, headers, and body bytes for one operation action."""
    url = _derive_url(action, context)
    headers = _build_headers(action, context)
    data = _resolve_body(action, context, headers)
    return url, headers, data


def _store_request(context, request_id, wrapper):
    """Record a request wrapper under ``request_id`` (if any) and as last.

    ``context["requests"]`` is replaced with a new dict rather than mutated
    in place, and ``wrapper`` is always a brand-new dict, so no existing
    nested object is ever mutated - preserving Task 7's shallow-clone
    assumption for discardable test contexts.
    """
    if request_id is not None:
        context["requests"] = dict(context["requests"])
        context["requests"][request_id] = wrapper
    context["last_request"] = wrapper


def _store_response(context, response_id, response):
    """Record the actual response object under ``response_id`` and as last.

    The real received response object is stored directly (never a dict
    snapshot), so later code - including Task 9 - can inspect parse errors,
    headers, and status through it.
    """
    if response_id is not None:
        context["responses"] = dict(context["responses"])
        context["responses"][response_id] = response
    context["last_response"] = response


def _response_status(response):
    return response.status_code


def _response_headers(response):
    return response.headers


def _response_json_or_none(response):
    """Return the response's parsed JSON body, or ``None`` if unparseable.

    Malformed/absent JSON is treated as "no body" for Task 8's sourceId and
    extraction purposes; Task 9 will surface the parse failure itself.
    """
    try:
        return response.json()
    except ValueError:
        return None


def _fire_semantic_failure(user, metric_name, exc):
    """Fire exactly one ``TESTSCRIPT_OPERATION`` semantic failure event."""
    user.environment.events.request.fire(
        request_type="TESTSCRIPT_OPERATION",
        name=metric_name,
        response_time=0,
        response_length=0,
        exception=exc,
        response=None,
    )


def _perform_request(user, method, url, headers, data, metric_name):
    """Perform one HTTP attempt, firing exactly one native event.

    Any received response - success, 4xx, or 5xx - is marked ``success()``
    unless the response itself carries a transport ``.error`` (the "returned
    Locust error response" case), which is left unmarked so it fires its own
    single native failure event on context exit. A directly raised
    ``requests.exceptions.RequestException`` means no response context ever
    existed, so exactly one native event is fired manually here instead; any
    other exception type is left to propagate uncaught.
    """
    import requests

    try:
        with user.client.request(
            method,
            url,
            name=metric_name,
            catch_response=True,
            headers=dict(headers),
            data=data,
        ) as response:
            if getattr(response, "error", None) is None:
                response.success()
        return response
    except requests.exceptions.RequestException as exc:
        user.environment.events.request.fire(
            request_type=method,
            name=metric_name,
            response_time=0,
            response_length=0,
            exception=exc,
            response=None,
        )
        return None


def _perform_request_with_polling(user, method, url, headers, data, metric_name, wait_for):
    """Perform one attempt, then poll while ``waitFor`` requests it.

    Returns ``(response_or_none, exhausted)``. ``response`` is ``None`` only
    when a transport exception stopped the attempt (already reported via its
    own native event); ``exhausted`` is ``True`` only when polling ran out of
    attempts while still observing the polling status code.
    """
    response = _perform_request(user, method, url, headers, data, metric_name)
    if wait_for is None or response is None:
        return response, False

    polling_status = wait_for["pollingStatusCode"]
    max_attempts = wait_for["maxAttempts"]
    interval_ms = wait_for["intervalMs"]
    attempts = 1

    while _response_status(response) == polling_status and attempts < max_attempts:
        import gevent

        gevent.sleep(interval_ms / 1000.0)
        response = _perform_request(user, method, url, headers, data, metric_name)
        attempts += 1
        if response is None:
            return None, False

    exhausted = _response_status(response) == polling_status
    return response, exhausted


def _extract_by_header(response, field):
    return _response_headers(response).get(field)


def _extract_by_path(response, path):
    """Descend a dotted path through the response JSON body.

    Only ever descends into JSON objects; hitting anything else (including
    an array) mid-path is a no-op, not an error. Terminal strings pass
    through raw; terminal numbers/booleans/objects/arrays are rendered as
    compact JSON text (booleans lower-case, matching FHIRPath semantics).
    """
    body = _response_json_or_none(response)
    if body is None:
        return None

    current = body
    for part in path.split("."):
        if not part:
            continue
        if not isinstance(current, dict):
            return None
        current = current.get(part)

    if current is None:
        return None
    if isinstance(current, str):
        return current
    if isinstance(current, bool):
        return "true" if current else "false"
    return json.dumps(current, separators=(",", ":"))


def _extract_by_fhirpath(response, expression):
    """Evaluate a FHIRPath expression against the response body.

    An empty result, a multi-value result, or a single complex (dict/list)
    result are all no-ops (the last matches .NET's ``AsString()`` returning
    null for non-primitive types). A malformed expression raises so the
    caller can record one semantic failure and continue with the next
    variable.
    """
    body = _response_json_or_none(response)
    if body is None:
        return None

    import fhirpathpy

    try:
        results = fhirpathpy.evaluate(body, expression)
    except Exception as exc:
        raise RuntimeError(
            f"FHIRPath expression '{expression}' failed to evaluate: {exc}"
        )

    if len(results) != 1:
        return None

    value = results[0]
    if value is None:
        return None
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, (dict, list)):
        return None
    if isinstance(value, str):
        return value
    return str(value)


def _fhirpath_model():
    """Return the cached FHIRPath R4 model, importing ``fhirpathpy`` lazily.

    The R4 model is required so choice-type navigation (e.g. ``Patient.deceased``)
    resolves the same way Ignixa's schema-aware FhirPath engine resolves it.
    """
    global _FHIRPATH_MODEL
    if _FHIRPATH_MODEL is None:
        from fhirpathpy.models import models

        _FHIRPATH_MODEL = models["r4"]
    return _FHIRPATH_MODEL


def _evaluate_fhirpath(expression, resource, shape):
    """Evaluate ``expression`` against ``resource`` in one of two Ignixa-parity shapes.

    ``boolean`` mirrors Ignixa ``IsTrue``: a real ``bool`` that is ``True`` only for a
    single ``true`` boolean result, ``False`` otherwise. ``scalar`` mirrors
    ``Select(...).AsString()``: the FhirPath ``toString()`` of a single primitive value
    (lower-case booleans), or ``None`` for empty, multi-valued, or complex results.

    ``fhirpathpy`` is imported lazily and evaluated with the R4 model so results match
    Ignixa's schema-aware engine. This is the single adapter the shared contract holds
    both the C# and Python halves accountable to.
    """
    import fhirpathpy

    results = fhirpathpy.evaluate(resource, expression, {}, _fhirpath_model())

    if shape == "boolean":
        return len(results) == 1 and isinstance(results[0], bool) and results[0]

    if shape == "scalar":
        if len(results) != 1:
            return None
        value = results[0]
        if value is None:
            return None
        if isinstance(value, bool):
            return "true" if value else "false"
        if isinstance(value, (dict, list)):
            return None
        if isinstance(value, str):
            return value
        return str(value)

    raise RuntimeError(f"Unsupported FHIRPath evaluation shape '{shape}'")


def _extract_variables(document, context):
    """Extract every document variable from its selected response.

    Mirrors the .NET evaluator: this always iterates *all* document
    variables (not only ones tied to the action that just ran), selecting
    each variable's own ``sourceId`` response (or the last response, if
    unset). A missing source response is a silent no-op. A per-variable
    extraction failure is recorded and does not stop the remaining
    variables from being attempted. Returns the list of failure messages
    (empty when every variable extracted cleanly).
    """
    failures = []
    for variable in document.get("variables", []):
        kind = variable.get("extractionKind", "none")
        if kind == "none":
            continue

        source_id = variable.get("sourceId")
        response = (
            context["responses"].get(source_id)
            if source_id is not None
            else context["last_response"]
        )
        if response is None:
            continue

        try:
            if kind == "header":
                value = _extract_by_header(response, variable["selector"])
            elif kind == "path":
                value = _extract_by_path(response, variable["selector"])
            elif kind == "fhirPath":
                value = _extract_by_fhirpath(response, variable["selector"])
            else:
                raise RuntimeError(f"Unsupported variable extraction kind '{kind}'")
        except RuntimeError as exc:
            failures.append(str(exc))
            continue

        if value is not None:
            context["variables"][variable["name"]] = value

    return failures


def _execute_operation(document, user, context, action):
    """Execute one TestScript operation action against the FHIR server.

    Builds the request (raising before any HTTP call for an undefined
    variable or unknown sourceId, converted into a single semantic
    failure), performs it (polling while ``waitFor`` requests it), records
    the canonical request/actual response objects in history, then runs
    document-wide variable extraction. The action is considered failed only
    for a transport-level failure, waitFor exhaustion, or an extraction
    failure - not merely for a received 4xx/5xx status.
    """
    action_id = action["id"]
    metric_name = _metric_name(document, action_id)

    if not action.get("encodeRequestUrl", True):
        _logger.warning(
            "%s: encodeRequestUrl=false is not supported; URL was encoded",
            metric_name,
        )

    try:
        url, headers, data = _build_request(action, context)
    except RuntimeError as exc:
        _fire_semantic_failure(user, metric_name, exc)
        return {"applicable": True, "failed": True}

    method = _derive_method(action)
    request_wrapper = {"method": method, "url": url, "headers": dict(headers), "body": data}
    _store_request(context, action.get("requestId"), request_wrapper)

    wait_for = action.get("waitFor")
    response, exhausted = _perform_request_with_polling(
        user, method, url, headers, data, metric_name, wait_for
    )

    if response is None:
        return {"applicable": True, "failed": True}

    _store_response(context, action.get("responseId"), response)

    failed = getattr(response, "error", None) is not None

    if exhausted:
        failed = True
        _fire_semantic_failure(
            user,
            metric_name,
            RuntimeError(
                f"Timed out waiting for job completion after {wait_for['maxAttempts']} attempts "
                f"(last status: {_response_status(response)})"
            ),
        )

    extraction_failures = _extract_variables(document, context)
    if extraction_failures:
        failed = True
        _fire_semantic_failure(
            user, metric_name, RuntimeError("; ".join(extraction_failures))
        )

    return {"applicable": True, "failed": failed}


def _run_autocreate(document, user, context, fixture):
    """POST a fixture's current resource to create it on the server.

    On success, the fixture's context entry is replaced with the server's
    returned JSON (never mutated in place), the response is additionally
    indexed under the fixture's own id (enabling ``sourceId``-pinned
    variable extraction and later operation sourceId lookups), and document-
    wide variable extraction runs. A non-2xx status is a native success but
    a semantic (fixture) failure; a missing resourceType or a transport
    exception fails without/with only a native event, respectively.
    """
    fixture_id = fixture["id"]
    metric_name = _metric_name(document, f"fixture.{fixture_id}.autocreate")

    resource = context["fixtures"].get(fixture_id)
    resource_type = resource.get("resourceType") if isinstance(resource, dict) else None
    if not resource_type:
        _fire_semantic_failure(
            user,
            metric_name,
            RuntimeError(f"Fixture '{fixture_id}' has no resourceType to autocreate"),
        )
        return True

    headers = _new_headers()
    auth = _parse_auth_header()
    if auth is not None:
        headers[auth[0]] = auth[1]
    headers["Content-Type"] = "application/fhir+json; charset=utf-8"
    data = json.dumps(resource, separators=(",", ":")).encode("utf-8")

    request_wrapper = {"method": "POST", "url": resource_type, "headers": dict(headers), "body": data}
    _store_request(context, None, request_wrapper)

    response = _perform_request(user, "POST", resource_type, headers, data, metric_name)
    if response is None:
        return True

    _store_response(context, fixture_id, response)

    if getattr(response, "error", None) is not None:
        return True

    body = _response_json_or_none(response)
    if body is not None:
        context["fixtures"] = dict(context["fixtures"])
        context["fixtures"][fixture_id] = body

    extraction_failures = _extract_variables(document, context)

    failed = False
    if extraction_failures:
        failed = True
        _fire_semantic_failure(
            user, metric_name, RuntimeError("; ".join(extraction_failures))
        )

    status = _response_status(response)
    if not (200 <= status < 300):
        failed = True
        _fire_semantic_failure(
            user,
            metric_name,
            RuntimeError(f"Autocreate for fixture '{fixture_id}' returned HTTP {status}"),
        )

    return failed


def _run_autodelete(document, user, context, fixture):
    """DELETE a fixture's current server-assigned resource type/id.

    Uses whatever resource is currently in context (the autocreate-replaced
    server JSON, if autocreate ran), so a missing type/id (fixture never
    created, or created without a server id) is a semantic precondition
    failure with no HTTP call. No variable extraction runs here (the
    resource is being torn down).
    """
    fixture_id = fixture["id"]
    metric_name = _metric_name(document, f"fixture.{fixture_id}.autodelete")

    resource = context["fixtures"].get(fixture_id)
    resource_type = resource.get("resourceType") if isinstance(resource, dict) else None
    resource_ref_id = resource.get("id") if isinstance(resource, dict) else None
    if not resource_type or not resource_ref_id:
        _fire_semantic_failure(
            user,
            metric_name,
            RuntimeError(
                f"Fixture '{fixture_id}' has no server-assigned type/id to autodelete"
            ),
        )
        return True

    headers = _new_headers()
    auth = _parse_auth_header()
    if auth is not None:
        headers[auth[0]] = auth[1]

    url = f"{resource_type}/{resource_ref_id}"
    request_wrapper = {"method": "DELETE", "url": url, "headers": dict(headers), "body": None}
    _store_request(context, None, request_wrapper)

    response = _perform_request(user, "DELETE", url, headers, None, metric_name)
    if response is None:
        return True

    _store_response(context, None, response)

    if getattr(response, "error", None) is not None:
        return True

    status = _response_status(response)
    if not (200 <= status < 300):
        _fire_semantic_failure(
            user,
            metric_name,
            RuntimeError(f"Autodelete for fixture '{fixture_id}' returned HTTP {status}"),
        )
        return True

    return False


def _run_autocreates(document, user, context):
    """Run autocreate for every fixture that requests it, never short-circuiting."""
    any_failed = False
    for fixture in document.get("fixtures", []):
        if fixture.get("autocreate"):
            if _run_autocreate(document, user, context, fixture):
                any_failed = True
    return any_failed


def _run_autodeletes(document, user, context):
    """Run autodelete for every fixture that requests it, never short-circuiting."""
    any_failed = False
    for fixture in document.get("fixtures", []):
        if fixture.get("autodelete"):
            if _run_autodelete(document, user, context, fixture):
                any_failed = True
    return any_failed


def _matches_response_code(category, status_code):
    """Map a response-category token to a status-code predicate (parity with .NET)."""
    if category == "okay":
        return 200 <= status_code < 300
    return status_code == {
        "created": 201,
        "noContent": 204,
        "notModified": 304,
        "bad": 400,
        "forbidden": 403,
        "notFound": 404,
        "methodNotAllowed": 405,
        "conflict": 409,
        "gone": 410,
        "preconditionFailed": 412,
        "unprocessable": 422,
    }.get(category)


def _media_type_of(header_value):
    """Return the bare media type (drop any ``;``-delimited parameters)."""
    if header_value is None:
        return None
    return header_value.split(";", 1)[0].strip()


# Grammar for .NET ``NumberStyles.Number`` under the invariant culture: an optional leading OR
# trailing sign, an integer part that must start with a digit and may then interleave digits with
# invariant group separators (``,``) with no fixed group size (matching the BCL's lenient grouping,
# so ``1,00`` and ``12,34,567`` are accepted while a leading/fractional comma is not), and an
# optional invariant decimal point with fractional digits. Exponents, hex, and non-finite tokens do
# not match, so they fall back to ordinal comparison.
_INVARIANT_NUMBER_RE = re.compile(
    r"^(?P<lead>[+-])?(?P<int>\d[\d,]*)?(?:\.(?P<frac>\d*))?(?P<trail>[+-])?$"
)

# .NET ``System.Decimal`` is a 96-bit unsigned significand scaled by ``10**-scale`` with a scale of
# 0..28. Parsing rounds the exact value half-to-even to fit that representation (reducing the scale,
# i.e. dropping fractional precision, as needed) and rejects any value whose integer magnitude cannot
# fit the significand even at scale 0. These bounds reproduce that model exactly.
_NET_DECIMAL_MAX_SIGNIFICAND = decimal.Decimal("79228162514264337593543950335")
_NET_DECIMAL_MAX_SCALE = 28


def _to_net_decimal(exact):
    """Round/range an exact ``Decimal`` to the nearest representable .NET ``System.Decimal``.

    Emulates the rounding/range behaviour of
    ``decimal.TryParse(value, NumberStyles.Number, InvariantCulture)``: the value is rounded
    half-to-even to fit a 96-bit significand with a scale of 0..28, preferring to keep as much
    fractional precision as the significand allows. A value whose integer magnitude cannot fit even
    at scale 0 is out of range and yields ``None`` (ordinal fallback). A fresh local context is used
    for every operation, so the process-wide decimal context is never mutated (important because the
    Locust runtime shares an interpreter across users).
    """
    if exact.is_zero():
        return exact

    negative = exact < 0
    magnitude = exact.copy_abs()
    context = decimal.Context(prec=60, rounding=decimal.ROUND_HALF_EVEN)

    # Start at the value's own fractional length (capped at 28) so exact-fitting values keep their
    # natural scale; only shed fractional digits when the significand would overflow.
    scale = min(_NET_DECIMAL_MAX_SCALE, max(0, -magnitude.as_tuple().exponent))
    unit = decimal.Decimal(1)
    while scale >= 0:
        try:
            rounded = magnitude.quantize(unit.scaleb(-scale, context), context=context)
            significand = rounded.scaleb(scale, context).to_integral_value(context=context)
        except decimal.InvalidOperation:
            # Result exceeds the working precision -> integer magnitude too large at this scale.
            scale -= 1
            continue
        if significand <= _NET_DECIMAL_MAX_SIGNIFICAND:
            return rounded.copy_negate() if negative else rounded
        scale -= 1

    return None


def _try_decimal(value):
    """Parse ``value`` as an invariant decimal, or ``None`` if it is not numeric.

    Mirrors .NET ``decimal.TryParse(value, NumberStyles.Number, InvariantCulture)`` rather
    than Python ``Decimal``'s broader grammar: leading/trailing whitespace, a single leading
    *or* trailing sign, an invariant decimal point (``.``), and invariant thousands group
    separators (``,``) between integer digits (BCL grouping leniency: no fixed group size, and
    trailing/consecutive commas are tolerated, but a leading comma or a comma in the fractional
    part is rejected) are accepted; exponent notation (``E``), hexadecimal, and the non-finite
    tokens ``NaN``/``Infinity`` are rejected. Accepted values are rounded/ranged to the nearest
    representable .NET ``System.Decimal`` (half-to-even, 96-bit significand, scale 0..28); values
    outside that range return ``None``. A rejected value returns ``None`` so
    :func:`_compare_ordered` falls back to ordinal string comparison.
    """
    if value is None:
        return None
    text = value.strip()
    if not text:
        return None

    match = _INVARIANT_NUMBER_RE.match(text)
    if match is None:
        return None

    # NumberStyles.Number permits a leading OR trailing sign, never both.
    if match.group("lead") and match.group("trail"):
        return None

    integer_digits = (match.group("int") or "").replace(",", "")
    fraction_digits = match.group("frac")
    # At least one digit must be present (rejects "", "-", ".", "+.", ",").
    if not integer_digits and not fraction_digits:
        return None

    sign = match.group("lead") or match.group("trail") or ""
    if sign == "+":
        sign = ""
    normalized = f"{sign}{integer_digits}"
    if fraction_digits:
        normalized += f".{fraction_digits}"

    try:
        exact = decimal.Decimal(normalized)
    except (ArithmeticError, ValueError):
        return None
    return _to_net_decimal(exact)


def _compare_ordered(actual, expected):
    """Compare two strings numerically when both parse as decimals, else ordinally.

    Mirrors .NET ``CompareOrdered``: invariant-decimal comparison is attempted first;
    on any non-numeric operand it falls back to ordinal (codepoint) string comparison,
    treating ``None`` as less than any string (as .NET ``string.Compare`` treats null).
    """
    da = _try_decimal(actual)
    de = _try_decimal(expected)
    if da is not None and de is not None:
        if da < de:
            return -1
        if da > de:
            return 1
        return 0

    if actual == expected:
        return 0
    if actual is None:
        return -1
    if expected is None:
        return 1
    return -1 if actual < expected else 1


def _evaluate_with_operator(actual, expected, operator):
    """Apply one of the ten assert operators (parity with .NET ``EvaluateWithOperator``)."""
    if operator == "Equals":
        return actual == expected
    if operator == "NotEquals":
        return actual != expected
    if operator == "Contains":
        return (expected or "") in actual if actual is not None else False
    if operator == "NotContains":
        return not ((expected or "") in actual if actual is not None else False)
    if operator == "In":
        return actual in [s.strip() for s in expected.split(",")] if expected is not None else False
    if operator == "NotIn":
        return not (actual in [s.strip() for s in expected.split(",")] if expected is not None else False)
    if operator == "Empty":
        return actual is None or actual == ""
    if operator == "NotEmpty":
        return not (actual is None or actual == "")
    if operator == "GreaterThan":
        return _compare_ordered(actual, expected) > 0
    if operator == "LessThan":
        return _compare_ordered(actual, expected) < 0
    raise RuntimeError(f"Unhandled assert operator '{operator}'")


def _resolve_assertion_response(action, context):
    """Direction-aware response resolution (parity with .NET ``ResolveAssertionResponse``).

    A request-direction assertion has no response. An absent ``sourceId`` selects the
    last response; an explicit ``sourceId`` selects that response or raises when unknown.
    """
    if action.get("direction") == "request":
        return None
    source_id = action.get("sourceId")
    if source_id is None:
        return context["last_response"]
    if source_id in context["responses"]:
        return context["responses"][source_id]
    raise RuntimeError(f"Assertion sourceId '{source_id}' refers to no known response")


def _resolve_assertion_request(action, context):
    """Resolve the request a request-direction assertion targets (parity with .NET)."""
    source_id = action.get("sourceId")
    if source_id is None:
        return context["last_request"]
    if source_id in context["requests"]:
        return context["requests"][source_id]
    raise RuntimeError(f"Assertion sourceId '{source_id}' refers to no known request")


def _request_body_json(request):
    """Parse a stored request wrapper's body bytes into JSON, or ``None``."""
    if request is None:
        return None
    body = request.get("body")
    if body is None:
        return None
    try:
        if isinstance(body, (bytes, bytearray)):
            return json.loads(bytes(body).decode("utf-8"))
        if isinstance(body, str):
            return json.loads(body)
    except (ValueError, UnicodeDecodeError):
        return None
    if isinstance(body, dict):
        return body
    return None


def _assertion_body_with_parse_error(action, context):
    """Resolve the assertion body, capturing a response JSON parse error if any.

    Returns ``(body_or_none, parse_error_or_none)``. For a request-direction assertion
    the body is the resolved request's parsed body. For a response-direction assertion
    the response's ``json()`` is used; a ``ValueError`` from an unparseable body is
    captured as the parse reason so it can be surfaced in the failure message.
    """
    if action.get("direction") == "request":
        request = _resolve_assertion_request(action, context)
        return _request_body_json(request), None
    response = _resolve_assertion_response(action, context)
    if response is None:
        return None, None
    try:
        return response.json(), None
    except ValueError as exc:
        return None, str(exc)


def _no_body_message(parse_error):
    if parse_error is not None:
        return f"Response body was not valid JSON: {parse_error}"
    return "No response body available to assert against with FHIRPath"


def _evaluate_assertion_criteria(action, context):
    """Evaluate one assertion's criteria, returning ``(passed, message)``.

    Matches ``TestScriptEvaluator`` exactly for every criteria kind. May raise for an
    unknown ``sourceId`` (an evaluation error the caller converts into a returned
    failure); it never raises for a merely-failing assertion.
    """
    criteria = action["criteria"]
    kind = criteria["kind"]

    if kind in ("responseStatus", "responseCode", "contentType", "header"):
        response = _resolve_assertion_response(action, context)
        if response is None:
            return (False, "No response available to assert against")

        if kind == "responseStatus":
            status = _response_status(response)
            matched = _matches_response_code(criteria["value"], status)
            return (matched, None if matched else f"Expected response '{criteria['value']}' but got status {status}")

        if kind == "responseCode":
            status = _response_status(response)
            passed = str(status) == criteria["value"]
            return (passed, None if passed else f"Expected responseCode '{criteria['value']}' but got {status}")

        if kind == "contentType":
            actual = _response_headers(response).get("Content-Type")
            passed = _media_type_equal(actual, criteria["value"])
            return (passed, None if passed else f"Expected content type '{criteria['value']}' but got '{actual}'")

        # header
        field = criteria["field"]
        actual = _response_headers(response).get(field)
        operator = criteria.get("operator") or ("NotEmpty" if criteria.get("value") is None else "Equals")
        passed = _evaluate_with_operator(actual, criteria.get("value"), operator)
        return (passed, None if passed else f"Header '{field}' value '{actual}' did not match expected '{criteria.get('value')}' with operator {operator}")

    if kind == "resourceType":
        response = _resolve_assertion_response(action, context)
        if response is None:
            return (False, "No response available to assert against")
        body, parse_error = _assertion_body_with_parse_error(action, context)
        if body is None:
            return (False, _no_body_message(parse_error) if parse_error is not None else "No response body available to assert against")
        actual = body.get("resourceType") if isinstance(body, dict) else None
        passed = actual == criteria["value"]
        return (passed, None if passed else f"Expected resource type '{criteria['value']}' but got '{actual}'")

    if kind == "fhirPath":
        body, parse_error = _assertion_body_with_parse_error(action, context)
        if body is None:
            return (False, _no_body_message(parse_error))
        expression = _resolve(criteria["expression"], context)
        result = _evaluate_fhirpath(expression, body, "boolean")
        return (result, None if result else f"FHIRPath expression '{expression}' did not evaluate to true")

    if kind == "fhirPathValue":
        body, parse_error = _assertion_body_with_parse_error(action, context)
        if body is None:
            return (False, _no_body_message(parse_error))
        expression = _resolve(criteria["expression"], context)
        expected = _resolve(criteria.get("value") or "", context)
        actual = _evaluate_fhirpath(expression, body, "scalar")
        passed = _evaluate_with_operator(actual, expected, criteria["operator"])
        return (passed, None if passed else f"FHIRPath expression '{expression}' value '{actual}' did not match expected '{expected}' with operator {criteria['operator']}")

    if kind == "requestMethod":
        request = _resolve_assertion_request(action, context)
        if request is None:
            return (False, "No request available to assert against")
        actual = request["method"]
        passed = actual.lower() == (criteria["value"] or "").lower()
        return (passed, None if passed else f"Expected request method '{criteria['value']}' but was '{actual}'")

    if kind == "requestUrl":
        request = _resolve_assertion_request(action, context)
        if request is None:
            return (False, "No request available to assert against")
        actual = request["url"]
        operator = criteria.get("operator") or "Equals"
        passed = _evaluate_with_operator(actual, criteria["value"], operator)
        return (passed, None if passed else f"Expected request URL '{criteria['value']}' but was '{actual}'")

    raise RuntimeError(f"Unsupported assertion criteria kind '{kind}'")


def _media_type_equal(actual, expected):
    """Case-insensitive media-type equality, treating two ``None`` values as equal."""
    a = _media_type_of(actual)
    b = _media_type_of(expected)
    if a is None and b is None:
        return True
    if a is None or b is None:
        return False
    return a.lower() == b.lower()


def _evaluate_assertion_member(action, context):
    """Evaluate one assertion, returning ``(applicable, failed, message, is_error)``.

    Applies the ``assertionWhenResponseStatus`` gate first (an unmatched gate is
    inapplicable, not failed). Any evaluation error - including an unknown ``sourceId``
    or a malformed FHIRPath expression - is caught and converted into a returned failure
    marked ``is_error``: assertions never propagate an uncaught exception, so teardown
    can never mask an earlier failure. The broad catch is logged to stay non-silent and
    mirrors .NET ``EvaluateGroupMemberSafe``/``VisitAssert``.
    """
    try:
        condition_source_id = action.get("whenResponseSourceId")
        if condition_source_id is not None:
            if condition_source_id not in context["responses"]:
                raise RuntimeError(
                    f"assertionWhenResponseStatus sourceId '{condition_source_id}' refers to no known response"
                )
            statuses = action.get("whenResponseStatuses") or []
            if _response_status(context["responses"][condition_source_id]) not in statuses:
                return (False, False, None, False)

        passed, message = _evaluate_assertion_criteria(action, context)
        return (True, not passed, message, False)
    except Exception as exc:  # noqa: BLE001 - parity with .NET; returned as failure, logged.
        _logger.warning("Assertion '%s' failed to evaluate: %s", action.get("id"), exc)
        return (True, True, str(exc), True)


def _fire_assertion_event(user, metric_name, exception):
    """Fire exactly one ``TESTSCRIPT_ASSERT`` semantic event.

    The event carries ``context={"source": metric_name}`` (never a ``response``), a
    zero response time/length, and either ``None`` (pass) or the assertion exception
    (fail).
    """
    user.environment.events.request.fire(
        request_type="TESTSCRIPT_ASSERT",
        name=metric_name,
        response_time=0,
        response_length=0,
        exception=exception,
        context={"source": metric_name},
    )


def _execute_assertion(document, user, context, action):
    """Execute one non-grouped TestScript assertion action.

    A compiled assertion always carries criteria; its absence is an interpreter defect
    surfaced before any metric/event work. A passing, applicable assertion fires one
    event with no exception; a failing, applicable, non-warning-only assertion fires one
    event carrying an ``AssertionError``; a failing ``warningOnly`` assertion logs the
    source-qualified metric and message and fires no event; an inapplicable assertion
    fires no event. Outcomes are always returned, never raised.
    """
    if action.get("criteria") is None:
        raise RuntimeError(
            f"TestScript assertion '{action.get('id')}' has no criteria to evaluate"
        )

    metric_name = _metric_name(document, action["id"])
    applicable, failed, message, _is_error = _evaluate_assertion_member(action, context)

    if not applicable:
        return {"applicable": False, "failed": False}

    if not failed:
        _fire_assertion_event(user, metric_name, None)
        return {"applicable": True, "failed": False}

    if action.get("warningOnly", False):
        _logger.warning("%s: assertion failed (warningOnly): %s", metric_name, message)
        return {"applicable": True, "failed": True}

    _fire_assertion_event(user, metric_name, AssertionError(message))
    return {"applicable": True, "failed": True}


def _record_group_result(document, user, group_id, members):
    """Aggregate a buffered any-of group into one outcome + one event.

    ``members`` is a list of ``(action, (applicable, failed, message, is_error))`` in
    document order. Mirrors .NET ``RecordGroupResult``: any member that errored fails the
    group; no applicable member fails the group (condition never matched); otherwise the
    group passes iff at least one applicable member passed. Exactly one
    ``TESTSCRIPT_ASSERT`` event is fired, always named for the first member's metric.
    """
    first_action = members[0][0]
    metric_name = _metric_name(document, first_action["id"])

    errored = next((m for m in members if m[1][3]), None)
    applicable_members = [m for m in members if m[1][0]]
    matched = next((m for m in applicable_members if not m[1][1]), None)

    if errored is not None:
        message = (
            f"assertionAnyOfGroup '{group_id}': member '{errored[0].get('id')}' "
            f"failed to evaluate: {errored[1][2]}"
        )
        failed = True
    elif not applicable_members:
        message = (
            f"assertionAnyOfGroup '{group_id}': no member was applicable - "
            "condition(s) never matched"
        )
        failed = True
    elif matched is not None:
        message = None
        failed = False
    else:
        summary = "; ".join(f"{m[0].get('id')}: {m[1][2]}" for m in applicable_members)
        message = f"assertionAnyOfGroup '{group_id}': no alternative matched ({summary})"
        failed = True

    _fire_assertion_event(user, metric_name, AssertionError(message) if failed else None)
    return {"applicable": True, "failed": failed}


def _execute_action(document, user, context, action):
    """Dispatch a single action to its operation/assertion executor.

    Returns a result dictionary exposing at least ``applicable`` and
    ``failed`` booleans, matching the protocol locked by the lifecycle
    tests. Tests replace this function directly (module-attribute
    assignment) with deterministic fakes; production callers always reach
    this dispatcher.
    """
    kind = action.get("kind")
    if kind == "operation":
        return _execute_operation(document, user, context, action)
    if kind == "assert":
        return _execute_assertion(document, user, context, action)
    raise RuntimeError(f"Unsupported TestScript action kind '{kind}'")


def _run_phase(document, user, context, actions):
    """Run every action in a phase, aggregating phase failure separately.

    Every action always runs, even after an earlier one fails. Assertions that belong
    to an ``anyOfGroupId`` are evaluated (with no per-member event) and buffered; at the
    group's last member the buffered results are aggregated into exactly one event and
    one outcome. A phase is marked failed only by a failed operation, a failed applicable
    non-warning-only assertion, or a failed any-of group; inapplicable assertions and
    failed warning-only assertions never fail the phase.
    """
    last_group_index = {}
    for index, action in enumerate(actions):
        if action.get("kind") == "assert":
            group_id = action.get("anyOfGroupId")
            if group_id is not None:
                last_group_index[group_id] = index

    phase_failed = False
    results = []
    pending_groups = {}

    for index, action in enumerate(actions):
        group_id = action.get("anyOfGroupId") if action.get("kind") == "assert" else None

        if group_id is not None:
            member = _evaluate_assertion_member(action, context)
            pending_groups.setdefault(group_id, []).append((action, member))
            if index == last_group_index[group_id]:
                group_result = _record_group_result(document, user, group_id, pending_groups[group_id])
                results.append(group_result)
                if group_result["failed"]:
                    phase_failed = True
            continue

        result = _execute_action(document, user, context, action)
        results.append(result)
        if not result.get("applicable", True):
            continue
        if not result.get("failed", False):
            continue
        if action.get("kind") == "assert" and action.get("warningOnly", False):
            continue
        phase_failed = True
    return phase_failed, results


def _skipped_test_outcome(test):
    return {"id": test["id"], "skipped": True, "failed": False, "results": []}


def execute(document, user, state):
    """Execute one complete, isolated TestScript invocation.

    Each call increments the per-user iteration counter once and allocates a
    brand-new execution context; Locust may invoke this repeatedly for the
    same virtual user, and concurrently for other virtual users, without any
    execution ever observing another's mutations.

    Normal TestScript pass/fail/skip outcomes are reported through the
    returned outcome dictionary rather than exceptions. Exceptions are
    reserved for invalid runtime configuration (for example an empty fixture
    variant pool) and unrecoverable interpreter defects.
    """
    state["iteration"] += 1
    iteration = state["iteration"]
    ordinal = state["ordinal"]

    outcome = {
        "suite_skipped": False,
        "setup_failed": False,
        "setup_results": [],
        "tests": [],
        "teardown_ran": False,
        "teardown_failed": False,
        "teardown_results": [],
        "failed": False,
        "context": None,
    }

    if not _suite_allowed():
        # The suite capability gate rejects before any fixture materialization,
        # setup, test, or teardown work occurs.
        outcome["suite_skipped"] = True
        outcome["tests"] = [
            _skipped_test_outcome(test) for test in document.get("tests", [])
        ]
        return outcome

    context = _new_context(document, state)
    outcome["context"] = context

    try:
        _materialize_fixtures(document, context, ordinal, iteration)

        autocreate_failed = _run_autocreates(document, user, context)

        explicit_setup_failed, setup_results = _run_phase(
            document, user, context, document.get("setup", [])
        )
        setup_failed = autocreate_failed or explicit_setup_failed
        outcome["setup_failed"] = setup_failed
        outcome["setup_results"] = setup_results

        for test in document.get("tests", []):
            if setup_failed or not _test_allowed(test["id"]):
                outcome["tests"].append(_skipped_test_outcome(test))
                continue

            discard = test.get("discardContextAfterExecution", False)
            test_context = _clone_test_context(context) if discard else context
            _apply_initial_variables(test_context, test.get("initialVariables", {}))

            test_failed, test_results = _run_phase(
                document, user, test_context, test.get("actions", [])
            )
            outcome["tests"].append(
                {
                    "id": test["id"],
                    "skipped": False,
                    "failed": test_failed,
                    "results": test_results,
                }
            )
            # A discarded clone's mutations are simply never written back to
            # ``context``; nothing further is required to discard them.
    finally:
        # Teardown always runs after fixture/setup/test work completes, for
        # both normal and exceptional paths, because the suite gate above is
        # the only short-circuit that returns before this point. Autodelete
        # runs before the explicit teardown actions, mirroring autocreate's
        # placement ahead of explicit setup.
        autodelete_failed = _run_autodeletes(document, user, context)
        explicit_teardown_failed, teardown_results = _run_phase(
            document, user, context, document.get("teardown", [])
        )
        outcome["teardown_ran"] = True
        outcome["teardown_failed"] = autodelete_failed or explicit_teardown_failed
        outcome["teardown_results"] = teardown_results

    outcome["failed"] = (
        outcome["setup_failed"]
        or any(test["failed"] for test in outcome["tests"])
        or outcome["teardown_failed"]
    )

    return outcome

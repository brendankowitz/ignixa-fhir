import copy
import hashlib
import itertools
import json
import logging
import os
import re
import socket


SUPPORTED_SCHEMA_MAJOR = 1

_USER_ORDINALS = itertools.count()

_logger = logging.getLogger("ignixa.testscript")

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


def _execute_assertion(document, user, context, action):
    """Placeholder assertion executor filled in by Task 9."""
    raise RuntimeError(
        "TestScript assertion execution is not implemented yet "
        f"(action '{action.get('id')}')"
    )


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

    Every action always runs, even after an earlier one fails. A phase is
    marked failed only by a failed operation or a failed, applicable,
    non-warning-only assertion; inapplicable assertions and failed
    warning-only assertions never fail the phase.
    """
    phase_failed = False
    results = []
    for action in actions:
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

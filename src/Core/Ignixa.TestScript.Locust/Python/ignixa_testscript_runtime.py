import copy
import hashlib
import itertools
import json
import logging
import os
import re
import socket


SUPPORTED_SCHEMA_MAJOR = 1

logger = logging.getLogger("ignixa.testscript")

_VARIABLE_PATTERN = re.compile(r"\$\{([^}]+)\}")

_USER_ORDINALS = itertools.count()

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
    """Substitute ``${name}`` placeholders in ``template`` from ``context['variables']``.

    Raises ``RuntimeError`` for any referenced variable that is not defined,
    matching ``VariableResolver``'s behavior in the C# evaluator.
    """
    if template is None:
        return None

    def replace(match):
        name = match.group(1)
        if name not in context["variables"]:
            raise RuntimeError(f"Variable '{name}' is not defined")
        return str(context["variables"][name])

    return _VARIABLE_PATTERN.sub(replace, template)


def _derive_url(operation, context):
    """Derive the relative request URL exactly as ``TestScriptEvaluator.BuildUrl`` does."""
    if operation.get("url") is not None:
        return _resolve(operation["url"], context)
    resource = operation.get("resource") or ""
    if operation["type"] == "search" and operation["method"] == "POST":
        return f"{resource}/_search"
    params = _resolve(operation.get("params"), context) or ""
    if operation["type"].startswith("$"):
        path = operation["type"] if not resource else f"{resource}/{operation['type']}"
        return f"{path}{params}"
    return f"{resource}{params}"


def _metric_name(document, action_id):
    """Build the source-qualified Locust metric name shared by HTTP and semantic events."""
    return f"{document['metadata']['source']}::{action_id}"


def _parse_auth_header():
    """Parse ``IGNIXA_AUTH_HEADER`` as exactly one ``Name: value`` pair.

    Returns ``None`` when the environment variable is unset or empty. Uses
    ``str.partition(":")`` so the value may itself contain colons (only the
    first colon separates name from value). Raises ``RuntimeError`` for a
    missing colon, an empty name, or an empty value.
    """
    raw = os.getenv("IGNIXA_AUTH_HEADER")
    if not raw:
        return None

    name, sep, value = raw.partition(":")
    if not sep:
        raise RuntimeError(
            "IGNIXA_AUTH_HEADER must be a 'Name: value' pair; no ':' separator was found"
        )

    name = name.strip()
    value = value.strip()
    if not name:
        raise RuntimeError("IGNIXA_AUTH_HEADER header name must not be empty")
    if not value:
        raise RuntimeError("IGNIXA_AUTH_HEADER header value must not be empty")

    return name, value


def _build_headers(operation, context):
    """Build request headers in evaluator precedence order.

    Order: the configured auth pair first, then the operation's ``Accept``/
    ``Content-Type`` IR properties, then the operation's resolved custom
    headers last -- so an explicit TestScript header wins case-insensitively
    over both the auth pair and the IR properties. Content handling
    (``_finalize_content``) may further adjust ``Content-Type`` afterwards.
    """
    from requests.structures import CaseInsensitiveDict

    headers = CaseInsensitiveDict()

    auth = _parse_auth_header()
    if auth is not None:
        headers[auth[0]] = auth[1]

    if operation.get("accept") is not None:
        headers["Accept"] = operation["accept"]
    if operation.get("contentType") is not None:
        headers["Content-Type"] = operation["contentType"]

    for header in operation.get("headers", []):
        field = _resolve(header["field"], context)
        value = _resolve(header["value"], context)
        headers[field] = value

    return headers


def _resolve_body(operation, context):
    """Resolve the operation's body source, matching ``TestScriptEvaluator.BuildRequest``.

    Returns a ``(json_body, form_body)`` tuple where exactly one element is
    non-``None`` (or both are ``None`` for a bodyless request). Raises
    ``RuntimeError`` when ``sourceId`` refers to neither a known fixture nor a
    known prior response.
    """
    is_form_search = operation["type"] == "search" and operation["method"] == "POST"
    if is_form_search:
        raw_params = _resolve(operation.get("params"), context)
        return None, (raw_params or "").lstrip("?")

    source_id = operation.get("sourceId")
    if source_id is None:
        return None, None
    if source_id in context["fixtures"]:
        return context["fixtures"][source_id], None
    if source_id in context["responses"]:
        return context["responses"][source_id].get("json"), None

    raise RuntimeError(f"sourceId '{source_id}' refers to no known fixture or response")


def _finalize_content(headers, json_body, form_body):
    """Apply `HttpTestRequestProvider`'s exact content-type/body rules.

    Mutates ``headers`` in place (never a shared/cached instance -- callers
    always pass a freshly built ``CaseInsensitiveDict``) and returns the
    ``(body_bytes, json_repr)`` pair to send and store.
    """
    if form_body is not None:
        headers["Content-Type"] = "application/x-www-form-urlencoded; charset=utf-8"
        return form_body.encode("utf-8"), None

    if json_body is not None:
        if "Content-Type" not in headers:
            headers["Content-Type"] = "application/fhir+json; charset=utf-8"
        body_bytes = json.dumps(json_body, separators=(",", ":")).encode("utf-8")
        return body_bytes, json_body

    if "Content-Type" in headers:
        del headers["Content-Type"]
    return None, None


def _wrap_response(response):
    """Build the canonical response wrapper stored in context/history.

    Malformed/non-JSON bodies are treated as "no body" here; Task 9 surfaces
    parse errors through assertions rather than this wrapper.
    """
    content = response.content or b""
    text = response.text or ""
    json_value = None
    if text.strip():
        try:
            json_value = json.loads(text)
        except ValueError:
            json_value = None

    return {
        "status_code": response.status_code,
        "headers": response.headers,
        "content": content,
        "text": text,
        "json": json_value,
        "raw": response,
    }


def _send_request(user, metric_name, method, url, headers, body_bytes):
    """Send one real HTTP attempt through the Locust user client.

    Every received response -- including 4xx/5xx -- calls ``response.success()``
    so TestScript assertions, not Locust, determine conformance. Returns the
    response wrapper on any received response, or ``None`` for a transport
    failure (a returned Locust error response or a raised exception), which
    Locust already reports as a native failed HTTP event.

    Only the client call itself is guarded broadly: real Locust's
    ``catch_response=True`` requests never raise for ordinary network errors
    (those come back as an errored response instead), but a small set of
    configuration errors (for example an invalid URL scheme) can still raise
    directly from ``request(...)``. Everything after the context manager is
    entered -- the error check, ``success()``, and building the response
    wrapper -- runs unguarded, so a genuine defect there surfaces as a real
    exception instead of being silently reported as a transport failure.
    """
    kwargs = {"name": metric_name, "catch_response": True, "headers": dict(headers)}
    if body_bytes is not None:
        kwargs["data"] = body_bytes

    try:
        response_context = user.client.request(method, url, **kwargs)
    except Exception:
        return None

    with response_context as response:
        if getattr(response, "error", None) is not None:
            return None
        response.success()
        wrapper = _wrap_response(response)
    return wrapper


def _record_semantic_failure(user, metric_name, message):
    """Fire a failed ``TESTSCRIPT_OPERATION`` semantic event with zero timing/size."""
    user.environment.events.request.fire(
        request_type="TESTSCRIPT_OPERATION",
        name=metric_name,
        response_time=0,
        response_length=0,
        exception=RuntimeError(message),
        context={},
    )


def _store_response(context, response_id, response_wrapper):
    """Replace last-response and, if present, the response-history entry.

    Only top-level dictionary entries are ever assigned -- consistent with
    the Task 7 shallow-clone contract, no nested wrapper is ever mutated in
    place.
    """
    if response_id:
        context["responses"][response_id] = response_wrapper
    context["last_response"] = response_wrapper


def _extract_path(body, path):
    """Traverse dotted-path ``body`` through object keys only (never array indices)."""
    if not isinstance(body, dict):
        return None

    current = body
    for part in path.split("."):
        if not part:
            continue
        if isinstance(current, dict):
            current = current.get(part)
        else:
            return None

    if current is None:
        return None
    if isinstance(current, str):
        return current
    # Numeric/boolean leaves and terminal object/array values all serialize as
    # compact JSON text (``3``, ``true``, ``{"a":1}``), matching
    # ``JsonValue.ToJsonString()``/``json.dumps(..., separators=(",", ":"))``.
    return json.dumps(current, separators=(",", ":"))


def _extract_fhirpath(body, expression):
    """Evaluate a scalar FHIRPath extraction expression against a JSON body.

    Empty and multi-value results are a no-op. A single boolean value
    lower-cases to ``true``/``false``; any other single value stringifies.
    Malformed expressions raise ``RuntimeError`` so the caller can record one
    semantic operation failure and continue extracting later variables.
    """
    if body is None:
        return None

    import fhirpathpy

    try:
        result = fhirpathpy.evaluate(body, expression)
    except Exception as ex:
        raise RuntimeError(f"FHIRPath extraction expression '{expression}' failed: {ex}") from ex

    if not result or len(result) > 1:
        return None

    value = result[0]
    if value is None:
        return None
    if isinstance(value, bool):
        return "true" if value else "false"
    return str(value)


def _extract_value(kind, selector, response):
    if kind == "header":
        return response["headers"].get(selector)
    if kind == "path":
        return _extract_path(response.get("json"), selector)
    if kind == "fhirPath":
        return _extract_fhirpath(response.get("json"), selector)
    raise RuntimeError(f"Unsupported variable extraction kind '{kind}'")


def _extract_variables(document, context, user, metric_name):
    """Evaluate every emitted IR variable against current response history.

    A variable with ``sourceId`` reads that response-history entry; otherwise
    it reads ``last_response``. A missing response, missing selector, or
    ``extractionKind == "none"`` is a no-op. A malformed FHIRPath expression
    records one semantic failure under ``metric_name`` (the current action's
    metric) and extraction continues for later variables. Returns ``True``
    when any extraction failed, so the enclosing operation result can be
    marked failed.
    """
    any_failed = False
    for variable in document.get("variables", []):
        kind = variable.get("extractionKind", "none")
        if kind == "none":
            continue
        selector = variable.get("selector")
        if not selector:
            continue

        source_id = variable.get("sourceId")
        response = context["responses"].get(source_id) if source_id else context["last_response"]
        if response is None:
            continue

        try:
            value = _extract_value(kind, selector, response)
        except RuntimeError as ex:
            _record_semantic_failure(user, metric_name, str(ex))
            any_failed = True
            continue

        if value is not None:
            context["variables"][variable["name"]] = value

    return any_failed


def _execute_operation(document, user, context, action):
    """Execute one TestScript operation action: request, poll, store, extract.

    Semantic failures (undefined variables, an unknown ``sourceId``) are
    detected before any HTTP attempt and reported as exactly one
    ``TESTSCRIPT_OPERATION`` event with no native HTTP request. Transport
    failures (a raised exception or a returned Locust error response) are
    left as native failed HTTP events with no duplicate semantic event.
    Every received response -- 2xx through 5xx -- is an ordinary operation
    success; only ``waitFor`` exhaustion is a semantic failure despite a
    received response.
    """
    metric_name = _metric_name(document, action["id"])

    if not action.get("encodeRequestUrl", True):
        logger.warning(
            "%s: encodeRequestUrl=false is not supported; URL was encoded", metric_name
        )

    try:
        method = action["method"]
        url = _derive_url(action, context)
        json_body, form_body = _resolve_body(action, context)
        headers = _build_headers(action, context)
    except RuntimeError as ex:
        _record_semantic_failure(user, metric_name, str(ex))
        return {"applicable": True, "failed": True}

    body_bytes, request_json_repr = _finalize_content(headers, json_body, form_body)

    request_wrapper = {
        "method": method,
        "url": url,
        "headers": dict(headers),
        "body": request_json_repr,
        "form_body": form_body,
    }
    request_id = action.get("requestId")
    if request_id:
        context["requests"][request_id] = request_wrapper
    context["last_request"] = request_wrapper

    response_wrapper = _send_request(user, metric_name, method, url, headers, body_bytes)
    if response_wrapper is None:
        return {"applicable": True, "failed": True}

    wait_for = action.get("waitFor")
    attempts = 1
    if wait_for is not None:
        import gevent

        while (
            response_wrapper["status_code"] == wait_for["pollingStatusCode"]
            and attempts < wait_for["maxAttempts"]
        ):
            gevent.sleep(wait_for["intervalMs"] / 1000.0)
            response_wrapper = _send_request(user, metric_name, method, url, headers, body_bytes)
            if response_wrapper is None:
                return {"applicable": True, "failed": True}
            attempts += 1

    _store_response(context, action.get("responseId"), response_wrapper)
    extraction_failed = _extract_variables(document, context, user, metric_name)

    if wait_for is not None and response_wrapper["status_code"] == wait_for["pollingStatusCode"]:
        _record_semantic_failure(
            user,
            metric_name,
            f"Timed out waiting for job completion after {attempts} attempts "
            f"(last status: {response_wrapper['status_code']})",
        )
        return {"applicable": True, "failed": True}

    return {"applicable": True, "failed": extraction_failed}


def _autocreate_fixture(document, user, context, fixture):
    """POST a materialized fixture resource to its resource type.

    Requires a materialized dict with a non-empty ``resourceType``. Any
    received response is a native HTTP success; only 2xx is semantic
    lifecycle success. A valid JSON response body replaces the context
    fixture. The response is always stored under the fixture ID and as
    ``last_response``, and variable extraction always runs afterward.
    """
    fixture_id = fixture["id"]
    metric_name = _metric_name(document, f"fixture.{fixture_id}.autocreate")

    resource = context["fixtures"].get(fixture_id)
    if not isinstance(resource, dict) or not resource.get("resourceType"):
        _record_semantic_failure(
            user, metric_name, f"Fixture '{fixture_id}' has no resourceType; cannot autocreate"
        )
        return {"applicable": True, "failed": True}

    resource_type = resource["resourceType"]
    body_bytes = json.dumps(resource, separators=(",", ":")).encode("utf-8")

    from requests.structures import CaseInsensitiveDict

    headers = CaseInsensitiveDict({"Content-Type": "application/fhir+json; charset=utf-8"})

    context["last_request"] = {
        "method": "POST",
        "url": resource_type,
        "headers": dict(headers),
        "body": resource,
        "form_body": None,
    }

    response_wrapper = _send_request(user, metric_name, "POST", resource_type, headers, body_bytes)
    if response_wrapper is None:
        return {"applicable": True, "failed": True}

    if isinstance(response_wrapper.get("json"), dict):
        context["fixtures"][fixture_id] = response_wrapper["json"]

    context["responses"][fixture_id] = response_wrapper
    context["last_response"] = response_wrapper
    extraction_failed = _extract_variables(document, context, user, metric_name)

    if not (200 <= response_wrapper["status_code"] < 300):
        _record_semantic_failure(
            user, metric_name, f"Autocreate returned HTTP {response_wrapper['status_code']}"
        )
        return {"applicable": True, "failed": True}

    return {"applicable": True, "failed": extraction_failed}


def _autodelete_fixture(document, user, context, fixture):
    """DELETE the fixture's server-assigned resource, using the current fixture.

    Reads the fixture from context *after* autocreate has run, so a
    server-assigned ``id`` (and possibly a different ``resourceType``) wins.
    Requires both a non-empty ``resourceType`` and ``id``; otherwise this is
    a semantic failure with no HTTP request. Only 2xx is semantic success.
    """
    fixture_id = fixture["id"]
    metric_name = _metric_name(document, f"fixture.{fixture_id}.autodelete")

    resource = context["fixtures"].get(fixture_id)
    resource_type = resource.get("resourceType") if isinstance(resource, dict) else None
    resource_id = resource.get("id") if isinstance(resource, dict) else None

    if not resource_type or not resource_id:
        _record_semantic_failure(
            user,
            metric_name,
            f"Fixture '{fixture_id}' has no server-assigned resourceType/id; cannot autodelete",
        )
        return {"applicable": True, "failed": True}

    url = f"{resource_type}/{resource_id}"

    from requests.structures import CaseInsensitiveDict

    headers = CaseInsensitiveDict()

    context["last_request"] = {
        "method": "DELETE",
        "url": url,
        "headers": dict(headers),
        "body": None,
        "form_body": None,
    }

    response_wrapper = _send_request(user, metric_name, "DELETE", url, headers, None)
    if response_wrapper is None:
        return {"applicable": True, "failed": True}

    context["last_response"] = response_wrapper

    if not (200 <= response_wrapper["status_code"] < 300):
        _record_semantic_failure(
            user, metric_name, f"Autodelete returned HTTP {response_wrapper['status_code']}"
        )
        return {"applicable": True, "failed": True}

    return {"applicable": True, "failed": False}


def _run_autocreates(document, user, context):
    """Run every fixture's autocreate, in fixture order, without short-circuiting."""
    failed = False
    results = []
    for fixture in document.get("fixtures", []):
        if not fixture.get("autocreate", False):
            continue
        result = _autocreate_fixture(document, user, context, fixture)
        results.append(result)
        if result.get("failed", False):
            failed = True
    return failed, results


def _run_autodeletes(document, user, context):
    """Run every fixture's autodelete, in fixture order, without short-circuiting."""
    failed = False
    results = []
    for fixture in document.get("fixtures", []):
        if not fixture.get("autodelete", False):
            continue
        result = _autodelete_fixture(document, user, context, fixture)
        results.append(result)
        if result.get("failed", False):
            failed = True
    return failed, results


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

        autocreate_failed, autocreate_results = _run_autocreates(document, user, context)
        setup_failed, setup_results = _run_phase(
            document, user, context, document.get("setup", [])
        )
        outcome["setup_failed"] = autocreate_failed or setup_failed
        outcome["setup_results"] = autocreate_results + setup_results

        for test in document.get("tests", []):
            if outcome["setup_failed"] or not _test_allowed(test["id"]):
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
        # the only short-circuit that returns before this point.
        autodelete_failed, autodelete_results = _run_autodeletes(document, user, context)
        teardown_failed, teardown_results = _run_phase(
            document, user, context, document.get("teardown", [])
        )
        outcome["teardown_ran"] = True
        outcome["teardown_failed"] = autodelete_failed or teardown_failed
        outcome["teardown_results"] = autodelete_results + teardown_results

    outcome["failed"] = (
        outcome["setup_failed"]
        or any(test["failed"] for test in outcome["tests"])
        or outcome["teardown_failed"]
    )

    return outcome

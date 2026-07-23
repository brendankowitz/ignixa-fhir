"""Task 8 RED-phase tests: operation execution, fixtures, variables, polling.

Every test in this module reloads the runtime fresh via ``fakes.load_runtime()``
in ``setUp`` so module-local state (user ordinals, capability decisions, and
monkeypatched dispatch functions) never leaks between tests.

Calls that exercise the still-placeholder ``_execute_operation`` (or a
not-yet-existing helper such as ``_derive_url``/``_resolve``/``_metric_name``/
``_parse_auth_header``) are centralized through the helpers below so a
"feature not implemented yet" state always surfaces as a clean assertion
*failure* (``self.fail(...)``) rather than an unhandled-exception *error*.
Only tests that specifically assert on unexpected-exception propagation
(exceptions distinct from the known placeholder shape) use a manual
try/except instead of ``assertRaises`` for the same reason.
"""

import ast
import copy
import os
import unittest
from unittest.mock import patch

import requests

import fakes


# ---------------------------------------------------------------------------
# Document / action builders
# ---------------------------------------------------------------------------


def _document(**overrides):
    base = {
        "schemaVersion": "1.0",
        "metadata": {"name": "Sample", "source": "test/sample.xml", "fhirVersion": None},
        "variables": [],
        "fixtures": [],
        "setup": [],
        "tests": [],
        "teardown": [],
    }
    base.update(overrides)
    return base


def _operation(
    action_id,
    type="read",
    method=None,
    resource="Patient",
    url=None,
    params=None,
    accept=None,
    content_type=None,
    source_id=None,
    response_id=None,
    request_id=None,
    encode_request_url=True,
    headers=None,
    wait_for=None,
    label=None,
    description=None,
):
    return {
        "id": action_id,
        "kind": "operation",
        "label": label,
        "description": description,
        "type": type,
        "method": method,
        "resource": resource,
        "url": url,
        "params": params,
        "accept": accept,
        "contentType": content_type,
        "sourceId": source_id,
        "responseId": response_id,
        "requestId": request_id,
        "encodeRequestUrl": encode_request_url,
        "headers": headers or [],
        "waitFor": wait_for,
    }


def _header(field, value):
    return {"field": field, "value": value}


def _wait_for(polling_status_code, max_attempts, interval_ms):
    return {"pollingStatusCode": polling_status_code, "maxAttempts": max_attempts, "intervalMs": interval_ms}


def _fixture(fixture_id, variants, autocreate=False, autodelete=False):
    return {"id": fixture_id, "autocreate": autocreate, "autodelete": autodelete, "variants": variants}


def _variable(name, default_value=None, source_id=None, extraction_kind="none", selector=None):
    return {
        "name": name,
        "defaultValue": default_value,
        "sourceId": source_id,
        "extractionKind": extraction_kind,
        "selector": selector,
    }


def _test_phase(test_id, actions=None, discard=False, initial_variables=None):
    return {
        "id": test_id,
        "name": test_id,
        "discardContextAfterExecution": discard,
        "initialVariables": initial_variables or {},
        "actions": actions or [],
    }


def _events(user):
    return list(user.environment.events.request.items)


def _events_of_type(user, request_type):
    return [event for event in _events(user) if event.get("request_type") == request_type]


def _semantic_events(user):
    return _events_of_type(user, "TESTSCRIPT_OPERATION")


# ---------------------------------------------------------------------------
# RED-safe invocation helpers
# ---------------------------------------------------------------------------

_PLACEHOLDER_MARKERS = ("is not implemented yet", "not implemented")


def get_fn(testcase, runtime, name):
    """Fetch ``runtime.<name>``, failing (not erroring) if it doesn't exist yet."""
    fn = getattr(runtime, name, None)
    if fn is None:
        testcase.fail(f"runtime.{name} is not implemented yet (Task 8 feature missing)")
    return fn


def call_or_fail(testcase, fn, *args, **kwargs):
    """Call ``fn``, converting the known Task 7 placeholder RuntimeError into a failure."""
    try:
        return fn(*args, **kwargs)
    except RuntimeError as exc:
        message = str(exc)
        if any(marker in message for marker in _PLACEHOLDER_MARKERS):
            testcase.fail(f"feature not implemented yet: {message}")
        raise


def run_operation(testcase, runtime, document, user, context, action):
    """Execute one operation action, converting placeholder failure into a clean fail()."""
    return call_or_fail(testcase, runtime._execute_operation, document, user, context, action)


def run_execute(testcase, runtime, document, user, state):
    """Run a full document via ``runtime.execute``, converting the known Task 7
    placeholder RuntimeError (raised from inside ``_execute_operation`` while
    running setup/test/teardown actions) into a clean fail() instead of an
    unhandled-exception error."""
    return call_or_fail(testcase, runtime.execute, document, user, state)


def new_context(runtime, document=None, ordinal=0, iteration=0):
    document = document or _document()
    return runtime._new_context(document, {"iteration": iteration, "ordinal": ordinal})


def make_user():
    client = fakes.FakeClient()
    return fakes.FakeUser(client=client), client


class RuntimeOperationsTestCase(unittest.TestCase):
    def setUp(self):
        self.runtime = fakes.load_runtime()


# ---------------------------------------------------------------------------
# Enforce genuine RED: minimal smoke test
# ---------------------------------------------------------------------------


class SmokeOperationExecutionTests(RuntimeOperationsTestCase):
    """A single, minimal test proving the placeholder produces a clean RED.

    This must be run in isolation first (see the RED report) to confirm the
    failure is a genuine "feature missing" assertion failure, not an import,
    fixture, or setup typo.
    """

    def test_ordinary_read_operation_executes_a_real_http_request(self):
        document = _document()
        user, client = make_user()
        context = new_context(self.runtime, document)
        client.queue_response(fakes.FakeResponse(status_code=200, json_data={"resourceType": "Patient", "id": "1"}))

        action = _operation("op-1", type="read", resource="Patient", request_id="op-1", response_id="op-1")

        run_operation(self, self.runtime, document, user, context, action)

        self.assertEqual(1, len(client.calls))
        self.assertEqual("GET", client.calls[0]["method"])


# ---------------------------------------------------------------------------
# Fake HTTP contract self-tests (validate test infrastructure directly).
#
# These exercise fakes.py itself, not the runtime under test, so they are
# expected to PASS now - they lock the contract the operation-execution
# tests below rely on. This satisfies the "returned Locust error response"
# (review item 10) and the fake-only half of the transport-exception
# contract (review item 11) directly against the fake, independent of
# whether the production runtime feature exists yet.
# ---------------------------------------------------------------------------


class FakeHttpContractTests(unittest.TestCase):
    def test_response_context_manager_fires_one_success_event_when_marked_success(self):
        user, client = make_user()
        response = client.queue_response(fakes.FakeResponse(status_code=200, json_data={"ok": True}))

        with client.request("GET", "/Patient/1", name="n::a", catch_response=True) as ctx:
            self.assertIs(ctx, response)
            ctx.success()

        events = _events(user)
        self.assertEqual(1, len(events))
        self.assertEqual("GET", events[0]["request_type"])
        self.assertEqual("n::a", events[0]["name"])
        self.assertIsNone(events[0]["exception"])
        self.assertTrue(response.success_called)

    def test_response_context_manager_defaults_to_failure_for_error_status_without_explicit_call(self):
        # "returned error responses produce one native failure on context exit
        # unless marked success" - review item 10 / the fake contract.
        user, client = make_user()
        client.queue_response(fakes.FakeResponse(status_code=500, json_data={"resourceType": "OperationOutcome"}))

        with client.request("POST", "/Patient", name="n::b", catch_response=True):
            pass  # deliberately do not call success()/failure()

        events = _events(user)
        self.assertEqual(1, len(events), "exactly one native event, no duplicate")
        self.assertEqual("POST", events[0]["request_type"])
        self.assertIsNotNone(events[0]["exception"])

    def test_response_context_manager_defaults_to_success_for_ok_status_without_explicit_call(self):
        user, client = make_user()
        client.queue_response(fakes.FakeResponse(status_code=204))

        with client.request("DELETE", "/Patient/1", name="n::c", catch_response=True):
            pass

        events = _events(user)
        self.assertEqual(1, len(events))
        self.assertIsNone(events[0]["exception"])

    def test_client_raises_queued_transport_exception_without_firing_any_event(self):
        user, client = make_user()
        exc = requests.exceptions.ConnectionError("boom")
        client.queue_exception(exc)

        with self.assertRaises(requests.exceptions.ConnectionError):
            client.request("GET", "/Patient", name="n::d", catch_response=True)

        self.assertEqual([], _events(user), "the fake itself never fires an event for a raised exception")

    def test_client_raises_queued_programmer_exception_without_firing_any_event(self):
        user, client = make_user()
        client.queue_exception(ValueError("unexpected"))

        with self.assertRaises(ValueError):
            client.request("GET", "/Patient", name="n::e", catch_response=True)

        self.assertEqual([], _events(user))

    def test_response_headers_are_case_insensitive(self):
        response = fakes.FakeResponse(status_code=200, headers={"ETag": 'W/"1"'})
        self.assertEqual('W/"1"', response.headers.get("etag"))
        self.assertEqual('W/"1"', response.headers.get("ETAG"))

    def test_client_captures_full_request_details(self):
        _, client = make_user()
        client.queue_response(fakes.FakeResponse(status_code=200))

        client.request(
            "POST",
            "/Patient/_search",
            name="src::action",
            catch_response=True,
            headers={"Content-Type": "application/x-www-form-urlencoded; charset=utf-8"},
            data=b"name=John",
        )

        call = client.calls[0]
        self.assertEqual("POST", call["method"])
        self.assertEqual("/Patient/_search", call["url"])
        self.assertEqual("src::action", call["name"])
        self.assertTrue(call["catch_response"])
        self.assertEqual(b"name=John", call["data"])
        self.assertEqual(
            "application/x-www-form-urlencoded; charset=utf-8", call["headers"]["Content-Type"]
        )

    def test_response_json_text_and_content_accessors(self):
        response = fakes.FakeResponse(status_code=200, content=b'{"a":1}', json_data={"a": 1})
        self.assertEqual({"a": 1}, response.json())
        self.assertEqual('{"a":1}', response.text)
        self.assertEqual(b'{"a":1}', response.content)

    def test_response_json_raises_when_unconfigured(self):
        response = fakes.FakeResponse(status_code=200)
        with self.assertRaises(ValueError):
            response.json()


# ---------------------------------------------------------------------------
# Item 21: bare Python import stays possible before third-party deps import.
# ---------------------------------------------------------------------------


class BareImportStaticAnalysisTests(unittest.TestCase):
    """Structural guard: the runtime must defer third-party imports.

    The authoritative proof that the module is importable with zero
    third-party dependencies is the Task 7 lifecycle suite running via
    ``uv run --python 3.9 python -m unittest ... test_runtime_lifecycle.py``
    with no ``--with`` packages at all (see the RED report). This test adds
    a fast, always-runnable structural guard against regressions: no
    module-level (unindented) import of ``requests``, ``locust``, or
    ``fhirpathpy`` may appear in the runtime source, even after Task 8 adds
    code that uses them lazily inside functions.
    """

    _THIRD_PARTY = ("requests", "locust", "fhirpathpy", "gevent")

    def test_runtime_module_has_no_top_level_third_party_imports(self):
        from fakes import load_runtime  # local import: keeps module import light

        module = load_runtime()
        runtime_path = module.__file__
        with open(runtime_path, "r", encoding="utf-8") as handle:
            source = handle.read()
        tree = ast.parse(source, filename=runtime_path)

        offending = []
        for node in tree.body:  # only top-level statements, not nested in functions
            if isinstance(node, ast.Import):
                for alias in node.names:
                    root = alias.name.split(".")[0]
                    if root in self._THIRD_PARTY:
                        offending.append(f"import {alias.name}")
            elif isinstance(node, ast.ImportFrom):
                if node.module and node.module.split(".")[0] in self._THIRD_PARTY:
                    offending.append(f"from {node.module} import ...")

        self.assertEqual(
            [],
            offending,
            "runtime module must import third-party packages lazily inside functions, not at module scope",
        )


# ---------------------------------------------------------------------------
# Item 1: CRUD/compiler-equivalent emitted methods; URL derivation; explicit
# URL and variable substitution.
# ---------------------------------------------------------------------------


class UrlAndMethodDerivationTests(RuntimeOperationsTestCase):
    def _context(self, variables=None):
        return {"variables": variables or {}}

    def test_resolve_substitutes_defined_variables(self):
        resolve = get_fn(self, self.runtime, "_resolve")
        context = self._context({"id": "abc123"})
        self.assertEqual("Patient/abc123", resolve("Patient/${id}", context))

    def test_resolve_passes_through_none_and_plain_text(self):
        resolve = get_fn(self, self.runtime, "_resolve")
        context = self._context({})
        self.assertIsNone(resolve(None, context))
        self.assertEqual("Patient", resolve("Patient", context))

    def test_resolve_raises_for_undefined_variable(self):
        resolve = get_fn(self, self.runtime, "_resolve")
        context = self._context({})
        with self.assertRaises(RuntimeError):
            resolve("Patient/${missing}", context)

    def test_derive_url_prefers_explicit_url_with_variable_substitution(self):
        derive_url = get_fn(self, self.runtime, "_derive_url")
        op = _operation("op-1", type="read", resource="Patient", url="${base}/Patient/${id}")
        context = self._context({"base": "http://example.test/fhir", "id": "7"})
        self.assertEqual("http://example.test/fhir/Patient/7", derive_url(op, context))

    def test_derive_url_ordinary_crud_uses_resource_and_resolved_params(self):
        derive_url = get_fn(self, self.runtime, "_derive_url")
        op = _operation("op-1", type="read", resource="Patient", params="/${id}")
        context = self._context({"id": "42"})
        self.assertEqual("Patient/42", derive_url(op, context))

    def test_derive_url_get_search_appends_resolved_params(self):
        derive_url = get_fn(self, self.runtime, "_derive_url")
        op = _operation("op-1", type="search", method="GET", resource="Patient", params="?name=${name}")
        context = self._context({"name": "Smith"})
        self.assertEqual("Patient?name=Smith", derive_url(op, context))

    def test_derive_url_post_search_uses_search_suffix_ignoring_params(self):
        derive_url = get_fn(self, self.runtime, "_derive_url")
        op = _operation("op-1", type="search", method="POST", resource="Patient", params="?name=Smith")
        context = self._context({})
        self.assertEqual("Patient/_search", derive_url(op, context))

    def test_derive_url_type_level_custom_operation_uses_dollar_path(self):
        derive_url = get_fn(self, self.runtime, "_derive_url")
        op = _operation("op-1", type="$validate", method="POST", resource="Patient", params=None)
        context = self._context({})
        self.assertEqual("Patient/$validate", derive_url(op, context))

    def test_derive_url_system_level_custom_operation_has_no_resource_prefix(self):
        derive_url = get_fn(self, self.runtime, "_derive_url")
        op = _operation("op-1", type="$export", method="POST", resource=None, params=None)
        context = self._context({})
        self.assertEqual("$export", derive_url(op, context))

    def test_execute_operation_sends_ir_baked_method_for_crud_actions(self):
        for method, op_type in (("GET", "read"), ("POST", "create"), ("PUT", "update"), ("DELETE", "delete")):
            with self.subTest(method=method):
                document = _document()
                user, client = make_user()
                context = new_context(self.runtime, document)
                client.queue_response(fakes.FakeResponse(status_code=200, json_data={}))
                action = _operation(
                    "op-1", type=op_type, method=method, resource="Patient",
                    request_id="op-1", response_id="op-1",
                )
                run_operation(self, self.runtime, document, user, context, action)
                self.assertEqual(method, client.calls[-1]["method"])


# ---------------------------------------------------------------------------
# Item 2: POST search strips leading '?', sends UTF-8 form bytes, forces the
# form content type over any script-provided Content-Type.
# ---------------------------------------------------------------------------


class PostSearchFormBodyTests(RuntimeOperationsTestCase):
    def test_post_search_strips_all_leading_question_marks_and_sends_utf8_form_bytes(self):
        document = _document()
        user, client = make_user()
        context = new_context(self.runtime, document)
        client.queue_response(fakes.FakeResponse(status_code=200, json_data={"resourceType": "Bundle"}))

        action = _operation(
            "op-1", type="search", method="POST", resource="Patient",
            params="??name=Jos\u00e9", request_id="op-1", response_id="op-1",
        )

        run_operation(self, self.runtime, document, user, context, action)

        call = client.calls[-1]
        self.assertEqual(b"name=Jos\xc3\xa9", call["data"])

    def test_post_search_forces_form_content_type_overriding_script_header(self):
        document = _document()
        user, client = make_user()
        context = new_context(self.runtime, document)
        client.queue_response(fakes.FakeResponse(status_code=200, json_data={"resourceType": "Bundle"}))

        action = _operation(
            "op-1", type="search", method="POST", resource="Patient", params="?name=Smith",
            content_type="application/fhir+json", request_id="op-1", response_id="op-1",
        )

        run_operation(self, self.runtime, document, user, context, action)

        headers = client.calls[-1]["headers"]
        self.assertEqual(
            "application/x-www-form-urlencoded; charset=utf-8",
            headers.get("Content-Type") or headers.get("content-type"),
        )


# ---------------------------------------------------------------------------
# Item 3: _parse_auth_header exact parsing rules.
# ---------------------------------------------------------------------------


class AuthHeaderParsingTests(RuntimeOperationsTestCase):
    def test_unset_env_var_returns_none(self):
        parse = get_fn(self, self.runtime, "_parse_auth_header")
        with patch.dict(os.environ, {}, clear=False):
            os.environ.pop("IGNIXA_AUTH_HEADER", None)
            self.assertIsNone(parse())

    def test_valid_header_uses_first_colon_and_allows_colons_in_value(self):
        parse = get_fn(self, self.runtime, "_parse_auth_header")
        with patch.dict(os.environ, {"IGNIXA_AUTH_HEADER": "Authorization: Bearer abc:def:ghi"}):
            self.assertEqual(("Authorization", "Bearer abc:def:ghi"), parse())

    def test_missing_colon_raises(self):
        parse = get_fn(self, self.runtime, "_parse_auth_header")
        with patch.dict(os.environ, {"IGNIXA_AUTH_HEADER": "NoColonHere"}):
            with self.assertRaises(RuntimeError):
                parse()

    def test_empty_name_raises(self):
        parse = get_fn(self, self.runtime, "_parse_auth_header")
        with patch.dict(os.environ, {"IGNIXA_AUTH_HEADER": ": value"}):
            with self.assertRaises(RuntimeError):
                parse()

    def test_empty_value_raises(self):
        parse = get_fn(self, self.runtime, "_parse_auth_header")
        with patch.dict(os.environ, {"IGNIXA_AUTH_HEADER": "Name:"}):
            with self.assertRaises(RuntimeError):
                parse()

    def test_explicit_empty_string_raises_as_malformed_not_unset(self):
        parse = get_fn(self, self.runtime, "_parse_auth_header")
        with patch.dict(os.environ, {"IGNIXA_AUTH_HEADER": ""}):
            with self.assertRaises(RuntimeError):
                parse()


# ---------------------------------------------------------------------------
# Item 4: IGNIXA_AUTH_HEADER applied to every FHIR request; explicit
# TestScript header overrides it case-insensitively.
# ---------------------------------------------------------------------------


class AuthHeaderApplicationTests(RuntimeOperationsTestCase):
    def test_auth_header_is_applied_to_an_ordinary_operation_request(self):
        document = _document()
        user, client = make_user()
        context = new_context(self.runtime, document)
        client.queue_response(fakes.FakeResponse(status_code=200, json_data={}))
        action = _operation("op-1", type="read", resource="Patient", request_id="op-1", response_id="op-1")

        with patch.dict(os.environ, {"IGNIXA_AUTH_HEADER": "Authorization: Bearer secret"}):
            run_operation(self, self.runtime, document, user, context, action)

        headers = client.calls[-1]["headers"]
        self.assertEqual("Bearer secret", headers.get("Authorization") or headers.get("authorization"))

    def test_explicit_script_header_overrides_auth_header_case_insensitively(self):
        # Distinct env vs. script values so this test can actually distinguish
        # the script header winning from the auth header merely matching -
        # a RED-phase authoring bug (both values were identical) is fixed here.
        document = _document()
        user, client = make_user()
        context = new_context(self.runtime, document)
        client.queue_response(fakes.FakeResponse(status_code=200, json_data={}))
        action = _operation(
            "op-1", type="read", resource="Patient", request_id="op-1", response_id="op-1",
            headers=[_header("AUTHORIZATION", "script-override-token")],
        )

        with patch.dict(os.environ, {"IGNIXA_AUTH_HEADER": "Authorization: env-secret-token"}):
            run_operation(self, self.runtime, document, user, context, action)

        headers = client.calls[-1]["headers"]
        value = headers.get("Authorization") or headers.get("AUTHORIZATION") or headers.get("authorization")
        self.assertEqual("script-override-token", value)

    def test_auth_header_is_applied_to_fixture_autocreate_request(self):
        self.runtime._execute_action = lambda *a, **k: {"applicable": True, "failed": False}
        document = _document(
            fixtures=[_fixture("patient", [{"resourceType": "Patient"}], autocreate=True)],
        )
        user, client = make_user()
        client.queue_response(fakes.FakeResponse(status_code=201, json_data={"resourceType": "Patient", "id": "1"}))
        state = self.runtime.initialize_user(document, user)

        with patch.dict(os.environ, {"IGNIXA_AUTH_HEADER": "Authorization: Bearer secret"}):
            run_execute(self, self.runtime, document, user, state)

        self.assertEqual(1, len(client.calls))
        headers = client.calls[0]["headers"]
        self.assertEqual("Bearer secret", headers.get("Authorization") or headers.get("authorization"))

    def test_auth_header_is_applied_to_fixture_autodelete_request(self):
        self.runtime._execute_action = lambda *a, **k: {"applicable": True, "failed": False}
        document = _document(
            fixtures=[_fixture("patient", [{"resourceType": "Patient", "id": "server-1"}], autodelete=True)],
        )
        user, client = make_user()
        client.queue_response(fakes.FakeResponse(status_code=204))
        state = self.runtime.initialize_user(document, user)

        with patch.dict(os.environ, {"IGNIXA_AUTH_HEADER": "Authorization: Bearer secret"}):
            run_execute(self, self.runtime, document, user, state)

        self.assertEqual(1, len(client.calls))
        headers = client.calls[0]["headers"]
        self.assertEqual("Bearer secret", headers.get("Authorization") or headers.get("authorization"))


# ---------------------------------------------------------------------------
# Item 5: custom header field/value variable substitution; undefined
# variable emits exactly one source-qualified semantic failure/no HTTP.
# ---------------------------------------------------------------------------


class HeaderSubstitutionTests(RuntimeOperationsTestCase):
    def test_header_field_and_value_are_variable_substituted(self):
        document = _document()
        user, client = make_user()
        context = new_context(self.runtime, document)
        context["variables"]["header_name"] = "X-Trace"
        context["variables"]["trace_id"] = "abc-123"
        client.queue_response(fakes.FakeResponse(status_code=200, json_data={}))

        action = _operation(
            "op-1", type="read", resource="Patient", request_id="op-1", response_id="op-1",
            headers=[_header("${header_name}", "${trace_id}")],
        )

        run_operation(self, self.runtime, document, user, context, action)

        headers = client.calls[-1]["headers"]
        self.assertEqual("abc-123", headers.get("X-Trace"))

    def test_undefined_header_variable_emits_one_semantic_failure_and_sends_no_http_request(self):
        document = _document(metadata={"name": "n", "source": "test/undefined.xml", "fhirVersion": None})
        user, client = make_user()
        context = new_context(self.runtime, document)
        action = _operation(
            "op-1", type="read", resource="Patient", request_id="op-1", response_id="op-1",
            headers=[_header("X-Trace", "${missing_var}")],
        )

        run_operation(self, self.runtime, document, user, context, action)

        self.assertEqual(0, len(client.calls), "an undefined variable must never reach the HTTP client")
        semantic = _semantic_events(user)
        self.assertEqual(1, len(semantic))
        self.assertEqual("test/undefined.xml::op-1", semantic[0]["name"])
        self.assertIsNotNone(semantic[0]["exception"])


# ---------------------------------------------------------------------------
# Item 6: sourceId body resolution, compact JSON bytes, content types.
# ---------------------------------------------------------------------------


class SourceIdBodyAndContentTypeTests(RuntimeOperationsTestCase):
    def test_source_id_uses_fixture_body_as_compact_json_with_default_content_type(self):
        document = _document(fixtures=[_fixture("patient", [{"resourceType": "Patient", "active": True}])])
        user, client = make_user()
        context = new_context(self.runtime, document)
        context["fixtures"]["patient"] = {"resourceType": "Patient", "active": True}
        client.queue_response(fakes.FakeResponse(status_code=201, json_data={"resourceType": "Patient", "id": "1"}))

        action = _operation(
            "op-1", type="create", method="POST", resource="Patient", source_id="patient",
            request_id="op-1", response_id="op-1",
        )

        run_operation(self, self.runtime, document, user, context, action)

        call = client.calls[-1]
        self.assertEqual(b'{"resourceType":"Patient","active":true}', call["data"])
        headers = call["headers"]
        self.assertEqual(
            "application/fhir+json; charset=utf-8",
            headers.get("Content-Type") or headers.get("content-type"),
        )

    def test_source_id_uses_prior_response_body_when_not_a_known_fixture(self):
        document = _document()
        user, client = make_user()
        context = new_context(self.runtime, document)
        client.queue_response(fakes.FakeResponse(status_code=200, json_data={"resourceType": "Patient", "id": "1"}))
        first = _operation("op-1", type="read", resource="Patient", request_id="op-1", response_id="op-1")
        run_operation(self, self.runtime, document, user, context, first)

        client.queue_response(fakes.FakeResponse(status_code=200, json_data={}))
        second = _operation(
            "op-2", type="update", method="PUT", resource="Patient/1", source_id="op-1",
            request_id="op-2", response_id="op-2",
        )
        run_operation(self, self.runtime, document, user, context, second)

        self.assertEqual(b'{"resourceType":"Patient","id":"1"}', client.calls[-1]["data"])

    def test_unknown_source_id_emits_semantic_failure_without_http_call(self):
        document = _document(metadata={"name": "n", "source": "test/unknown-source.xml", "fhirVersion": None})
        user, client = make_user()
        context = new_context(self.runtime, document)
        action = _operation(
            "op-1", type="create", method="POST", resource="Patient", source_id="does-not-exist",
            request_id="op-1", response_id="op-1",
        )

        run_operation(self, self.runtime, document, user, context, action)

        self.assertEqual(0, len(client.calls))
        semantic = _semantic_events(user)
        self.assertEqual(1, len(semantic))
        self.assertEqual("test/unknown-source.xml::op-1", semantic[0]["name"])

    def test_custom_content_type_is_sent_verbatim(self):
        document = _document(fixtures=[_fixture("patient", [{"resourceType": "Patient"}])])
        user, client = make_user()
        context = new_context(self.runtime, document)
        context["fixtures"]["patient"] = {"resourceType": "Patient"}
        client.queue_response(fakes.FakeResponse(status_code=201, json_data={}))

        action = _operation(
            "op-1", type="create", method="POST", resource="Patient", source_id="patient",
            content_type="application/xml", request_id="op-1", response_id="op-1",
        )

        run_operation(self, self.runtime, document, user, context, action)

        headers = client.calls[-1]["headers"]
        self.assertEqual("application/xml", headers.get("Content-Type") or headers.get("content-type"))

    def test_no_body_request_removes_content_type_but_preserves_accept_and_other_headers(self):
        document = _document()
        user, client = make_user()
        context = new_context(self.runtime, document)
        client.queue_response(fakes.FakeResponse(status_code=200, json_data={}))

        action = _operation(
            "op-1", type="read", method="GET", resource="Patient", request_id="op-1", response_id="op-1",
            content_type="application/xml",
            headers=[_header("Accept", "application/fhir+json"), _header("Content-Type", "application/xml")],
        )

        run_operation(self, self.runtime, document, user, context, action)

        headers = client.calls[-1]["headers"]
        self.assertNotIn("Content-Type", headers)
        self.assertNotIn("content-type", {k.lower() for k in headers})
        self.assertEqual("application/fhir+json", headers.get("Accept"))


# ---------------------------------------------------------------------------
# Item 7: canonical relative request wrapper + actual response object stored
# under requestId/responseId and as last refs.
# ---------------------------------------------------------------------------


class RequestResponseHistoryTests(RuntimeOperationsTestCase):
    def test_request_and_response_are_stored_under_explicit_ids_and_as_last(self):
        document = _document()
        user, client = make_user()
        context = new_context(self.runtime, document)
        queued = client.queue_response(fakes.FakeResponse(status_code=200, json_data={"resourceType": "Patient"}))

        action = _operation(
            "op-1", type="read", resource="Patient/1", request_id="req-1", response_id="resp-1",
        )

        run_operation(self, self.runtime, document, user, context, action)

        self.assertIn("req-1", context["requests"])
        self.assertEqual("GET", context["requests"]["req-1"]["method"])
        self.assertEqual("Patient/1", context["requests"]["req-1"]["url"])

        # The stored response must be the actual queued FakeResponse object,
        # not a dict snapshot copy - this is what gives Task 9 parse-error
        # access to the real response.
        self.assertIs(queued, context["responses"]["resp-1"])
        self.assertIs(queued, context["last_response"])
        self.assertEqual("Patient/1", context["last_request"]["url"])

    def test_missing_request_or_response_id_only_updates_last_refs(self):
        document = _document()
        user, client = make_user()
        context = new_context(self.runtime, document)
        queued = client.queue_response(fakes.FakeResponse(status_code=200, json_data={}))

        action = _operation("op-1", type="read", resource="Patient/1", request_id=None, response_id=None)

        run_operation(self, self.runtime, document, user, context, action)

        self.assertEqual({}, context["requests"])
        self.assertEqual({}, context["responses"])
        self.assertIs(queued, context["last_response"])


# ---------------------------------------------------------------------------
# Item 8: native/semantic metric names and fixture metric IDs.
# ---------------------------------------------------------------------------


class MetricNamingTests(RuntimeOperationsTestCase):
    def test_metric_name_joins_source_and_action_id(self):
        metric_name = get_fn(self, self.runtime, "_metric_name")
        document = _document(metadata={"name": "n", "source": "conformance/patient-crud.xml", "fhirVersion": None})
        self.assertEqual("conformance/patient-crud.xml::setup.0", metric_name(document, "setup.0"))

    def test_native_http_event_uses_metric_name(self):
        document = _document(metadata={"name": "n", "source": "s/x.xml", "fhirVersion": None})
        user, client = make_user()
        context = new_context(self.runtime, document)
        client.queue_response(fakes.FakeResponse(status_code=200, json_data={}))
        action = _operation("test.0.action.0", type="read", resource="Patient", request_id="a", response_id="a")

        run_operation(self, self.runtime, document, user, context, action)

        self.assertEqual("s/x.xml::test.0.action.0", client.calls[-1]["name"])

    def test_fixture_autocreate_metric_id_is_exact(self):
        self.runtime._execute_action = lambda *a, **k: {"applicable": True, "failed": False}
        document = _document(
            metadata={"name": "n", "source": "s/fixtures.xml", "fhirVersion": None},
            fixtures=[_fixture("patient", [{"resourceType": "Patient"}], autocreate=True)],
        )
        user, client = make_user()
        client.queue_response(fakes.FakeResponse(status_code=201, json_data={"resourceType": "Patient", "id": "1"}))
        state = self.runtime.initialize_user(document, user)

        run_execute(self, self.runtime, document, user, state)

        self.assertEqual(1, len(client.calls))
        self.assertEqual("s/fixtures.xml::fixture.patient.autocreate", client.calls[0]["name"])


# ---------------------------------------------------------------------------
# Item 9 & part of 11: received 4xx/5xx stays an operation success; a
# transport exception is a native-only failure with correct request_type.
# ---------------------------------------------------------------------------


class HttpOutcomeSemanticsTests(RuntimeOperationsTestCase):
    def test_received_4xx_calls_success_and_operation_stays_a_native_success_with_no_semantic_event(self):
        document = _document()
        user, client = make_user()
        context = new_context(self.runtime, document)
        response = client.queue_response(
            fakes.FakeResponse(status_code=404, json_data={"resourceType": "OperationOutcome"})
        )
        action = _operation("op-1", type="read", resource="Patient/missing", request_id="op-1", response_id="op-1")

        result = run_operation(self, self.runtime, document, user, context, action)

        self.assertTrue(response.success_called, "the runtime must call response.success() for any received status")
        native = _events_of_type(user, "GET")
        self.assertEqual(1, len(native))
        self.assertIsNone(native[0]["exception"])
        self.assertEqual(0, len(_semantic_events(user)))
        self.assertFalse(result.get("failed", False))

    def test_received_5xx_also_stays_a_native_success_with_no_semantic_event(self):
        document = _document()
        user, client = make_user()
        context = new_context(self.runtime, document)
        response = client.queue_response(fakes.FakeResponse(status_code=500))
        action = _operation("op-1", type="read", resource="Patient/1", request_id="op-1", response_id="op-1")

        run_operation(self, self.runtime, document, user, context, action)

        self.assertTrue(response.success_called)
        self.assertEqual(1, len(_events_of_type(user, "GET")))
        self.assertEqual(0, len(_semantic_events(user)))

    def test_transport_exception_fires_exactly_one_native_event_with_http_method_request_type(self):
        # Review correction: real Locust's HttpSession only lets a
        # requests.exceptions.RequestException escape client.request() itself
        # (before any response context exists) for URL/schema construction
        # errors such as InvalidURL/MissingSchema/InvalidSchema - ordinary
        # transport failures (e.g. ConnectionError) come back as a *returned*
        # response with .error set instead (see HttpOutcomeSemanticsTests'
        # returned-error-response coverage below). This test now exercises a
        # realistic exception type for the "no response context" path.
        document = _document(metadata={"name": "n", "source": "s/transport.xml", "fhirVersion": None})
        user, client = make_user()
        context = new_context(self.runtime, document)
        exc = requests.exceptions.InvalidURL("no scheme supplied")
        client.queue_exception(exc)
        action = _operation("op-1", type="read", resource="Patient/1", request_id="op-1", response_id="op-1")

        result = run_operation(self, self.runtime, document, user, context, action)

        native = _events_of_type(user, "GET")
        self.assertEqual(1, len(native))
        self.assertEqual("s/transport.xml::op-1", native[0]["name"])
        self.assertIsNotNone(native[0]["exception"])
        self.assertEqual(0, len(_semantic_events(user)), "no duplicate semantic event for a transport failure")
        self.assertTrue(result.get("failed", False))

    def test_returned_error_response_fires_exactly_one_native_failure_success_not_called(self):
        # The common real-Locust path: HttpSession.request(catch_response=True)
        # never lets an ordinary transport failure (e.g. a dropped connection)
        # raise - it returns a response with `.error` set instead. The runtime
        # must not call success() on such a response, letting the fake's (and
        # real Locust's) default failure-on-exit behavior fire exactly one
        # native failure event, with no semantic duplicate.
        document = _document(metadata={"name": "n", "source": "s/transport.xml", "fhirVersion": None})
        user, client = make_user()
        context = new_context(self.runtime, document)
        transport_error = requests.exceptions.ConnectionError("connection reset")
        response = client.queue_response(fakes.FakeResponse(status_code=0, error=transport_error))
        action = _operation("op-1", type="read", resource="Patient/1", request_id="op-1", response_id="op-1")

        result = run_operation(self, self.runtime, document, user, context, action)

        self.assertFalse(response.success_called, "success() must not be called for a response carrying .error")
        native = _events_of_type(user, "GET")
        self.assertEqual(1, len(native))
        self.assertIsNotNone(native[0]["exception"])
        self.assertEqual(0, len(_semantic_events(user)))
        self.assertTrue(result.get("failed", False))

    def test_unexpected_non_requests_exception_propagates_uncaught(self):
        document = _document()
        user, client = make_user()
        context = new_context(self.runtime, document)
        client.queue_exception(ValueError("programmer bug"))
        action = _operation("op-1", type="read", resource="Patient/1", request_id="op-1", response_id="op-1")

        try:
            self.runtime._execute_operation(document, user, context, action)
        except ValueError:
            pass
        except RuntimeError as exc:
            self.fail(f"feature not implemented yet: {exc}")
        else:
            self.fail("expected the unexpected ValueError to propagate out of _execute_operation")


# ---------------------------------------------------------------------------
# Item 12: encodeRequestUrl=false logs the "URL was encoded" warning, still
# sends the request, and does not emit a failed semantic event.
# ---------------------------------------------------------------------------


class EncodeRequestUrlWarningTests(RuntimeOperationsTestCase):
    def test_encode_request_url_false_logs_warning_sends_request_and_emits_no_failed_semantic_event(self):
        # Review correction: the warning must carry the full source-qualified
        # metric name and the evaluator-equivalent meaning verbatim
        # ("encodeRequestUrl=false is not supported; URL was encoded"), not a
        # differently-worded paraphrase.
        document = _document(metadata={"name": "n", "source": "s/enc.xml", "fhirVersion": None})
        user, client = make_user()
        context = new_context(self.runtime, document)
        client.queue_response(fakes.FakeResponse(status_code=200, json_data={}))
        action = _operation(
            "op-1", type="read", resource="Patient", request_id="op-1", response_id="op-1",
            encode_request_url=False,
        )

        with self.assertLogs("ignixa.testscript", level="WARNING") as log_ctx:
            run_operation(self, self.runtime, document, user, context, action)

        joined = "\n".join(log_ctx.output)
        self.assertIn(
            "s/enc.xml::op-1", joined,
            "the warning must be source-qualified with the full metric name",
        )
        self.assertIn(
            "encodeRequestUrl=false is not supported; URL was encoded", joined,
            "the warning must match the evaluator-equivalent meaning verbatim",
        )
        self.assertEqual(1, len(client.calls))
        self.assertEqual(0, len(_semantic_events(user)))


# ---------------------------------------------------------------------------
# Item 13: header/dotted-path variable extraction.
# ---------------------------------------------------------------------------


def _run_variable_extraction(testcase, variable, response, resource="Patient"):
    document = _document(variables=[variable])
    user, client = make_user()
    context = new_context(testcase.runtime, document)
    client.queue_response(response)
    action = _operation(
        "op-1", type="read", resource=resource, source_id=None,
        request_id="op-1", response_id="op-1",
    )
    run_operation(testcase, testcase.runtime, document, user, context, action)
    return context


class HeaderAndPathExtractionTests(RuntimeOperationsTestCase):
    def test_header_extraction_is_case_insensitive(self):
        variable = _variable("etag", source_id="op-1", extraction_kind="header", selector="etag")
        response = fakes.FakeResponse(status_code=200, headers={"ETag": 'W/"3"'}, json_data={})
        context = _run_variable_extraction(self, variable, response)
        self.assertEqual('W/"3"', context["variables"]["etag"])

    def test_missing_header_is_a_no_op(self):
        variable = _variable("etag", default_value="unset", source_id="op-1", extraction_kind="header", selector="Missing-Header")
        response = fakes.FakeResponse(status_code=200, headers={}, json_data={})
        context = _run_variable_extraction(self, variable, response)
        self.assertEqual("unset", context["variables"]["etag"])

    def test_dotted_path_extracts_string_leaf(self):
        variable = _variable("pid", source_id="op-1", extraction_kind="path", selector="id")
        response = fakes.FakeResponse(status_code=200, json_data={"resourceType": "Patient", "id": "abc"})
        context = _run_variable_extraction(self, variable, response)
        self.assertEqual("abc", context["variables"]["pid"])

    def test_dotted_path_extracts_numeric_leaf_as_json_text(self):
        variable = _variable("count", source_id="op-1", extraction_kind="path", selector="total")
        response = fakes.FakeResponse(status_code=200, json_data={"total": 3})
        context = _run_variable_extraction(self, variable, response)
        self.assertEqual("3", context["variables"]["count"])

    def test_dotted_path_extracts_boolean_leaf_as_json_text(self):
        variable = _variable("active", source_id="op-1", extraction_kind="path", selector="active")
        response = fakes.FakeResponse(status_code=200, json_data={"active": True})
        context = _run_variable_extraction(self, variable, response)
        self.assertEqual("true", context["variables"]["active"])

    def test_dotted_path_extracts_object_leaf_as_compact_json(self):
        variable = _variable("meta", source_id="op-1", extraction_kind="path", selector="meta")
        response = fakes.FakeResponse(status_code=200, json_data={"meta": {"versionId": "1"}})
        context = _run_variable_extraction(self, variable, response)
        self.assertEqual('{"versionId":"1"}', context["variables"]["meta"])

    def test_dotted_path_extracts_array_leaf_as_compact_json(self):
        variable = _variable("names", source_id="op-1", extraction_kind="path", selector="name")
        response = fakes.FakeResponse(status_code=200, json_data={"name": [{"family": "Smith"}]})
        context = _run_variable_extraction(self, variable, response)
        self.assertEqual('[{"family":"Smith"}]', context["variables"]["names"])

    def test_dotted_path_does_not_traverse_array_indices(self):
        variable = _variable("family", default_value="unset", source_id="op-1", extraction_kind="path", selector="name.family")
        response = fakes.FakeResponse(status_code=200, json_data={"name": [{"family": "Smith"}]})
        context = _run_variable_extraction(self, variable, response)
        self.assertEqual("unset", context["variables"]["family"])

    def test_missing_path_segment_is_a_no_op(self):
        variable = _variable("missing", default_value="unset", source_id="op-1", extraction_kind="path", selector="nope")
        response = fakes.FakeResponse(status_code=200, json_data={"resourceType": "Patient"})
        context = _run_variable_extraction(self, variable, response)
        self.assertEqual("unset", context["variables"]["missing"])


# ---------------------------------------------------------------------------
# Item 14: FHIRPath variable extraction.
# ---------------------------------------------------------------------------


_PATIENT_WITH_NAMES = {
    "resourceType": "Patient",
    "id": "1",
    "active": True,
    "name": [{"family": "Smith"}, {"family": "Jones"}],
}


class FhirPathExtractionTests(RuntimeOperationsTestCase):
    def test_scalar_expression_extracts_string_value(self):
        variable = _variable("pid", source_id="op-1", extraction_kind="fhirPath", selector="id")
        response = fakes.FakeResponse(status_code=200, json_data=_PATIENT_WITH_NAMES)
        context = _run_variable_extraction(self, variable, response)
        self.assertEqual("1", context["variables"]["pid"])

    def test_boolean_expression_extracts_lowercase_fhirpath_string(self):
        variable = _variable("is_active", source_id="op-1", extraction_kind="fhirPath", selector="active")
        response = fakes.FakeResponse(status_code=200, json_data=_PATIENT_WITH_NAMES)
        context = _run_variable_extraction(self, variable, response)
        self.assertEqual("true", context["variables"]["is_active"])

    def test_empty_result_is_a_no_op(self):
        variable = _variable(
            "deceased", default_value="unset", source_id="op-1", extraction_kind="fhirPath",
            selector="deceasedBoolean",
        )
        response = fakes.FakeResponse(status_code=200, json_data=_PATIENT_WITH_NAMES)
        context = _run_variable_extraction(self, variable, response)
        self.assertEqual("unset", context["variables"]["deceased"])

    def test_multi_value_result_is_a_no_op(self):
        variable = _variable(
            "family", default_value="unset", source_id="op-1", extraction_kind="fhirPath",
            selector="name.family",
        )
        response = fakes.FakeResponse(status_code=200, json_data=_PATIENT_WITH_NAMES)
        context = _run_variable_extraction(self, variable, response)
        self.assertEqual("unset", context["variables"]["family"])

    def test_single_complex_result_is_a_no_op_matching_dotnet_asstring_null(self):
        variable = _variable(
            "first_name", default_value="unset", source_id="op-1", extraction_kind="fhirPath",
            selector="name.first()",
        )
        response = fakes.FakeResponse(status_code=200, json_data=_PATIENT_WITH_NAMES)
        context = _run_variable_extraction(self, variable, response)
        self.assertEqual("unset", context["variables"]["first_name"])

    def test_single_null_element_result_is_a_no_op(self):
        # Review correction: fhirpathpy can return a single-element result
        # list whose only element is itself None (e.g. a null array entry).
        # That must be treated as a no-op too, not stringified to the literal
        # text "None".
        variable = _variable(
            "contact", default_value="unset", source_id="op-1", extraction_kind="fhirPath",
            selector="Patient.contact",
        )
        response = fakes.FakeResponse(
            status_code=200,
            json_data={"resourceType": "Patient", "contact": [None]},
        )
        context = _run_variable_extraction(self, variable, response)
        self.assertEqual("unset", context["variables"]["contact"])

    def test_malformed_expression_fails_the_operation_and_emits_testscript_operation_event(self):
        document = _document(
            metadata={"name": "n", "source": "test/fhirpath.xml", "fhirVersion": None},
            variables=[_variable("x", source_id="op-1", extraction_kind="fhirPath", selector="foo(")],
        )
        user, client = make_user()
        context = new_context(self.runtime, document)
        client.queue_response(fakes.FakeResponse(status_code=200, json_data=_PATIENT_WITH_NAMES))
        action = _operation("op-1", type="read", resource="Patient", request_id="op-1", response_id="op-1")

        result = run_operation(self, self.runtime, document, user, context, action)

        self.assertNotIn("x", context["variables"])
        semantic = _semantic_events(user)
        self.assertEqual(1, len(semantic))
        self.assertEqual("test/fhirpath.xml::op-1", semantic[0]["name"])
        self.assertTrue(result.get("failed", False))
        # The HTTP call itself still succeeded - only the extraction failed.
        self.assertEqual(1, len(_events_of_type(user, "GET")))
        self.assertIsNone(_events_of_type(user, "GET")[0]["exception"])


# ---------------------------------------------------------------------------
# Item 15: variable sourceId selects a specific historical response rather
# than always using the last one.
# ---------------------------------------------------------------------------


class VariableSourceIdSelectionTests(RuntimeOperationsTestCase):
    def test_variable_with_source_id_stays_pinned_while_last_reflects_the_latest_operation(self):
        document = _document(
            variables=[
                _variable("last_id", extraction_kind="path", selector="id"),
                _variable("pinned_id", source_id="r1", extraction_kind="path", selector="id"),
            ],
            setup=[
                _operation("setup.0", type="read", resource="Patient/A", request_id="r1", response_id="r1"),
                _operation("setup.1", type="read", resource="Patient/B", request_id="r2", response_id="r2"),
            ],
        )
        user, client = make_user()
        client.queue_response(fakes.FakeResponse(status_code=200, json_data={"resourceType": "Patient", "id": "A"}))
        client.queue_response(fakes.FakeResponse(status_code=200, json_data={"resourceType": "Patient", "id": "B"}))
        state = self.runtime.initialize_user(document, user)

        outcome = run_execute(self, self.runtime, document, user, state)

        context = outcome["context"]
        self.assertEqual("B", context["variables"]["last_id"])
        self.assertEqual("A", context["variables"]["pinned_id"])


# ---------------------------------------------------------------------------
# Item 20: header/body dictionaries are replaced by assignment, not nested
# mutation, preserving the Task 7 shallow-clone assumption.
# ---------------------------------------------------------------------------


class ShallowCloneAssumptionTests(RuntimeOperationsTestCase):
    def test_fixture_body_used_as_request_source_is_never_mutated(self):
        document = _document(fixtures=[_fixture("patient", [{"resourceType": "Patient", "id": "shared"}])])
        user, client = make_user()
        context = new_context(self.runtime, document)
        context["fixtures"]["patient"] = {"resourceType": "Patient", "id": "shared"}
        original = context["fixtures"]["patient"]
        snapshot = copy.deepcopy(original)
        client.queue_response(fakes.FakeResponse(status_code=201, json_data={"resourceType": "Patient", "id": "server-1"}))

        action = _operation(
            "create-1", type="create", method="POST", resource="Patient", source_id="patient",
            request_id="create-1", response_id="create-1",
        )
        run_operation(self, self.runtime, document, user, context, action)

        self.assertIs(context["fixtures"]["patient"], original)
        self.assertEqual(snapshot, original)

    def test_operation_headers_do_not_leak_between_two_operations(self):
        document = _document()
        user, client = make_user()
        context = new_context(self.runtime, document)
        client.queue_response(fakes.FakeResponse(status_code=200, json_data={}))
        client.queue_response(fakes.FakeResponse(status_code=200, json_data={}))

        action1 = _operation(
            "op-1", type="read", resource="Patient", request_id="op-1", response_id="op-1",
            headers=[_header("X-Custom", "one")],
        )
        action2 = _operation(
            "op-2", type="read", resource="Patient", request_id="op-2", response_id="op-2",
            headers=[_header("X-Custom", "two")],
        )

        run_operation(self, self.runtime, document, user, context, action1)
        run_operation(self, self.runtime, document, user, context, action2)

        self.assertEqual("one", client.calls[0]["headers"].get("X-Custom"))
        self.assertEqual("two", client.calls[1]["headers"].get("X-Custom"))


# ---------------------------------------------------------------------------
# Items 16 & 17: fixture autocreate/autodelete lifecycle.
# ---------------------------------------------------------------------------


class FixtureAutocreateTests(RuntimeOperationsTestCase):
    def _run(self, document, user, client):
        state = self.runtime.initialize_user(document, user)
        return run_execute(self, self.runtime, document, user, state)

    def test_autocreate_posts_resource_type_with_compact_body_and_native_success_metric(self):
        document = _document(
            metadata={"name": "n", "source": "s/fixtures.xml", "fhirVersion": None},
            variables=[_variable("patient_id", source_id="patient", extraction_kind="path", selector="id")],
            fixtures=[_fixture("patient", [{"resourceType": "Patient", "active": True}], autocreate=True)],
        )
        user, client = make_user()
        client.queue_response(
            fakes.FakeResponse(status_code=201, json_data={"resourceType": "Patient", "id": "server-123"})
        )

        outcome = self._run(document, user, client)

        self.assertEqual(1, len(client.calls))
        call = client.calls[0]
        self.assertEqual("POST", call["method"])
        self.assertEqual("Patient", call["url"])
        self.assertEqual(b'{"resourceType":"Patient","active":true}', call["data"])
        self.assertEqual("s/fixtures.xml::fixture.patient.autocreate", call["name"])

        native = _events_of_type(user, "POST")
        self.assertEqual(1, len(native))
        self.assertIsNone(native[0]["exception"])
        self.assertEqual(0, len(_semantic_events(user)))

        context = outcome["context"]
        self.assertEqual({"resourceType": "Patient", "id": "server-123"}, context["fixtures"]["patient"])
        self.assertEqual("server-123", context["variables"]["patient_id"])
        self.assertFalse(outcome["setup_failed"])

    def test_autocreate_non_2xx_marks_setup_failed_with_a_semantic_event_but_native_success(self):
        document = _document(
            metadata={"name": "n", "source": "s/fixtures.xml", "fhirVersion": None},
            fixtures=[_fixture("patient", [{"resourceType": "Patient"}], autocreate=True)],
        )
        user, client = make_user()
        client.queue_response(fakes.FakeResponse(status_code=400, json_data={"resourceType": "OperationOutcome"}))

        outcome = self._run(document, user, client)

        native = _events_of_type(user, "POST")
        self.assertEqual(1, len(native))
        self.assertIsNone(native[0]["exception"], "the HTTP layer received a response, so it is a native success")
        semantic = _semantic_events(user)
        self.assertEqual(1, len(semantic))
        self.assertIsNotNone(semantic[0]["exception"])
        self.assertTrue(outcome["setup_failed"])

    def test_autocreate_missing_resource_type_emits_semantic_failure_without_http_call(self):
        document = _document(
            metadata={"name": "n", "source": "s/fixtures.xml", "fhirVersion": None},
            fixtures=[_fixture("patient", [{"id": "no-type"}], autocreate=True)],
        )
        user, client = make_user()

        outcome = self._run(document, user, client)

        self.assertEqual(0, len(client.calls))
        semantic = _semantic_events(user)
        self.assertEqual(1, len(semantic))
        self.assertTrue(outcome["setup_failed"])

    def test_autocreate_transport_exception_is_native_only(self):
        document = _document(
            metadata={"name": "n", "source": "s/fixtures.xml", "fhirVersion": None},
            fixtures=[_fixture("patient", [{"resourceType": "Patient"}], autocreate=True)],
        )
        user, client = make_user()
        client.queue_exception(requests.exceptions.ConnectionError("boom"))

        outcome = self._run(document, user, client)

        native = _events_of_type(user, "POST")
        self.assertEqual(1, len(native))
        self.assertIsNotNone(native[0]["exception"])
        self.assertEqual(0, len(_semantic_events(user)))
        self.assertTrue(outcome["setup_failed"])

    def test_autocreate_returned_error_response_is_native_only(self):
        # The realistic real-Locust path: a transport failure comes back as a
        # *returned* response with .error set, not a raised exception.
        document = _document(
            metadata={"name": "n", "source": "s/fixtures.xml", "fhirVersion": None},
            fixtures=[_fixture("patient", [{"resourceType": "Patient"}], autocreate=True)],
        )
        user, client = make_user()
        response = client.queue_response(
            fakes.FakeResponse(status_code=0, error=requests.exceptions.ConnectionError("boom"))
        )

        outcome = self._run(document, user, client)

        self.assertFalse(response.success_called)
        native = _events_of_type(user, "POST")
        self.assertEqual(1, len(native))
        self.assertIsNotNone(native[0]["exception"])
        self.assertEqual(0, len(_semantic_events(user)))
        self.assertTrue(outcome["setup_failed"])

    def test_autocreate_extraction_failure_fires_one_semantic_event_and_fails_setup_skipping_tests(self):
        # Review correction: _run_autocreate previously discarded
        # _extract_variables' failure list entirely, so a malformed
        # extraction pinned to the fixture's own response silently
        # succeeded. It must instead fire one source-qualified semantic
        # failure, fail the autocreate (aggregating into setup_failed so
        # tests are skipped), while still letting later variables extract.
        document = _document(
            metadata={"name": "n", "source": "sample.xml", "fhirVersion": None},
            variables=[
                _variable("bad", source_id="patient", extraction_kind="fhirPath", selector="foo("),
                _variable("good", source_id="patient", extraction_kind="path", selector="id"),
            ],
            fixtures=[_fixture("patient", [{"resourceType": "Patient"}], autocreate=True)],
            tests=[_test_phase("test-1", actions=[])],
        )
        user, client = make_user()
        client.queue_response(
            fakes.FakeResponse(status_code=201, json_data={"resourceType": "Patient", "id": "server-1"})
        )
        state = self.runtime.initialize_user(document, user)

        outcome = run_execute(self, self.runtime, document, user, state)

        semantic = _semantic_events(user)
        self.assertEqual(1, len(semantic), "one distinct extraction-failure event, no duplicate")
        self.assertEqual("sample.xml::fixture.patient.autocreate", semantic[0]["name"])
        self.assertIsNotNone(semantic[0]["exception"])

        native = _events_of_type(user, "POST")
        self.assertEqual(1, len(native))
        self.assertIsNone(native[0]["exception"], "the HTTP layer received a 2xx response")

        self.assertTrue(outcome["setup_failed"])
        self.assertEqual(1, len(outcome["tests"]))
        self.assertTrue(outcome["tests"][0]["skipped"])

        context = outcome["context"]
        self.assertNotIn("bad", context["variables"])
        self.assertEqual("server-1", context["variables"]["good"], "later variables still extract")


class FixtureAutodeleteTests(RuntimeOperationsTestCase):
    def _run(self, document, user, client):
        state = self.runtime.initialize_user(document, user)
        return run_execute(self, self.runtime, document, user, state)

    def test_autodelete_uses_current_server_assigned_type_and_id_with_native_success_metric(self):
        document = _document(
            metadata={"name": "n", "source": "s/fixtures.xml", "fhirVersion": None},
            fixtures=[_fixture("patient", [{"resourceType": "Patient", "id": "server-9"}], autodelete=True)],
        )
        user, client = make_user()
        client.queue_response(fakes.FakeResponse(status_code=204))

        outcome = self._run(document, user, client)

        self.assertEqual(1, len(client.calls))
        call = client.calls[0]
        self.assertEqual("DELETE", call["method"])
        self.assertEqual("Patient/server-9", call["url"])
        self.assertEqual("s/fixtures.xml::fixture.patient.autodelete", call["name"])
        self.assertEqual(0, len(_semantic_events(user)))
        self.assertFalse(outcome["teardown_failed"])

    def test_autodelete_missing_id_emits_semantic_failure_without_http_call(self):
        document = _document(
            metadata={"name": "n", "source": "s/fixtures.xml", "fhirVersion": None},
            fixtures=[_fixture("patient", [{"resourceType": "Patient"}], autodelete=True)],
        )
        user, client = make_user()

        outcome = self._run(document, user, client)

        self.assertEqual(0, len(client.calls))
        self.assertEqual(1, len(_semantic_events(user)))
        self.assertTrue(outcome["teardown_failed"])

    def test_autodelete_non_2xx_marks_teardown_failed_with_semantic_event(self):
        document = _document(
            metadata={"name": "n", "source": "s/fixtures.xml", "fhirVersion": None},
            fixtures=[_fixture("patient", [{"resourceType": "Patient", "id": "server-9"}], autodelete=True)],
        )
        user, client = make_user()
        client.queue_response(fakes.FakeResponse(status_code=500))

        outcome = self._run(document, user, client)

        self.assertEqual(1, len(_semantic_events(user)))
        self.assertTrue(outcome["teardown_failed"])

    def test_autodelete_transport_exception_is_native_only(self):
        document = _document(
            metadata={"name": "n", "source": "s/fixtures.xml", "fhirVersion": None},
            fixtures=[_fixture("patient", [{"resourceType": "Patient", "id": "server-9"}], autodelete=True)],
        )
        user, client = make_user()
        client.queue_exception(requests.exceptions.ConnectionError("boom"))

        outcome = self._run(document, user, client)

        native = _events_of_type(user, "DELETE")
        self.assertEqual(1, len(native))
        self.assertIsNotNone(native[0]["exception"])
        self.assertEqual(0, len(_semantic_events(user)))
        self.assertTrue(outcome["teardown_failed"])

    def test_autodelete_returned_error_response_is_native_only(self):
        # The realistic real-Locust path: a transport failure comes back as a
        # *returned* response with .error set, not a raised exception.
        document = _document(
            metadata={"name": "n", "source": "s/fixtures.xml", "fhirVersion": None},
            fixtures=[_fixture("patient", [{"resourceType": "Patient", "id": "server-9"}], autodelete=True)],
        )
        user, client = make_user()
        response = client.queue_response(
            fakes.FakeResponse(status_code=0, error=requests.exceptions.ConnectionError("boom"))
        )

        outcome = self._run(document, user, client)

        self.assertFalse(response.success_called)
        native = _events_of_type(user, "DELETE")
        self.assertEqual(1, len(native))
        self.assertIsNotNone(native[0]["exception"])
        self.assertEqual(0, len(_semantic_events(user)))
        self.assertTrue(outcome["teardown_failed"])


# ---------------------------------------------------------------------------
# Item 18: fixture failures aggregate with setup/teardown without
# short-circuiting remaining lifecycle work.
# ---------------------------------------------------------------------------


class FixtureFailureAggregationTests(RuntimeOperationsTestCase):
    def test_autocreate_failure_attempts_every_fixture_marks_setup_failed_and_skips_tests(self):
        calls = []

        def fake_execute_action(document, user, context, action):
            calls.append(action["id"])
            return {"applicable": True, "failed": False}

        self.runtime._execute_action = fake_execute_action

        document = _document(
            fixtures=[
                _fixture("patient-a", [{"resourceType": "Patient"}], autocreate=True),
                _fixture("patient-b", [{"resourceType": "Patient"}], autocreate=True),
            ],
            tests=[_test_phase("test-1", actions=[])],
        )
        user, client = make_user()
        client.queue_response(fakes.FakeResponse(status_code=500))
        client.queue_response(fakes.FakeResponse(status_code=201, json_data={"resourceType": "Patient", "id": "b1"}))
        state = self.runtime.initialize_user(document, user)

        outcome = run_execute(self, self.runtime, document, user, state)

        self.assertEqual(2, len(client.calls), "both fixtures must be attempted despite the first failing")
        self.assertTrue(outcome["setup_failed"])
        self.assertEqual(1, len(outcome["tests"]))
        self.assertTrue(outcome["tests"][0]["skipped"])

    def test_suite_rejection_performs_zero_fixture_http_requests(self):
        self.runtime._SUITE_ALLOWED = False
        document = _document(
            fixtures=[_fixture("patient", [{"resourceType": "Patient"}], autocreate=True, autodelete=True)],
        )
        user, client = make_user()
        state = self.runtime.initialize_user(document, user)

        outcome = run_execute(self, self.runtime, document, user, state)

        self.assertEqual(0, len(client.calls))
        self.assertTrue(outcome["suite_skipped"])


# ---------------------------------------------------------------------------
# Item 19: waitFor polling.
# ---------------------------------------------------------------------------


class WaitForPollingTests(RuntimeOperationsTestCase):
    def test_waitfor_polls_until_status_code_matches_then_stops_early(self):
        document = _document(metadata={"name": "n", "source": "s/poll.xml", "fhirVersion": None})
        user, client = make_user()
        context = new_context(self.runtime, document)
        client.queue_response(fakes.FakeResponse(status_code=202))
        client.queue_response(fakes.FakeResponse(status_code=202))
        client.queue_response(fakes.FakeResponse(status_code=200, json_data={"resourceType": "Bundle"}))

        action = _operation(
            "op-1", type="read", resource="Patient/_history/1", request_id="op-1", response_id="op-1",
            wait_for=_wait_for(202, 5, 100),
        )

        with patch("gevent.sleep") as sleep_mock:
            result = run_operation(self, self.runtime, document, user, context, action)

        self.assertEqual(3, len(client.calls))
        self.assertEqual(2, sleep_mock.call_count, "sleep happens between attempts, not after the final one")
        for call in sleep_mock.call_args_list:
            self.assertAlmostEqual(0.1, call.args[0])
        native = _events_of_type(user, "GET")
        for event in native:
            self.assertEqual("s/poll.xml::op-1", event["name"])
        self.assertFalse(result.get("failed", False))
        self.assertIs(context["responses"]["op-1"], context["last_response"])
        self.assertEqual(200, context["last_response"].status_code)

    def test_waitfor_exhaustion_emits_exactly_one_semantic_failure_and_keeps_final_response(self):
        document = _document(metadata={"name": "n", "source": "s/poll.xml", "fhirVersion": None})
        user, client = make_user()
        context = new_context(self.runtime, document)
        for _ in range(3):
            client.queue_response(fakes.FakeResponse(status_code=202))

        action = _operation(
            "op-1", type="read", resource="Patient/_history/1", request_id="op-1", response_id="op-1",
            wait_for=_wait_for(202, 3, 10),
        )

        with patch("gevent.sleep"):
            result = run_operation(self, self.runtime, document, user, context, action)

        self.assertEqual(3, len(client.calls))
        self.assertTrue(result.get("failed", False))
        semantic = _semantic_events(user)
        self.assertEqual(1, len(semantic))
        # Review correction: message must match the evaluator's exact wording,
        # not merely convey the same idea in different words.
        self.assertEqual(
            "Timed out waiting for job completion after 3 attempts (last status: 202)",
            str(semantic[0]["exception"]),
        )
        self.assertEqual(202, context["last_response"].status_code)

    def test_waitfor_stops_on_transport_exception_without_semantic_duplicate(self):
        document = _document(metadata={"name": "n", "source": "s/poll.xml", "fhirVersion": None})
        user, client = make_user()
        context = new_context(self.runtime, document)
        client.queue_response(fakes.FakeResponse(status_code=202))
        client.queue_exception(requests.exceptions.ConnectionError("boom"))

        action = _operation(
            "op-1", type="read", resource="Patient/_history/1", request_id="op-1", response_id="op-1",
            wait_for=_wait_for(202, 5, 10),
        )

        with patch("gevent.sleep"):
            result = run_operation(self, self.runtime, document, user, context, action)

        self.assertEqual(2, len(client.calls))
        self.assertTrue(result.get("failed", False))
        self.assertEqual(0, len(_semantic_events(user)), "no semantic duplicate for a native transport failure")
        native = _events_of_type(user, "GET")
        self.assertEqual(1, len([e for e in native if e["exception"] is not None]))

    def test_waitfor_stops_on_returned_error_response_without_semantic_duplicate(self):
        # The realistic real-Locust path: the second polling attempt comes
        # back as a *returned* response with .error set, not a raised
        # exception; polling must still stop with exactly one native failure
        # and no semantic duplicate.
        document = _document(metadata={"name": "n", "source": "s/poll.xml", "fhirVersion": None})
        user, client = make_user()
        context = new_context(self.runtime, document)
        client.queue_response(fakes.FakeResponse(status_code=202))
        error_response = client.queue_response(
            fakes.FakeResponse(status_code=0, error=requests.exceptions.ConnectionError("boom"))
        )

        action = _operation(
            "op-1", type="read", resource="Patient/_history/1", request_id="op-1", response_id="op-1",
            wait_for=_wait_for(202, 5, 10),
        )

        with patch("gevent.sleep"):
            result = run_operation(self, self.runtime, document, user, context, action)

        self.assertEqual(2, len(client.calls))
        self.assertFalse(error_response.success_called)
        self.assertTrue(result.get("failed", False))
        self.assertEqual(0, len(_semantic_events(user)), "no semantic duplicate for a native transport failure")
        native = _events_of_type(user, "GET")
        self.assertEqual(1, len([e for e in native if e["exception"] is not None]))




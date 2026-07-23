import os
import sys
import types
import unittest
from unittest.mock import patch

import fakes


def _document(**overrides):
    base = {
        "schemaVersion": "1.0",
        "metadata": {"name": "Sample", "source": "sample.xml"},
        "variables": [],
        "fixtures": [],
        "setup": [],
        "tests": [],
        "teardown": [],
    }
    base.update(overrides)
    return base


def _operation(action_id, type="read", method="GET", **kwargs):
    action = {"id": action_id, "kind": "operation", "type": type, "method": method}
    action.update(kwargs)
    return action


def _header(field, value):
    return {"field": field, "value": value}


def _variable(name, extraction_kind="none", selector=None, source_id=None, default_value=None):
    return {
        "name": name,
        "defaultValue": default_value,
        "sourceId": source_id,
        "extractionKind": extraction_kind,
        "selector": selector,
    }


def _fixture(fixture_id, autocreate=False, autodelete=False, variants=None):
    return {
        "id": fixture_id,
        "autocreate": autocreate,
        "autodelete": autodelete,
        "variants": variants or [{"resourceType": "Patient"}],
    }


def _new_context(runtime, document):
    return runtime._new_context(document, {"iteration": 0, "ordinal": 0})


def _semantic_events(items):
    return [item for item in items if item["request_type"] == "TESTSCRIPT_OPERATION"]


class _FakeGeventMixin:
    """Shared helper mixin for gevent sleep faking (not a test case itself)."""

    def _install_fake_gevent(self):
        fake_module = types.ModuleType("gevent")
        sleeps = []
        fake_module.sleep = lambda seconds: sleeps.append(seconds)
        patcher = patch.dict(sys.modules, {"gevent": fake_module})
        patcher.start()
        self.addCleanup(patcher.stop)
        return sleeps


class OperationRequestConstructionTests(unittest.TestCase):
    def setUp(self):
        self.runtime = fakes.load_runtime()

    # ------------------------------------------------------------------
    # 1. CRUD/compiler-equivalent methods, derived URLs, explicit URL,
    #    variable substitution.
    # ------------------------------------------------------------------

    def test_read_url_uses_resource_and_resolved_params(self):
        document = _document()
        context = _new_context(self.runtime, document)
        context["variables"]["patientId"] = "123"
        action = _operation("op-1", type="read", method="GET", resource="Patient", params="/${patientId}")

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=200, content=b"{}"))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        self.assertEqual("Patient/123", client.calls[0]["url"])
        self.assertEqual("GET", client.calls[0]["method"])

    def test_create_url_is_bare_resource_type(self):
        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation("op-1", type="create", method="POST", resource="Patient")

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=201, content=b"{}"))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        self.assertEqual("Patient", client.calls[0]["url"])
        self.assertEqual("POST", client.calls[0]["method"])

    def test_custom_fhir_operation_url_includes_resource_and_dollar_type(self):
        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation("op-1", type="$validate", method="POST", resource="Patient")

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=200, content=b"{}"))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        self.assertEqual("Patient/$validate", client.calls[0]["url"])

    def test_custom_fhir_operation_url_without_resource_is_system_level(self):
        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation("op-1", type="$validate", method="POST")

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=200, content=b"{}"))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        self.assertEqual("$validate", client.calls[0]["url"])

    def test_explicit_url_resolves_variables_and_wins_over_resource(self):
        document = _document()
        context = _new_context(self.runtime, document)
        context["variables"]["base"] = "Observation"
        action = _operation("op-1", type="read", method="GET", resource="Patient", url="${base}/1")

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=200, content=b"{}"))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        self.assertEqual("Observation/1", client.calls[0]["url"])

    def test_search_get_uses_resource_and_params_url(self):
        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation("op-1", type="search", method="GET", resource="Patient", params="?name=Smith")

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=200, content=b"{}"))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        self.assertEqual("Patient?name=Smith", client.calls[0]["url"])

    # ------------------------------------------------------------------
    # 2. POST search strips one/multiple '?', UTF-8 form body, forced
    #    content type.
    # ------------------------------------------------------------------

    def test_post_search_uses_search_url_and_strips_single_question_mark(self):
        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation(
            "op-1", type="search", method="POST", resource="Patient", params="?name=Smith",
            contentType="text/plain",
        )

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=200, content=b"{}"))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        call = client.calls[0]
        self.assertEqual("Patient/_search", call["url"])
        self.assertEqual(b"name=Smith", call["data"])
        self.assertEqual(
            "application/x-www-form-urlencoded; charset=utf-8", call["headers"]["Content-Type"]
        )

    def test_post_search_strips_multiple_leading_question_marks(self):
        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation(
            "op-1", type="search", method="POST", resource="Patient", params="??name=Smith"
        )

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=200, content=b"{}"))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        self.assertEqual(b"name=Smith", client.calls[0]["data"])

    # ------------------------------------------------------------------
    # 3. Auth header parsing/application/override.
    # ------------------------------------------------------------------

    def test_parse_auth_header_unset_returns_none(self):
        with patch.dict(os.environ, {}, clear=False):
            os.environ.pop("IGNIXA_AUTH_HEADER", None)
            self.assertIsNone(self.runtime._parse_auth_header())

    def test_parse_auth_header_parses_name_and_value_allowing_colons_in_value(self):
        with patch.dict(os.environ, {"IGNIXA_AUTH_HEADER": "Authorization: Bearer abc:def"}):
            self.assertEqual(("Authorization", "Bearer abc:def"), self.runtime._parse_auth_header())

    def test_parse_auth_header_rejects_missing_colon(self):
        with patch.dict(os.environ, {"IGNIXA_AUTH_HEADER": "Authorization"}):
            with self.assertRaises(RuntimeError):
                self.runtime._parse_auth_header()

    def test_parse_auth_header_rejects_empty_name(self):
        with patch.dict(os.environ, {"IGNIXA_AUTH_HEADER": ": value"}):
            with self.assertRaises(RuntimeError):
                self.runtime._parse_auth_header()

    def test_parse_auth_header_rejects_empty_value(self):
        with patch.dict(os.environ, {"IGNIXA_AUTH_HEADER": "Name:"}):
            with self.assertRaises(RuntimeError):
                self.runtime._parse_auth_header()

    def test_auth_header_is_applied_to_request(self):
        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation("op-1", type="read", method="GET", resource="Patient")

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=200, content=b"{}"))
        user = fakes.FakeUser(client)

        with patch.dict(os.environ, {"IGNIXA_AUTH_HEADER": "Authorization: Bearer xyz"}):
            self.runtime._execute_operation(document, user, context, action)

        self.assertEqual("Bearer xyz", client.calls[0]["headers"]["Authorization"])

    def test_explicit_testscript_header_overrides_auth_case_insensitively(self):
        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation(
            "op-1", type="read", method="GET", resource="Patient",
            headers=[_header("authorization", "Bearer script-value")],
        )

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=200, content=b"{}"))
        user = fakes.FakeUser(client)

        with patch.dict(os.environ, {"IGNIXA_AUTH_HEADER": "Authorization: Bearer env-value"}):
            self.runtime._execute_operation(document, user, context, action)

        self.assertEqual("Bearer script-value", client.calls[0]["headers"]["authorization"])

    # ------------------------------------------------------------------
    # 4. Resolved headers; undefined variable -> one semantic failure/no HTTP.
    # ------------------------------------------------------------------

    def test_header_value_variable_substitution(self):
        document = _document()
        context = _new_context(self.runtime, document)
        context["variables"]["token"] = "abc123"
        action = _operation(
            "op-1", type="read", method="GET", resource="Patient",
            headers=[_header("X-Trace", "trace-${token}")],
        )

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=200, content=b"{}"))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        self.assertEqual("trace-abc123", client.calls[0]["headers"]["X-Trace"])

    def test_undefined_variable_in_header_is_one_semantic_failure_with_no_http(self):
        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation(
            "op-1", type="read", method="GET", resource="Patient",
            headers=[_header("X-Trace", "${missing}")],
        )

        client = fakes.FakeClient()
        user = fakes.FakeUser(client)

        result = self.runtime._execute_operation(document, user, context, action)

        self.assertTrue(result["failed"])
        self.assertEqual([], client.calls)
        events = user.environment.events.request.items
        self.assertEqual(1, len(events))
        self.assertEqual("TESTSCRIPT_OPERATION", events[0]["request_type"])
        self.assertEqual("sample.xml::op-1", events[0]["name"])
        self.assertEqual(0, events[0]["response_time"])
        self.assertEqual(0, events[0]["response_length"])
        self.assertIn("missing", str(events[0]["exception"]))

    # ------------------------------------------------------------------
    # 5. sourceId body resolution, unknown source failure, content-type
    #    rules.
    # ------------------------------------------------------------------

    def test_source_id_from_fixture_serializes_compact_json_with_default_content_type(self):
        document = _document()
        context = _new_context(self.runtime, document)
        context["fixtures"]["patient-fixture"] = {"resourceType": "Patient", "active": True}
        action = _operation(
            "op-1", type="create", method="POST", resource="Patient", sourceId="patient-fixture"
        )

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=201, content=b"{}"))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        call = client.calls[0]
        self.assertEqual(b'{"resourceType":"Patient","active":true}', call["data"])
        self.assertEqual("application/fhir+json; charset=utf-8", call["headers"]["Content-Type"])

    def test_source_id_from_prior_response_body(self):
        document = _document()
        context = _new_context(self.runtime, document)
        action1 = _operation(
            "op-1", type="create", method="POST", resource="Patient", responseId="created"
        )
        client = fakes.FakeClient()
        client.queue_response(
            fakes.FakeResponse(status_code=201, content=b'{"resourceType":"Patient","id":"42"}')
        )
        user = fakes.FakeUser(client)
        self.runtime._execute_operation(document, user, context, action1)

        action2 = _operation(
            "op-2", type="update", method="PUT", resource="Patient", sourceId="created"
        )
        client.queue_response(fakes.FakeResponse(status_code=200, content=b"{}"))
        self.runtime._execute_operation(document, user, context, action2)

        call = client.calls[1]
        self.assertEqual(b'{"resourceType":"Patient","id":"42"}', call["data"])

    def test_unknown_source_id_is_one_semantic_failure_with_no_http(self):
        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation(
            "op-1", type="create", method="POST", resource="Patient", sourceId="does-not-exist"
        )

        client = fakes.FakeClient()
        user = fakes.FakeUser(client)

        result = self.runtime._execute_operation(document, user, context, action)

        self.assertTrue(result["failed"])
        self.assertEqual([], client.calls)
        events = user.environment.events.request.items
        self.assertEqual(1, len(events))
        self.assertEqual("TESTSCRIPT_OPERATION", events[0]["request_type"])
        self.assertIn("does-not-exist", str(events[0]["exception"]))

    def test_explicit_content_type_used_for_json_body_when_present(self):
        document = _document()
        context = _new_context(self.runtime, document)
        context["fixtures"]["f1"] = {"resourceType": "Patient"}
        action = _operation(
            "op-1", type="create", method="POST", resource="Patient", sourceId="f1",
            contentType="application/json; fhirVersion=4.0",
        )

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=201, content=b"{}"))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        self.assertEqual(
            "application/json; fhirVersion=4.0", client.calls[0]["headers"]["Content-Type"]
        )

    def test_no_body_removes_content_type_and_preserves_accept(self):
        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation(
            "op-1", type="read", method="GET", resource="Patient",
            accept="application/fhir+json", contentType="application/fhir+json",
        )

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=200, content=b"{}"))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        call = client.calls[0]
        self.assertNotIn("Content-Type", call["headers"])
        self.assertEqual("application/fhir+json", call["headers"]["Accept"])
        self.assertIsNone(call["data"])

    # ------------------------------------------------------------------
    # 6. Request/response history and last wrappers/IDs.
    # ------------------------------------------------------------------

    def test_request_response_stored_under_ids_and_as_last(self):
        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation(
            "op-1", type="create", method="POST", resource="Patient",
            requestId="req-1", responseId="resp-1",
        )

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=201, content=b'{"id":"1"}'))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        self.assertIn("req-1", context["requests"])
        self.assertIn("resp-1", context["responses"])
        self.assertIs(context["requests"]["req-1"], context["last_request"])
        self.assertIs(context["responses"]["resp-1"], context["last_response"])
        self.assertEqual("Patient", context["last_request"]["url"])
        self.assertEqual(201, context["last_response"]["status_code"])

    def test_request_response_without_ids_only_update_last(self):
        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation("op-1", type="create", method="POST", resource="Patient")

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=201, content=b"{}"))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        self.assertEqual({}, context["requests"])
        self.assertEqual({}, context["responses"])
        self.assertIsNotNone(context["last_request"])
        self.assertIsNotNone(context["last_response"])

    def test_second_send_replaces_last_request_and_response(self):
        document = _document()
        context = _new_context(self.runtime, document)
        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=200, content=b"{}"))
        client.queue_response(fakes.FakeResponse(status_code=201, content=b"{}"))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(
            document, user, context, _operation("op-1", type="read", method="GET", resource="Patient")
        )
        first_request = context["last_request"]
        first_response = context["last_response"]

        self.runtime._execute_operation(
            document, user, context, _operation("op-2", type="create", method="POST", resource="Patient")
        )

        self.assertIsNot(first_request, context["last_request"])
        self.assertIsNot(first_response, context["last_response"])
        self.assertEqual(201, context["last_response"]["status_code"])

    # ------------------------------------------------------------------
    # 7. Metric naming.
    # ------------------------------------------------------------------

    def test_metric_name_is_source_qualified(self):
        document = _document(metadata={"name": "n", "source": "path/to/script.xml"})
        self.assertEqual(
            "path/to/script.xml::setup.0", self.runtime._metric_name(document, "setup.0")
        )

    def test_http_request_uses_source_qualified_metric_name(self):
        document = _document(metadata={"name": "n", "source": "my/script.xml"})
        context = _new_context(self.runtime, document)
        action = _operation("test.0.action.0", type="read", method="GET", resource="Patient")

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=200, content=b"{}"))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        self.assertEqual("my/script.xml::test.0.action.0", client.calls[0]["name"])

    def test_fixture_autocreate_and_autodelete_use_dotted_metric_ids(self):
        document = _document(metadata={"name": "n", "source": "my/script.xml"})
        context = _new_context(self.runtime, document)
        context["fixtures"]["patient"] = {"resourceType": "Patient"}
        fixture = _fixture("patient", autocreate=True)

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=201, content=b'{"resourceType":"Patient","id":"1"}'))
        user = fakes.FakeUser(client)

        self.runtime._autocreate_fixture(document, user, context, fixture)

        self.assertEqual("my/script.xml::fixture.patient.autocreate", client.calls[0]["name"])

        client.queue_response(fakes.FakeResponse(status_code=204, content=b""))
        self.runtime._autodelete_fixture(document, user, context, fixture)

        self.assertEqual("my/script.xml::fixture.patient.autodelete", client.calls[1]["name"])


class OperationNativeVsSemanticFailureTests(unittest.TestCase):
    def setUp(self):
        self.runtime = fakes.load_runtime()

    # ------------------------------------------------------------------
    # 8. Received 4xx/5xx call success() and do not fail/fire a semantic
    #    event.
    # ------------------------------------------------------------------

    def test_received_404_is_ordinary_operation_success_and_calls_response_success(self):
        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation("op-1", type="read", method="GET", resource="Patient")

        response = fakes.FakeResponse(status_code=404, content=b'{"issue":"not-found"}')
        client = fakes.FakeClient()
        client.queue_response(response)
        user = fakes.FakeUser(client)

        result = self.runtime._execute_operation(document, user, context, action)

        self.assertFalse(result["failed"])
        self.assertTrue(response.success_called)
        # A native HTTP event still fires (Locust always records the attempt
        # for load statistics) but as a success, with no semantic event.
        events = user.environment.events.request.items
        self.assertEqual(1, len(events))
        self.assertIsNone(events[0]["exception"])
        self.assertEqual([], _semantic_events(events))

    def test_received_500_is_ordinary_operation_success_and_calls_response_success(self):
        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation("op-1", type="read", method="GET", resource="Patient")

        response = fakes.FakeResponse(status_code=500, content=b"")
        client = fakes.FakeClient()
        client.queue_response(response)
        user = fakes.FakeUser(client)

        result = self.runtime._execute_operation(document, user, context, action)

        self.assertFalse(result["failed"])
        self.assertTrue(response.success_called)
        events = user.environment.events.request.items
        self.assertEqual(1, len(events))
        self.assertIsNone(events[0]["exception"])
        self.assertEqual([], _semantic_events(events))

    # ------------------------------------------------------------------
    # 9. Transport exceptions/error responses remain native failures only.
    # ------------------------------------------------------------------

    def test_raised_transport_exception_is_native_failure_only(self):
        # A defensive/synthetic case: the fake's ``request()`` raises directly
        # (rather than returning an errored response), so the response
        # context manager is never entered/exited and no event -- native or
        # semantic -- fires at all. Real Locust's own safe-mode request
        # wrapper normally converts connection-type errors into an errored
        # *response* instead (see the next test), but this runtime still
        # defends against an outright raise without emitting a duplicate
        # semantic event.
        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation("op-1", type="read", method="GET", resource="Patient")

        client = fakes.FakeClient()
        client.queue_transport_exception(ConnectionError("boom"))
        user = fakes.FakeUser(client)

        result = self.runtime._execute_operation(document, user, context, action)

        self.assertTrue(result["failed"])
        self.assertEqual([], user.environment.events.request.items)

    def test_returned_locust_error_response_is_native_failure_only(self):
        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation("op-1", type="read", method="GET", resource="Patient")

        response = fakes.FakeResponse(status_code=0, content=b"", error=ConnectionError("dead"))
        client = fakes.FakeClient()
        client.queue_response(response)
        user = fakes.FakeUser(client)

        result = self.runtime._execute_operation(document, user, context, action)

        self.assertTrue(result["failed"])
        self.assertFalse(response.success_called)
        # No manual success()/failure() call: the fake's __exit__ mirrors real
        # Locust's default raise_for_status() fallback and fires exactly one
        # native failure using the transport error -- never a duplicate
        # TESTSCRIPT_OPERATION event from the runtime.
        events = user.environment.events.request.items
        self.assertEqual(1, len(events))
        self.assertIsNotNone(events[0]["exception"])
        self.assertNotEqual("TESTSCRIPT_OPERATION", events[0]["request_type"])

    # ------------------------------------------------------------------
    # 10. encodeRequestUrl=false logs a warning and still sends the request.
    # ------------------------------------------------------------------

    def test_encode_request_url_false_logs_warning_and_still_sends(self):
        document = _document(metadata={"name": "n", "source": "my/script.xml"})
        context = _new_context(self.runtime, document)
        action = _operation(
            "op-1", type="read", method="GET", resource="Patient", encodeRequestUrl=False
        )

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=200, content=b"{}"))
        user = fakes.FakeUser(client)

        with self.assertLogs("ignixa.testscript", level="WARNING") as log_ctx:
            result = self.runtime._execute_operation(document, user, context, action)

        self.assertFalse(result["failed"])
        self.assertEqual(1, len(client.calls))
        joined = " ".join(log_ctx.output)
        self.assertIn("my/script.xml::op-1", joined)
        self.assertIn("encodeRequestUrl=false is not supported", joined)
        self.assertIn("URL was encoded", joined)
        self.assertEqual([], _semantic_events(user.environment.events.request.items))


class VariableExtractionTests(unittest.TestCase):
    def setUp(self):
        self.runtime = fakes.load_runtime()

    # ------------------------------------------------------------------
    # 11. Header/dotted-path/FHIRPath extraction.
    # ------------------------------------------------------------------

    def test_header_extraction_is_case_insensitive(self):
        document = _document(variables=[_variable("etag", "header", selector="etag")])
        context = _new_context(self.runtime, document)
        action = _operation("op-1", type="read", method="GET", resource="Patient")

        client = fakes.FakeClient()
        client.queue_response(
            fakes.FakeResponse(status_code=200, headers={"ETag": "W/\"1\""}, content=b"{}")
        )
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        self.assertEqual('W/"1"', context["variables"]["etag"])

    def test_header_extraction_missing_header_is_no_op(self):
        document = _document(variables=[_variable("etag", "header", selector="etag")])
        context = _new_context(self.runtime, document)
        action = _operation("op-1", type="read", method="GET", resource="Patient")

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=200, content=b"{}"))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        self.assertNotIn("etag", context["variables"])

    def test_dotted_path_extracts_string_number_bool_object_array(self):
        document = _document(
            variables=[
                _variable("v_str", "path", selector="name"),
                _variable("v_num", "path", selector="count"),
                _variable("v_bool", "path", selector="active"),
                _variable("v_obj", "path", selector="meta"),
                _variable("v_arr", "path", selector="tags"),
                _variable("v_nested", "path", selector="meta.tag"),
            ]
        )
        context = _new_context(self.runtime, document)
        action = _operation("op-1", type="read", method="GET", resource="Patient")

        body = (
            b'{"name":"Smith","count":3,"active":true,'
            b'"meta":{"tag":"x"},"tags":["a","b"]}'
        )
        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=200, content=body))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        self.assertEqual("Smith", context["variables"]["v_str"])
        self.assertEqual("3", context["variables"]["v_num"])
        self.assertEqual("true", context["variables"]["v_bool"])
        self.assertEqual('{"tag":"x"}', context["variables"]["v_obj"])
        self.assertEqual('["a","b"]', context["variables"]["v_arr"])
        self.assertEqual("x", context["variables"]["v_nested"])

    def test_dotted_path_does_not_traverse_array_indices(self):
        document = _document(variables=[_variable("v", "path", selector="tags.0")])
        context = _new_context(self.runtime, document)
        action = _operation("op-1", type="read", method="GET", resource="Patient")

        client = fakes.FakeClient()
        client.queue_response(
            fakes.FakeResponse(status_code=200, content=b'{"tags":["a","b"]}')
        )
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        self.assertNotIn("v", context["variables"])

    def test_dotted_path_missing_key_is_no_op(self):
        document = _document(variables=[_variable("v", "path", selector="missing.deeper")])
        context = _new_context(self.runtime, document)
        action = _operation("op-1", type="read", method="GET", resource="Patient")

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=200, content=b'{"name":"x"}'))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        self.assertNotIn("v", context["variables"])

    def test_fhirpath_extraction_scalar_and_boolean(self):
        document = _document(
            variables=[
                _variable("pid", "fhirPath", selector="Patient.id"),
                _variable("pactive", "fhirPath", selector="Patient.active"),
            ]
        )
        context = _new_context(self.runtime, document)
        action = _operation("op-1", type="read", method="GET", resource="Patient")

        client = fakes.FakeClient()
        client.queue_response(
            fakes.FakeResponse(
                status_code=200,
                content=b'{"resourceType":"Patient","id":"42","active":true}',
            )
        )
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        self.assertEqual("42", context["variables"]["pid"])
        self.assertEqual("true", context["variables"]["pactive"])

    def test_fhirpath_extraction_empty_and_multi_value_are_no_ops(self):
        document = _document(
            variables=[
                _variable("v_empty", "fhirPath", selector="Patient.name"),
                _variable("v_multi", "fhirPath", selector="Patient.name.given"),
            ]
        )
        context = _new_context(self.runtime, document)
        action = _operation("op-1", type="read", method="GET", resource="Patient")

        body = (
            b'{"resourceType":"Patient",'
            b'"name":[{"given":["A"]},{"given":["B"]}]}'
        )
        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=200, content=body))
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(document, user, context, action)

        self.assertNotIn("v_multi", context["variables"])

    def test_malformed_fhirpath_extraction_is_one_semantic_failure_and_fails_operation(self):
        document = _document(
            variables=[
                _variable("v_bad", "fhirPath", selector="Patient.name.given["),
                _variable("v_after", "path", selector="id"),
            ]
        )
        context = _new_context(self.runtime, document)
        action = _operation("op-1", type="read", method="GET", resource="Patient")

        client = fakes.FakeClient()
        client.queue_response(
            fakes.FakeResponse(status_code=200, content=b'{"resourceType":"Patient","id":"7"}')
        )
        user = fakes.FakeUser(client)

        result = self.runtime._execute_operation(document, user, context, action)

        self.assertTrue(result["failed"])
        # extraction continues for variables after the malformed one
        self.assertEqual("7", context["variables"]["v_after"])
        semantic_events = _semantic_events(user.environment.events.request.items)
        self.assertEqual(1, len(semantic_events))
        self.assertEqual("sample.xml::op-1", semantic_events[0]["name"])

    def test_extraction_uses_source_id_response_not_last_response(self):
        document = _document(
            variables=[_variable("v", "path", selector="id", source_id="resp-a")]
        )
        context = _new_context(self.runtime, document)

        client = fakes.FakeClient()
        client.queue_response(
            fakes.FakeResponse(status_code=200, content=b'{"id":"from-a"}')
        )
        client.queue_response(
            fakes.FakeResponse(status_code=200, content=b'{"id":"from-b"}')
        )
        user = fakes.FakeUser(client)

        self.runtime._execute_operation(
            document, user, context,
            _operation("op-a", type="read", method="GET", resource="Patient", responseId="resp-a"),
        )
        self.runtime._execute_operation(
            document, user, context,
            _operation("op-b", type="read", method="GET", resource="Patient", responseId="resp-b"),
        )

        self.assertEqual("from-a", context["variables"]["v"])


class FixtureLifecycleTests(unittest.TestCase):
    def setUp(self):
        self.runtime = fakes.load_runtime()

    # ------------------------------------------------------------------
    # 12. Fixture autocreate.
    # ------------------------------------------------------------------

    def test_autocreate_posts_resource_type_and_replaces_fixture_with_server_json(self):
        document = _document()
        context = _new_context(self.runtime, document)
        context["fixtures"]["patient"] = {"resourceType": "Patient", "active": True}
        fixture = _fixture("patient", autocreate=True)

        client = fakes.FakeClient()
        client.queue_response(
            fakes.FakeResponse(status_code=201, content=b'{"resourceType":"Patient","id":"srv-1"}')
        )
        user = fakes.FakeUser(client)

        result = self.runtime._autocreate_fixture(document, user, context, fixture)

        self.assertFalse(result["failed"])
        self.assertEqual("POST", client.calls[0]["method"])
        self.assertEqual("Patient", client.calls[0]["url"])
        self.assertEqual({"resourceType": "Patient", "id": "srv-1"}, context["fixtures"]["patient"])

    def test_autocreate_stores_response_under_fixture_id_and_last_and_runs_extraction(self):
        document = _document(
            variables=[_variable("patientId", "path", selector="id", source_id="patient")]
        )
        context = _new_context(self.runtime, document)
        context["fixtures"]["patient"] = {"resourceType": "Patient"}
        fixture = _fixture("patient", autocreate=True)

        client = fakes.FakeClient()
        client.queue_response(
            fakes.FakeResponse(status_code=201, content=b'{"resourceType":"Patient","id":"srv-2"}')
        )
        user = fakes.FakeUser(client)

        self.runtime._autocreate_fixture(document, user, context, fixture)

        self.assertIn("patient", context["responses"])
        self.assertIs(context["responses"]["patient"], context["last_response"])
        self.assertEqual("srv-2", context["variables"]["patientId"])

    def test_autocreate_non_2xx_is_one_semantic_failure(self):
        document = _document()
        context = _new_context(self.runtime, document)
        context["fixtures"]["patient"] = {"resourceType": "Patient"}
        fixture = _fixture("patient", autocreate=True)

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=422, content=b"{}"))
        user = fakes.FakeUser(client)

        result = self.runtime._autocreate_fixture(document, user, context, fixture)

        self.assertTrue(result["failed"])
        semantic_events = _semantic_events(user.environment.events.request.items)
        self.assertEqual(1, len(semantic_events))
        self.assertEqual("sample.xml::fixture.patient.autocreate", semantic_events[0]["name"])

    def test_autocreate_missing_resource_type_is_semantic_failure_with_no_http(self):
        document = _document()
        context = _new_context(self.runtime, document)
        context["fixtures"]["patient"] = {"active": True}
        fixture = _fixture("patient", autocreate=True)

        client = fakes.FakeClient()
        user = fakes.FakeUser(client)

        result = self.runtime._autocreate_fixture(document, user, context, fixture)

        self.assertTrue(result["failed"])
        self.assertEqual([], client.calls)

    # ------------------------------------------------------------------
    # 13. Fixture autodelete.
    # ------------------------------------------------------------------

    def test_autodelete_uses_server_assigned_id_after_autocreate(self):
        document = _document()
        context = _new_context(self.runtime, document)
        context["fixtures"]["patient"] = {"resourceType": "Patient"}
        fixture = _fixture("patient", autocreate=True, autodelete=True)

        client = fakes.FakeClient()
        client.queue_response(
            fakes.FakeResponse(status_code=201, content=b'{"resourceType":"Patient","id":"srv-9"}')
        )
        user = fakes.FakeUser(client)
        self.runtime._autocreate_fixture(document, user, context, fixture)

        client.queue_response(fakes.FakeResponse(status_code=204, content=b""))
        result = self.runtime._autodelete_fixture(document, user, context, fixture)

        self.assertFalse(result["failed"])
        self.assertEqual("DELETE", client.calls[1]["method"])
        self.assertEqual("Patient/srv-9", client.calls[1]["url"])

    def test_autodelete_missing_id_is_semantic_failure_with_no_http(self):
        document = _document()
        context = _new_context(self.runtime, document)
        context["fixtures"]["patient"] = {"resourceType": "Patient"}
        fixture = _fixture("patient", autodelete=True)

        client = fakes.FakeClient()
        user = fakes.FakeUser(client)

        result = self.runtime._autodelete_fixture(document, user, context, fixture)

        self.assertTrue(result["failed"])
        self.assertEqual([], client.calls)
        events = user.environment.events.request.items
        self.assertEqual(1, len(events))
        self.assertEqual("TESTSCRIPT_OPERATION", events[0]["request_type"])

    def test_autodelete_missing_resource_type_is_semantic_failure_with_no_http(self):
        document = _document()
        context = _new_context(self.runtime, document)
        context["fixtures"]["patient"] = {"id": "1"}
        fixture = _fixture("patient", autodelete=True)

        client = fakes.FakeClient()
        user = fakes.FakeUser(client)

        result = self.runtime._autodelete_fixture(document, user, context, fixture)

        self.assertTrue(result["failed"])
        self.assertEqual([], client.calls)

    def test_autodelete_non_2xx_is_one_semantic_failure(self):
        document = _document()
        context = _new_context(self.runtime, document)
        context["fixtures"]["patient"] = {"resourceType": "Patient", "id": "1"}
        fixture = _fixture("patient", autodelete=True)

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=409, content=b"{}"))
        user = fakes.FakeUser(client)

        result = self.runtime._autodelete_fixture(document, user, context, fixture)

        self.assertTrue(result["failed"])
        semantic_events = _semantic_events(user.environment.events.request.items)
        self.assertEqual(1, len(semantic_events))
        self.assertEqual("sample.xml::fixture.patient.autodelete", semantic_events[0]["name"])

    # ------------------------------------------------------------------
    # 14. Fixture lifecycle integration with setup/teardown aggregation.
    # ------------------------------------------------------------------

    def test_autocreate_failures_aggregate_into_setup_but_all_actions_still_run(self):
        document = _document(
            fixtures=[
                _fixture("bad", autocreate=True, variants=[{"active": True}]),
                _fixture("good", autocreate=True, variants=[{"resourceType": "Patient"}]),
            ],
            setup=[{"id": "setup.0", "kind": "operation"}],
        )

        calls = []

        def fake_execute_action(document, user, context, action):
            calls.append(action["id"])
            return {"applicable": True, "failed": False}

        self.runtime._execute_action = fake_execute_action

        client = fakes.FakeClient()
        client.queue_response(
            fakes.FakeResponse(status_code=201, content=b'{"resourceType":"Patient","id":"1"}')
        )
        user = fakes.FakeUser(client)
        state = self.runtime.initialize_user(document, user)

        outcome = self.runtime.execute(document, user, state)

        # "bad" has no resourceType (semantic failure, no HTTP); "good" still
        # autocreates over HTTP, and the explicit setup action still runs.
        self.assertTrue(outcome["setup_failed"])
        self.assertEqual(["setup.0"], calls)
        self.assertEqual(1, len(client.calls))
        self.assertEqual({"resourceType": "Patient", "id": "1"}, outcome["context"]["fixtures"]["good"])

    def test_autodelete_failures_aggregate_into_teardown_but_all_actions_still_run(self):
        document = _document(
            fixtures=[_fixture("orphan", autodelete=True, variants=[{"resourceType": "Patient"}])],
            teardown=[{"id": "teardown.0", "kind": "operation"}],
        )

        calls = []

        def fake_execute_action(document, user, context, action):
            calls.append(action["id"])
            return {"applicable": True, "failed": False}

        self.runtime._execute_action = fake_execute_action

        client = fakes.FakeClient()
        user = fakes.FakeUser(client)
        state = self.runtime.initialize_user(document, user)

        outcome = self.runtime.execute(document, user, state)

        # The fixture was never autocreated in this test, so it has no
        # server-assigned id -- a semantic failure with no HTTP attempt --
        # but the explicit teardown action still runs and is aggregated.
        self.assertTrue(outcome["teardown_failed"])
        self.assertEqual(["teardown.0"], calls)
        self.assertEqual([], client.calls)

    def test_suite_rejection_prevents_all_fixture_requests(self):
        document = _document(
            fixtures=[_fixture("patient", autocreate=True, autodelete=True)],
        )
        self.runtime._SUITE_ALLOWED = False

        client = fakes.FakeClient()
        user = fakes.FakeUser(client)
        state = self.runtime.initialize_user(document, user)

        outcome = self.runtime.execute(document, user, state)

        self.assertTrue(outcome["suite_skipped"])
        self.assertEqual([], client.calls)


class PollingTests(_FakeGeventMixin, unittest.TestCase):
    def setUp(self):
        self.runtime = fakes.load_runtime()

    # ------------------------------------------------------------------
    # 15. waitFor polling.
    # ------------------------------------------------------------------

    def test_polling_succeeds_before_exhausting_max_attempts(self):
        sleeps = self._install_fake_gevent()

        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation(
            "op-1", type="read", method="GET", resource="Job/1",
            waitFor={"pollingStatusCode": 202, "maxAttempts": 5, "intervalMs": 250},
        )

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=202, content=b"{}"))
        client.queue_response(fakes.FakeResponse(status_code=202, content=b"{}"))
        client.queue_response(fakes.FakeResponse(status_code=200, content=b'{"done":true}'))
        user = fakes.FakeUser(client)

        result = self.runtime._execute_operation(document, user, context, action)

        self.assertFalse(result["failed"])
        self.assertEqual(3, len(client.calls))
        self.assertEqual([0.25, 0.25], sleeps)
        self.assertEqual(200, context["last_response"]["status_code"])
        names = {call["name"] for call in client.calls}
        self.assertEqual(1, len(names))

    def test_polling_exhaustion_is_one_semantic_failure_with_exact_message(self):
        sleeps = self._install_fake_gevent()

        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation(
            "op-1", type="read", method="GET", resource="Job/1",
            waitFor={"pollingStatusCode": 202, "maxAttempts": 3, "intervalMs": 100},
        )

        client = fakes.FakeClient()
        for _ in range(3):
            client.queue_response(fakes.FakeResponse(status_code=202, content=b"{}"))
        user = fakes.FakeUser(client)

        result = self.runtime._execute_operation(document, user, context, action)

        self.assertTrue(result["failed"])
        self.assertEqual(3, len(client.calls))
        self.assertEqual([0.1, 0.1], sleeps)
        self.assertEqual(202, context["last_response"]["status_code"])

        # 3 native (success) events, one per attempt, plus exactly one
        # semantic timeout failure -- never a native failure, since every
        # polling attempt received an ordinary (if still-pending) response.
        semantic_events = _semantic_events(user.environment.events.request.items)
        self.assertEqual(1, len(semantic_events))
        self.assertEqual(
            "Timed out waiting for job completion after 3 attempts (last status: 202)",
            str(semantic_events[0]["exception"]),
        )

    def test_polling_stops_on_transport_failure_without_duplicate_semantic_event(self):
        # Models real Locust semantics for a mid-poll connection failure: the
        # second attempt is a returned Locust error response (``error`` set),
        # not a raised exception -- matching how ``HttpSession._send_request_safe_mode``
        # actually converts connection errors into an errored response rather
        # than letting them propagate out of ``client.request(...)``.
        sleeps = self._install_fake_gevent()

        document = _document()
        context = _new_context(self.runtime, document)
        action = _operation(
            "op-1", type="read", method="GET", resource="Job/1",
            waitFor={"pollingStatusCode": 202, "maxAttempts": 5, "intervalMs": 50},
        )

        client = fakes.FakeClient()
        client.queue_response(fakes.FakeResponse(status_code=202, content=b"{}"))
        client.queue_response(
            fakes.FakeResponse(status_code=0, content=b"", error=ConnectionError("dropped"))
        )
        user = fakes.FakeUser(client)

        result = self.runtime._execute_operation(document, user, context, action)

        self.assertTrue(result["failed"])
        self.assertEqual(2, len(client.calls))
        self.assertEqual([0.05], sleeps)
        # One native success (first attempt) + one native failure (second
        # attempt's transport error) -- zero semantic events either way.
        self.assertEqual([], _semantic_events(user.environment.events.request.items))


if __name__ == "__main__":
    unittest.main()

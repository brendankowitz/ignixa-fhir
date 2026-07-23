"""Task 9 RED-phase tests: runtime assertion execution + capability-gate engine.

These tests describe the *wished-for* Task 9 runtime behavior that does not exist yet:

* ``_execute_assertion(document, user, context, action)`` - currently a placeholder that
  raises "is not implemented yet". Task 9 fills it in to evaluate every
  ``LocustIrAssertionKind`` with the exact semantics of
  ``src/Core/Ignixa.TestScript/Evaluation/TestScriptEvaluator.cs`` (response-category
  mapping, media-type comparison, all ten operators with invariant-decimal-before-ordinal
  comparison, request/response/body resolution, warningOnly, status applicability, and
  buffered any-of aggregation) and fire the ``TESTSCRIPT_ASSERT`` semantic event.
* ``initialize_engine(document, environment)`` / ``clear_engine()`` - do not exist yet.
  Task 9 adds them to fetch the target CapabilityStatement, derive the immutable suite/test
  capability decisions, validate the IR schema, and reset per-run state.

Every test converts the known placeholder ("is not implemented yet") RuntimeError, or a
missing wished-for symbol, into a clean ``self.fail(...)`` so the RED failures are
attributable "feature missing" assertion failures - never malformed-test errors. Assertions
target observable runtime events/outcomes/state, never mock internals.
"""

import decimal
import itertools
import os
import unittest
from unittest.mock import patch

import fakes


# ---------------------------------------------------------------------------
# Document / action / criteria builders
# ---------------------------------------------------------------------------


def _document(source="suite/sample.xml", **overrides):
    base = {
        "schemaVersion": "1.0",
        "metadata": {"name": "Sample", "source": source, "fhirVersion": "4.0"},
        "requiresCapability": None,
        "variables": [],
        "fixtures": [],
        "setup": [],
        "tests": [],
        "teardown": [],
    }
    base.update(overrides)
    if base.get("requiresCapability") is None:
        base.pop("requiresCapability", None)
    return base


def _criteria(kind, field=None, expression=None, value=None, operator=None):
    return {
        "kind": kind,
        "field": field,
        "expression": expression,
        "value": value,
        "operator": operator,
    }


def _assert(
    action_id,
    criteria,
    warning_only=False,
    direction="response",
    source_id=None,
    any_of_group_id=None,
    when_response_source_id=None,
    when_response_statuses=None,
    label=None,
    description=None,
):
    return {
        "id": action_id,
        "kind": "assert",
        "label": label,
        "description": description,
        "criteria": criteria,
        "warningOnly": warning_only,
        "direction": direction,
        "sourceId": source_id,
        "anyOfGroupId": any_of_group_id,
        "whenResponseSourceId": when_response_source_id,
        "whenResponseStatuses": when_response_statuses or [],
    }


def _test_phase(test_id, actions=None, requires_capability=None):
    phase = {
        "id": test_id,
        "name": test_id,
        "discardContextAfterExecution": False,
        "initialVariables": {},
        "actions": actions or [],
    }
    if requires_capability is not None:
        phase["requiresCapability"] = requires_capability
    return phase


def _capability(**overrides):
    base = {
        "resourceType": "CapabilityStatement",
        "status": "active",
        "date": "2021-01-01",
        "kind": "instance",
        "fhirVersion": "4.0.1",
        "format": ["json"],
        "rest": [{"mode": "server", "resource": [{"type": "Patient"}]}],
    }
    base.update(overrides)
    return base


# ---------------------------------------------------------------------------
# Response / request seeding helpers
# ---------------------------------------------------------------------------


def _json_response(status_code=200, body=None, headers=None):
    return fakes.FakeResponse(status_code=status_code, headers=headers, json_data=body)


def _bare_response(status_code=200, headers=None):
    # No configured JSON payload: json() raises ValueError, modelling a malformed body.
    return fakes.FakeResponse(status_code=status_code, headers=headers, text="<<not json>>")


def make_user():
    client = fakes.FakeClient()
    return fakes.FakeUser(client=client), client


# ---------------------------------------------------------------------------
# Event helpers
# ---------------------------------------------------------------------------


def _events(user):
    return list(user.environment.events.request.items)


def _assert_events(user):
    return [e for e in _events(user) if e.get("request_type") == "TESTSCRIPT_ASSERT"]


# ---------------------------------------------------------------------------
# RED-safe invocation helpers (mirror the Task 8 operation-test pattern)
# ---------------------------------------------------------------------------

_PLACEHOLDER_MARKERS = ("is not implemented yet", "not implemented")


def get_fn(testcase, runtime, name):
    fn = getattr(runtime, name, None)
    if fn is None:
        testcase.fail(f"runtime.{name} is not implemented yet (Task 9 feature missing)")
    return fn


def call_or_fail(testcase, fn, *args, **kwargs):
    try:
        return fn(*args, **kwargs)
    except RuntimeError as exc:
        message = str(exc)
        if any(marker in message for marker in _PLACEHOLDER_MARKERS):
            testcase.fail(f"feature not implemented yet: {message}")
        raise


def run_assertion(testcase, runtime, document, user, context, action):
    """Execute one assertion action, converting the placeholder RuntimeError to a clean fail()."""
    return call_or_fail(testcase, runtime._execute_assertion, document, user, context, action)


def run_phase(testcase, runtime, document, user, context, actions):
    fn = get_fn(testcase, runtime, "_run_phase")
    return call_or_fail(testcase, fn, document, user, context, actions)


def call_init_engine(testcase, runtime, document, environment, session):
    fn = get_fn(testcase, runtime, "initialize_engine")
    with patch("requests.Session", return_value=session):
        return call_or_fail(testcase, fn, document, environment)


class AssertionsTestCase(unittest.TestCase):
    def setUp(self):
        # Fresh module per test: no engine decision, ordinal, or monkeypatch leakage.
        self.runtime = fakes.load_runtime()

    def _context(self, document=None):
        document = document or _document()
        return self.runtime._new_context(document, {"iteration": 0, "ordinal": 0})

    def _metric(self, document, action_id):
        return self.runtime._metric_name(document, action_id)


# ===========================================================================
# Smoke: prove a genuine RED for the assertion executor itself.
# ===========================================================================


class SmokeAssertionTests(AssertionsTestCase):
    def test_passing_response_status_fires_single_assert_event_with_no_exception(self):
        document = _document()
        user, _ = make_user()
        context = self._context(document)
        self.runtime._store_response(context, "r1", _json_response(200, {"resourceType": "Patient", "id": "1"}))

        action = _assert("test.0.action.1", _criteria("responseStatus", value="okay"), source_id="r1")
        result = run_assertion(self, self.runtime, document, user, context, action)

        self.assertEqual({"applicable": True, "failed": False}, result)
        events = _assert_events(user)
        self.assertEqual(1, len(events))
        event = events[0]
        self.assertEqual("TESTSCRIPT_ASSERT", event["request_type"])
        self.assertEqual(self._metric(document, "test.0.action.1"), event["name"])
        self.assertEqual(0, event["response_time"])
        self.assertEqual(0, event["response_length"])
        self.assertIsNone(event["exception"])
        self.assertEqual({"source": self._metric(document, "test.0.action.1")}, event["context"])


# ===========================================================================
# Every LocustIrAssertionKind present in the IR.
# ===========================================================================


class AssertionKindTests(AssertionsTestCase):
    def _run_single(self, criteria, response=None, request=None, direction="response", source_id=None):
        document = _document()
        user, _ = make_user()
        context = self._context(document)
        if response is not None:
            self.runtime._store_response(context, source_id or "r1", response)
        if request is not None:
            self.runtime._store_request(context, source_id or "req1", request)
        action = _assert("test.0.action.0", criteria, direction=direction, source_id=source_id)
        result = run_assertion(self, self.runtime, document, user, context, action)
        return result, _assert_events(user)

    def test_response_status_kind(self):
        result, events = self._run_single(_criteria("responseStatus", value="okay"), _json_response(200, {"resourceType": "Patient"}))
        self.assertFalse(result["failed"])
        self.assertEqual(1, len(events))

    def test_response_code_kind(self):
        result, _ = self._run_single(_criteria("responseCode", value="201"), _json_response(201, {"resourceType": "Patient"}))
        self.assertFalse(result["failed"])

    def test_content_type_kind_ignores_charset_parameter_case_insensitively(self):
        response = _json_response(200, {"resourceType": "Patient"}, headers={"Content-Type": "APPLICATION/FHIR+JSON; charset=utf-8"})
        result, _ = self._run_single(_criteria("contentType", value="application/fhir+json"), response)
        self.assertFalse(result["failed"])

    def test_resource_type_kind(self):
        result, _ = self._run_single(_criteria("resourceType", value="Patient"), _json_response(200, {"resourceType": "Patient", "id": "1"}))
        self.assertFalse(result["failed"])

    def test_header_kind_case_insensitive_field_lookup(self):
        response = _json_response(200, {"resourceType": "Patient"}, headers={"ETag": "W/\"3\""})
        result, _ = self._run_single(_criteria("header", field="etag", value="W/\"3\"", operator="Equals"), response)
        self.assertFalse(result["failed"])

    def test_fhirpath_kind_uses_boolean_adapter(self):
        result, _ = self._run_single(_criteria("fhirPath", expression="Patient.id.exists()"), _json_response(200, {"resourceType": "Patient", "id": "1"}))
        self.assertFalse(result["failed"])

    def test_fhirpath_value_kind_uses_scalar_adapter_and_operator(self):
        result, _ = self._run_single(
            _criteria("fhirPathValue", expression="Patient.gender", value="male", operator="Equals"),
            _json_response(200, {"resourceType": "Patient", "id": "1", "gender": "male"}),
        )
        self.assertFalse(result["failed"])

    def test_request_method_kind(self):
        request = {"method": "POST", "url": "Patient", "headers": {}, "body": None}
        result, _ = self._run_single(_criteria("requestMethod", value="post"), request=request, direction="request", source_id="req1")
        self.assertFalse(result["failed"])

    def test_request_url_kind(self):
        request = {"method": "GET", "url": "Patient/1", "headers": {}, "body": None}
        result, _ = self._run_single(_criteria("requestUrl", value="Patient/1"), request=request, direction="request", source_id="req1")
        self.assertFalse(result["failed"])


# ===========================================================================
# All ten operators + invariant-decimal-before-ordinal comparison.
# ===========================================================================


class OperatorSemanticsTests(AssertionsTestCase):
    def _header_result(self, operator, header_value, expected):
        document = _document()
        user, _ = make_user()
        context = self._context(document)
        headers = {} if header_value is None else {"X-Val": header_value}
        self.runtime._store_response(context, "r1", _json_response(200, {"resourceType": "Patient"}, headers=headers))
        action = _assert("test.0.action.0", _criteria("header", field="X-Val", value=expected, operator=operator), source_id="r1")
        result = run_assertion(self, self.runtime, document, user, context, action)
        return result["failed"]

    def test_equals(self):
        self.assertFalse(self._header_result("Equals", "abc", "abc"))
        self.assertTrue(self._header_result("Equals", "abc", "xyz"))

    def test_not_equals(self):
        self.assertFalse(self._header_result("NotEquals", "abc", "xyz"))

    def test_contains(self):
        self.assertFalse(self._header_result("Contains", "abcdef", "cde"))

    def test_not_contains(self):
        self.assertFalse(self._header_result("NotContains", "abcdef", "zzz"))

    def test_in_splits_on_comma_and_trims(self):
        self.assertFalse(self._header_result("In", "b", "a, b, c"))
        self.assertTrue(self._header_result("In", "d", "a, b, c"))

    def test_not_in(self):
        self.assertFalse(self._header_result("NotIn", "d", "a, b, c"))

    def test_empty(self):
        self.assertFalse(self._header_result("Empty", None, None))

    def test_not_empty(self):
        self.assertFalse(self._header_result("NotEmpty", "present", None))

    def test_greater_than_uses_invariant_decimal_before_ordinal(self):
        # Numeric: "10" > "9" is true decimally, but false by ordinal string compare.
        self.assertFalse(self._header_result("GreaterThan", "10", "9"))

    def test_greater_than_falls_back_to_ordinal_for_non_numeric(self):
        self.assertFalse(self._header_result("GreaterThan", "banana", "apple"))

    def test_less_than_uses_invariant_decimal_before_ordinal(self):
        self.assertFalse(self._header_result("LessThan", "9", "10"))

    def test_thousands_grouping_accepted_as_number(self):
        # .NET NumberStyles.Number accepts group separators: "1,000" == 1000 > 999.
        # Python Decimal rejects the comma, so this must NOT fall back to ordinal.
        self.assertFalse(self._header_result("GreaterThan", "1,000", "999"))

    def test_exponent_notation_rejected_falls_back_to_ordinal(self):
        # decimal.TryParse(value, NumberStyles.Number, Invariant) rejects exponents, so
        # "1E3" vs "999" is an ordinal compare ('1' < '9') -> GreaterThan fails.
        self.assertTrue(self._header_result("GreaterThan", "1E3", "999"))

    def test_nan_rejected_falls_back_to_ordinal_without_exception(self):
        # "NaN" is non-numeric under NumberStyles.Number: ordinal 'N'(78) > '9'(57) passes,
        # and crucially the comparison must not raise (Python's Decimal('NaN') would).
        self.assertFalse(self._header_result("GreaterThan", "NaN", "999"))

    def test_non_finite_and_grouping_parser_matches_dotnet_numberstyles(self):
        # Non-finite / exponent tokens are rejected outright (return None); grouping is
        # accepted; ordinary decimals are unchanged.
        self.assertIsNone(self.runtime._try_decimal("NaN"))
        self.assertIsNone(self.runtime._try_decimal("Infinity"))
        self.assertIsNone(self.runtime._try_decimal("-Infinity"))
        self.assertIsNone(self.runtime._try_decimal("1E3"))
        self.assertIsNone(self.runtime._try_decimal("0x1F"))
        self.assertEqual("1000", str(self.runtime._try_decimal("1,000")))
        self.assertEqual("-1234567.89", str(self.runtime._try_decimal("-1,234,567.89")))
        self.assertEqual("12.5", str(self.runtime._try_decimal("12.5")))
        self.assertEqual("-3", str(self.runtime._try_decimal("  -3  ")))

    def test_lenient_thousands_grouping_matches_dotnet_bcl(self):
        # decimal.TryParse(NumberStyles.Number, Invariant) accepts non-standard grouping:
        # commas may occur between integer digits with no fixed group size (verified empirically
        # against the BCL). The value equals the digit sequence with commas stripped.
        self.assertEqual(decimal.Decimal("100"), self.runtime._try_decimal("1,00"))
        self.assertEqual(decimal.Decimal("10000"), self.runtime._try_decimal("1,0000"))
        self.assertEqual(decimal.Decimal("1234567"), self.runtime._try_decimal("12,34,567"))
        self.assertEqual(decimal.Decimal("1000"), self.runtime._try_decimal("1,00,0"))
        self.assertEqual(decimal.Decimal("100.5"), self.runtime._try_decimal("1,00.5"))
        # Trailing and consecutive commas are ACCEPTED by the BCL (empirically confirmed).
        self.assertEqual(decimal.Decimal("123"), self.runtime._try_decimal("123,"))
        self.assertEqual(decimal.Decimal("1000"), self.runtime._try_decimal("1,,000"))

    def test_lenient_grouping_rejects_malformed_comma_forms(self):
        # The BCL rejects a leading comma and a comma anywhere in the fractional part.
        self.assertIsNone(self.runtime._try_decimal(",123"))
        self.assertIsNone(self.runtime._try_decimal(","))
        self.assertIsNone(self.runtime._try_decimal(",5"))
        self.assertIsNone(self.runtime._try_decimal("1.0,0"))

    def test_lenient_grouping_ordered_comparison_is_numeric(self):
        # "1,00" == 100 > 9 numerically; must not fall back to ordinal ('1' < '9').
        self.assertFalse(self._header_result("GreaterThan", "1,00", "9"))

    def test_out_of_range_decimal_falls_back_to_ordinal(self):
        # .NET decimal max is 79228162514264337593543950335 (~7.9228e28); anything larger is out of
        # range for decimal.TryParse and must be treated as non-numeric (ordinal fallback).
        self.assertIsNone(self.runtime._try_decimal("80000000000000000000000000000"))
        self.assertIsNone(self.runtime._try_decimal("-80000000000000000000000000000"))
        self.assertIsNone(self.runtime._try_decimal("79228162514264337593543950336"))
        self.assertIsNone(self.runtime._try_decimal("99999999999999999999999999999"))
        # MaxValue / MinValue themselves parse exactly.
        self.assertEqual(
            decimal.Decimal("79228162514264337593543950335"),
            self.runtime._try_decimal("79228162514264337593543950335"),
        )
        self.assertEqual(
            decimal.Decimal("-79228162514264337593543950335"),
            self.runtime._try_decimal("-79228162514264337593543950335"),
        )
        # Ordinal fallback: "8e28..." vs "9" -> '8' < '9' -> GreaterThan fails (not greater).
        self.assertTrue(self._header_result("GreaterThan", "80000000000000000000000000000", "9"))

    def test_excess_precision_rounds_like_dotnet_decimal(self):
        # Valid extra precision rounds half-to-even at 28 fractional places, exactly as the BCL does
        # (each expected value was captured empirically from decimal.TryParse).
        parse = self.runtime._try_decimal
        self.assertEqual(decimal.Decimal("1"), parse("1.00000000000000000000000000001"))
        self.assertEqual(decimal.Decimal("1.0000000000000000000000000001"), parse("1.00000000000000000000000000009"))
        # Banker's (round-half-to-even) at the 28th place.
        self.assertEqual(decimal.Decimal("0"), parse("0.00000000000000000000000000005"))
        self.assertEqual(decimal.Decimal("0.0000000000000000000000000002"), parse("0.00000000000000000000000000015"))
        self.assertEqual(decimal.Decimal("0.0000000000000000000000000002"), parse("0.00000000000000000000000000025"))
        # Carry-producing round up to 1.
        self.assertEqual(decimal.Decimal("1"), parse("0.99999999999999999999999999995"))
        # Large integer part sheds fractional precision to fit the 96-bit significand.
        self.assertEqual(decimal.Decimal("7922816251426433759354395034"), parse("7922816251426433759354395033.99"))
        # Ordered comparison: extra precision rounds to equality with 1, so it is NOT greater.
        self.assertTrue(self._header_result("GreaterThan", "1.00000000000000000000000000001", "1"))


# ===========================================================================
# Response-category mapping (MatchesResponseCode) parity.
# ===========================================================================


class ResponseCategoryMappingTests(AssertionsTestCase):
    def _status_pass(self, category, status_code):
        document = _document()
        user, _ = make_user()
        context = self._context(document)
        self.runtime._store_response(context, "r1", _json_response(status_code, {"resourceType": "Patient"}))
        action = _assert("test.0.action.0", _criteria("responseStatus", value=category), source_id="r1")
        result = run_assertion(self, self.runtime, document, user, context, action)
        return not result["failed"]

    def test_category_mapping_matches_dotnet_table(self):
        cases = [
            ("okay", 200), ("okay", 299), ("created", 201), ("noContent", 204),
            ("notModified", 304), ("bad", 400), ("forbidden", 403), ("notFound", 404),
            ("methodNotAllowed", 405), ("conflict", 409), ("gone", 410),
            ("preconditionFailed", 412), ("unprocessable", 422),
        ]
        for category, status in cases:
            with self.subTest(category=category, status=status):
                self.assertTrue(self._status_pass(category, status))

    def test_okay_rejects_out_of_range_status(self):
        self.assertFalse(self._status_pass("okay", 300))


# ===========================================================================
# warningOnly / applicability / event emission rules.
# ===========================================================================


class WarningOnlyAndApplicabilityTests(AssertionsTestCase):
    def test_warning_only_failure_logs_metric_and_message_and_emits_no_event(self):
        document = _document()
        user, _ = make_user()
        context = self._context(document)
        self.runtime._store_response(context, "r1", _json_response(500, {"resourceType": "Patient"}))
        action = _assert(
            "test.0.action.2",
            _criteria("responseStatus", value="okay"),
            warning_only=True,
            source_id="r1",
        )

        # get_fn ensures the executor exists before assertLogs traps the placeholder path.
        get_fn(self, self.runtime, "_execute_assertion")
        with self.assertLogs("ignixa.testscript", level="WARNING") as captured:
            result = run_assertion(self, self.runtime, document, user, context, action)

        self.assertEqual({"applicable": True, "failed": True}, result)
        self.assertEqual([], _assert_events(user))
        joined = "\n".join(captured.output)
        self.assertIn(self._metric(document, "test.0.action.2"), joined)

    def test_inapplicable_status_conditional_assertion_emits_no_event(self):
        document = _document()
        user, _ = make_user()
        context = self._context(document)
        self.runtime._store_response(context, "gate", _json_response(404, {"resourceType": "OperationOutcome"}))
        self.runtime._store_response(context, "r1", _json_response(200, {"resourceType": "Patient", "id": "1"}))
        action = _assert(
            "test.0.action.0",
            _criteria("fhirPath", expression="Patient.id.exists()"),
            source_id="r1",
            when_response_source_id="gate",
            when_response_statuses=[201],
        )
        result = run_assertion(self, self.runtime, document, user, context, action)
        self.assertEqual({"applicable": False, "failed": False}, result)
        self.assertEqual([], _assert_events(user))

    def test_applicable_failure_emits_event_with_assertion_exception(self):
        document = _document()
        user, _ = make_user()
        context = self._context(document)
        self.runtime._store_response(context, "r1", _json_response(500, {"resourceType": "Patient"}))
        action = _assert("test.0.action.0", _criteria("responseStatus", value="okay"), source_id="r1")
        result = run_assertion(self, self.runtime, document, user, context, action)

        self.assertTrue(result["failed"])
        events = _assert_events(user)
        self.assertEqual(1, len(events))
        self.assertIsNotNone(events[0]["exception"])
        self.assertEqual({"source": self._metric(document, "test.0.action.0")}, events[0]["context"])


# ===========================================================================
# Source resolution / history selection / malformed body.
# ===========================================================================


class SourceResolutionTests(AssertionsTestCase):
    def test_source_id_selects_specific_prior_response_not_last(self):
        document = _document()
        user, _ = make_user()
        context = self._context(document)
        self.runtime._store_response(context, "first", _json_response(200, {"resourceType": "Patient", "id": "first"}))
        self.runtime._store_response(context, "second", _json_response(200, {"resourceType": "Observation", "id": "second"}))
        action = _assert("test.0.action.0", _criteria("resourceType", value="Patient"), source_id="first")
        result = run_assertion(self, self.runtime, document, user, context, action)
        self.assertFalse(result["failed"])

    def test_absent_source_id_uses_last_response(self):
        document = _document()
        user, _ = make_user()
        context = self._context(document)
        self.runtime._store_response(context, None, _json_response(200, {"resourceType": "Patient", "id": "last"}))
        action = _assert("test.0.action.0", _criteria("resourceType", value="Patient"))
        result = run_assertion(self, self.runtime, document, user, context, action)
        self.assertFalse(result["failed"])

    def test_request_direction_source_id_selects_prior_request_history(self):
        document = _document()
        user, _ = make_user()
        context = self._context(document)
        self.runtime._store_request(context, "reqA", {"method": "PUT", "url": "Patient/1", "headers": {}, "body": None})
        self.runtime._store_request(context, "reqB", {"method": "DELETE", "url": "Patient/2", "headers": {}, "body": None})
        action = _assert("test.0.action.0", _criteria("requestMethod", value="PUT"), direction="request", source_id="reqA")
        result = run_assertion(self, self.runtime, document, user, context, action)
        self.assertFalse(result["failed"])

    def test_response_header_lookup_is_case_insensitive(self):
        document = _document()
        user, _ = make_user()
        context = self._context(document)
        self.runtime._store_response(context, "r1", _json_response(200, {"resourceType": "Patient"}, headers={"Location": "Patient/1/_history/2"}))
        action = _assert("test.0.action.0", _criteria("header", field="LOCATION", operator="NotEmpty"), source_id="r1")
        result = run_assertion(self, self.runtime, document, user, context, action)
        self.assertFalse(result["failed"])

    def test_malformed_response_json_is_assertion_error_preserving_parse_reason(self):
        document = _document()
        user, _ = make_user()
        context = self._context(document)
        malformed = _bare_response(200)
        try:
            malformed.json()
        except ValueError as exc:
            parse_reason = str(exc)
        self.runtime._store_response(context, "r1", malformed)
        action = _assert("test.0.action.0", _criteria("fhirPath", expression="Patient.id.exists()"), source_id="r1")
        result = run_assertion(self, self.runtime, document, user, context, action)

        self.assertTrue(result["failed"])
        events = _assert_events(user)
        self.assertEqual(1, len(events))
        self.assertIn(parse_reason, str(events[0]["exception"]))


# ===========================================================================
# Buffered any-of aggregation (observed through the phase runner).
# ===========================================================================


class AnyOfGroupTests(AssertionsTestCase):
    def _seed(self, document, context, status_code, body):
        self.runtime._store_response(context, "r1", _json_response(status_code, body))

    def test_any_of_passes_with_one_applicable_passing_member(self):
        document = _document()
        user, _ = make_user()
        context = self._context(document)
        self._seed(document, context, 200, {"resourceType": "Patient", "id": "1"})
        actions = [
            _assert("test.0.action.0", _criteria("responseStatus", value="created"), any_of_group_id="g1", source_id="r1"),
            _assert("test.0.action.1", _criteria("responseStatus", value="okay"), any_of_group_id="g1", source_id="r1"),
        ]
        phase_failed, _ = run_phase(self, self.runtime, document, user, context, actions)
        self.assertFalse(phase_failed)

    def test_any_of_errors_when_no_member_applies(self):
        document = _document()
        user, _ = make_user()
        context = self._context(document)
        # A gate response whose status matches none of the members' whenResponseStatuses.
        self.runtime._store_response(context, "gate", _json_response(200, {"resourceType": "Patient"}))
        self._seed(document, context, 200, {"resourceType": "Patient", "id": "1"})
        actions = [
            _assert("test.0.action.0", _criteria("responseStatus", value="okay"), any_of_group_id="g1", source_id="r1",
                    when_response_source_id="gate", when_response_statuses=[404]),
            _assert("test.0.action.1", _criteria("responseStatus", value="created"), any_of_group_id="g1", source_id="r1",
                    when_response_source_id="gate", when_response_statuses=[201]),
        ]
        phase_failed, _ = run_phase(self, self.runtime, document, user, context, actions)
        self.assertTrue(phase_failed)

    def test_any_of_emits_exactly_one_event_under_first_member_metric(self):
        document = _document()
        user, _ = make_user()
        context = self._context(document)
        self._seed(document, context, 200, {"resourceType": "Patient", "id": "1"})
        actions = [
            _assert("test.0.action.0", _criteria("responseStatus", value="created"), any_of_group_id="g1", source_id="r1"),
            _assert("test.0.action.1", _criteria("responseStatus", value="okay"), any_of_group_id="g1", source_id="r1"),
        ]
        run_phase(self, self.runtime, document, user, context, actions)
        events = _assert_events(user)
        self.assertEqual(1, len(events))
        self.assertEqual(self._metric(document, "test.0.action.0"), events[0]["name"])
        self.assertEqual({"source": self._metric(document, "test.0.action.0")}, events[0]["context"])


# ===========================================================================
# Assertion outcomes must be returned, never raised (teardown masking).
# ===========================================================================


class AssertionOutcomeShapeTests(AssertionsTestCase):
    def test_evaluation_error_becomes_returned_failure_not_raised(self):
        document = _document()
        user, _ = make_user()
        context = self._context(document)
        # Unknown sourceId is an evaluation error in the .NET evaluator; the runtime must
        # convert it into a returned failure + one event, never an uncaught exception that
        # would let teardown mask the earlier failure.
        action = _assert("test.0.action.0", _criteria("resourceType", value="Patient"), source_id="does-not-exist")
        result = run_assertion(self, self.runtime, document, user, context, action)
        self.assertEqual(True, result["applicable"])
        self.assertEqual(True, result["failed"])
        self.assertEqual(1, len(_assert_events(user)))

    def test_passing_assertion_returns_success_outcome_dict(self):
        document = _document()
        user, _ = make_user()
        context = self._context(document)
        self.runtime._store_response(context, "r1", _json_response(200, {"resourceType": "Patient", "id": "1"}))
        action = _assert("test.0.action.0", _criteria("fhirPath", expression="Patient.id.exists()"), source_id="r1")
        result = run_assertion(self, self.runtime, document, user, context, action)
        self.assertEqual({"applicable": True, "failed": False}, result)


# ===========================================================================
# Capability-gate engine startup / teardown.
# ===========================================================================


class EngineLifecycleTestCase(unittest.TestCase):
    def setUp(self):
        self.runtime = fakes.load_runtime()
        # Neutralize any ambient base-url/auth so target-resolution tests are deterministic.
        self._env_patch = patch.dict(os.environ, {}, clear=False)
        self._env_patch.start()
        os.environ.pop("IGNIXA_BASE_URL", None)
        os.environ.pop("IGNIXA_AUTH_HEADER", None)

    def tearDown(self):
        self._env_patch.stop()

    def _metadata_session(self, capability=None, status_code=200, json_error=None, transport_error=None, non_dict=None):
        if transport_error is not None:
            return fakes.FakeRequestsSession(error=transport_error)
        if non_dict is not None:
            response = fakes.FakeMetadataResponse(status_code=status_code, json_data=non_dict)
        elif json_error is not None:
            response = fakes.FakeMetadataResponse(status_code=status_code, json_error=json_error)
        else:
            response = fakes.FakeMetadataResponse(status_code=status_code, json_data=capability if capability is not None else _capability())
        return fakes.FakeRequestsSession(response=response)


class EngineStartupTests(EngineLifecycleTestCase):
    def test_unsupported_schema_major_fails_before_metadata_fetch(self):
        document = _document(schemaVersion="2.0")
        environment = fakes.FakeEnvironment(host="http://fhir.test")
        session = self._metadata_session()
        fn = get_fn(self, self.runtime, "initialize_engine")

        with patch("requests.Session", return_value=session):
            with self.assertRaises(RuntimeError):
                fn(document, environment)

        # Schema is validated before any metadata I/O or user spawn.
        self.assertEqual([], session.get_calls)

    def test_initialize_clears_stale_decisions_and_resets_ordinals_even_after_failed_startup(self):
        # Seed stale state and consume ordinals.
        self.runtime._SUITE_ALLOWED = False
        self.runtime._TEST_DECISIONS = {"stale": False}
        next(self.runtime._USER_ORDINALS)
        next(self.runtime._USER_ORDINALS)

        document = _document(schemaVersion="9.0")
        environment = fakes.FakeEnvironment(host="http://fhir.test")
        fn = get_fn(self, self.runtime, "initialize_engine")
        with patch("requests.Session", return_value=self._metadata_session()):
            with self.assertRaises(RuntimeError):
                fn(document, environment)

        # Ordinals and decisions are reset before validation, so a failed startup still
        # leaves the engine ready to spawn user 0.
        self.assertEqual({}, self.runtime._TEST_DECISIONS)
        self.assertEqual(0, next(self.runtime._USER_ORDINALS))

    def test_target_resolution_prefers_environment_host(self):
        document = _document()
        environment = fakes.FakeEnvironment(host="http://from-env-host")
        os.environ["IGNIXA_BASE_URL"] = "http://from-base-url"
        session = self._metadata_session()
        call_init_engine(self, self.runtime, document, environment, session)
        self.assertEqual(1, len(session.get_calls))
        self.assertTrue(session.get_calls[0]["url"].startswith("http://from-env-host"))

    def test_target_resolution_falls_back_to_base_url_env(self):
        document = _document()
        environment = fakes.FakeEnvironment(host=None)
        os.environ["IGNIXA_BASE_URL"] = "http://from-base-url"
        session = self._metadata_session()
        call_init_engine(self, self.runtime, document, environment, session)
        self.assertEqual(1, len(session.get_calls))
        self.assertTrue(session.get_calls[0]["url"].startswith("http://from-base-url"))

    def test_target_resolution_missing_everywhere_raises(self):
        document = _document()
        environment = fakes.FakeEnvironment(host=None)
        session = self._metadata_session()
        fn = get_fn(self, self.runtime, "initialize_engine")
        with patch("requests.Session", return_value=session):
            with self.assertRaises(RuntimeError):
                fn(document, environment)

    def test_metadata_request_shape_url_timeout_and_auth_header(self):
        document = _document()
        environment = fakes.FakeEnvironment(host="http://fhir.test/")
        os.environ["IGNIXA_AUTH_HEADER"] = "Authorization: Bearer secret"
        session = self._metadata_session()
        call_init_engine(self, self.runtime, document, environment, session)

        self.assertEqual(1, len(session.get_calls))
        call = session.get_calls[0]
        self.assertEqual("http://fhir.test/metadata", call["url"])
        self.assertEqual(30, call["timeout"])
        # IGNIXA_AUTH_HEADER applies to the uninstrumented metadata request.
        auth = {k.lower(): v for k, v in call["headers"].items()}
        self.assertEqual("Bearer secret", auth.get("authorization"))

    def test_malformed_auth_header_fails_startup_and_does_not_fail_open(self):
        document = _document(requiresCapability="rest.resource.where(type='Nonexistent').exists()")
        environment = fakes.FakeEnvironment(host="http://fhir.test")
        os.environ["IGNIXA_AUTH_HEADER"] = "no-colon-here"
        session = self._metadata_session()
        fn = get_fn(self, self.runtime, "initialize_engine")
        with patch("requests.Session", return_value=session):
            with self.assertRaises(RuntimeError):
                fn(document, environment)
        # Failing closed on malformed auth must NOT leave the suite allowed-by-default.
        self.assertFalse(getattr(self.runtime, "_SUITE_ALLOWED", True))


class EngineDecisionTests(EngineLifecycleTestCase):
    def test_suite_gate_disables_all_execution(self):
        document = _document(
            requiresCapability="rest.resource.where(type='Encounter').exists()",
            tests=[_test_phase("test.0"), _test_phase("test.1")],
        )
        environment = fakes.FakeEnvironment(host="http://fhir.test")
        # Capability lacks Encounter -> suite requirement unmet.
        session = self._metadata_session(capability=_capability(rest=[{"mode": "server", "resource": [{"type": "Patient"}]}]))
        call_init_engine(self, self.runtime, document, environment, session)

        self.assertFalse(self.runtime._SUITE_ALLOWED)

        user = fakes.FakeUser(client=None)
        state = self.runtime.initialize_user(document, user)
        outcome = self.runtime.execute(document, user, state)
        self.assertTrue(outcome["suite_skipped"])
        self.assertTrue(all(t["skipped"] for t in outcome["tests"]))

    def test_test_gate_skips_only_the_unmet_test(self):
        document = _document(
            tests=[
                _test_phase("test.0", requires_capability="rest.resource.where(type='Patient').exists()"),
                _test_phase("test.1", requires_capability="rest.resource.where(type='Encounter').exists()"),
            ],
        )
        environment = fakes.FakeEnvironment(host="http://fhir.test")
        session = self._metadata_session(capability=_capability(rest=[{"mode": "server", "resource": [{"type": "Patient"}]}]))
        call_init_engine(self, self.runtime, document, environment, session)

        self.assertTrue(self.runtime._SUITE_ALLOWED)
        self.assertTrue(self.runtime._TEST_DECISIONS.get("test.0", True))
        self.assertFalse(self.runtime._TEST_DECISIONS.get("test.1", True))

        user = fakes.FakeUser(client=None)
        state = self.runtime.initialize_user(document, user)
        outcome = self.runtime.execute(document, user, state)
        by_id = {t["id"]: t for t in outcome["tests"]}
        self.assertFalse(by_id["test.0"]["skipped"])
        self.assertTrue(by_id["test.1"]["skipped"])

    def test_absent_capability_fails_open_on_http_error(self):
        document = _document(
            requiresCapability="rest.resource.where(type='Encounter').exists()",
            tests=[_test_phase("test.0", requires_capability="rest.resource.where(type='Encounter').exists()")],
        )
        environment = fakes.FakeEnvironment(host="http://fhir.test")
        session = self._metadata_session(status_code=503)  # raise_for_status -> HTTPError
        call_init_engine(self, self.runtime, document, environment, session)

        self.assertTrue(self.runtime._SUITE_ALLOWED)
        self.assertTrue(self.runtime._TEST_DECISIONS.get("test.0", True))

    def test_network_error_fails_open(self):
        document = _document(requiresCapability="rest.resource.where(type='Encounter').exists()")
        environment = fakes.FakeEnvironment(host="http://fhir.test")
        import requests

        session = self._metadata_session(transport_error=requests.exceptions.ConnectionError("boom"))
        call_init_engine(self, self.runtime, document, environment, session)
        self.assertTrue(self.runtime._SUITE_ALLOWED)

    def test_non_dict_metadata_json_fails_open(self):
        document = _document(requiresCapability="rest.resource.where(type='Encounter').exists()")
        environment = fakes.FakeEnvironment(host="http://fhir.test")
        session = self._metadata_session(non_dict=["not", "a", "dict"])
        call_init_engine(self, self.runtime, document, environment, session)
        self.assertTrue(self.runtime._SUITE_ALLOWED)

    def test_parse_error_metadata_json_fails_open(self):
        document = _document(requiresCapability="rest.resource.where(type='Encounter').exists()")
        environment = fakes.FakeEnvironment(host="http://fhir.test")
        session = self._metadata_session(json_error=ValueError("no json"))
        call_init_engine(self, self.runtime, document, environment, session)
        self.assertTrue(self.runtime._SUITE_ALLOWED)

    def test_malformed_capability_expression_fails_closed(self):
        document = _document(requiresCapability="this is (not valid fhirpath")
        environment = fakes.FakeEnvironment(host="http://fhir.test")
        session = self._metadata_session(capability=_capability())
        call_init_engine(self, self.runtime, document, environment, session)
        # A malformed requiresCapability against an AVAILABLE capability fails closed.
        self.assertFalse(self.runtime._SUITE_ALLOWED)

    def test_malformed_suite_capability_logs_scope_expression_and_exception(self):
        # An expression fhirpathpy *raises* on (bad arity) forces the except/log path so we can
        # prove the structured error carries the suite scope, expression, and evaluator exception.
        expression = "rest.resource.where("
        document = _document(source="suite/scoped.xml", requiresCapability=expression)
        environment = fakes.FakeEnvironment(host="http://fhir.test")
        session = self._metadata_session(capability=_capability())

        fn = get_fn(self, self.runtime, "initialize_engine")
        with self.assertLogs("ignixa.testscript", level="WARNING") as captured:
            with patch("requests.Session", return_value=session):
                call_or_fail(self, fn, document, environment)

        joined = "\n".join(captured.output)
        # Stable suite identifier available in the IR (metadata.source).
        self.assertIn("suite/scoped.xml", joined)
        self.assertIn(expression, joined)
        # The evaluator exception text (fhirpathpy arity error) must be preserved.
        self.assertIn("arity", joined)
        self.assertFalse(self.runtime._SUITE_ALLOWED)

    def test_malformed_test_capability_logs_test_id_scope_expression_and_exception(self):
        expression = "rest.resource.where("
        document = _document(
            source="suite/scoped.xml",
            tests=[_test_phase("test.scoped", requires_capability=expression)],
        )
        environment = fakes.FakeEnvironment(host="http://fhir.test")
        session = self._metadata_session(capability=_capability())

        fn = get_fn(self, self.runtime, "initialize_engine")
        with self.assertLogs("ignixa.testscript", level="WARNING") as captured:
            with patch("requests.Session", return_value=session):
                call_or_fail(self, fn, document, environment)

        joined = "\n".join(captured.output)
        # Per-test scope must be the test's own id.
        self.assertIn("test.scoped", joined)
        self.assertIn(expression, joined)
        self.assertIn("arity", joined)
        self.assertFalse(self.runtime._TEST_DECISIONS.get("test.scoped", True))

    def test_cached_state_retains_only_immutable_decisions_not_metadata_session_response(self):
        document = _document(
            requiresCapability="rest.resource.where(type='Patient').exists()",
            tests=[_test_phase("test.0", requires_capability="rest.resource.where(type='Patient').exists()")],
        )
        environment = fakes.FakeEnvironment(host="http://fhir.test")
        capability = _capability()
        response = fakes.FakeMetadataResponse(status_code=200, json_data=capability)
        session = fakes.FakeRequestsSession(response=response)
        fn = get_fn(self, self.runtime, "initialize_engine")
        with patch("requests.Session", return_value=session):
            call_or_fail(self, fn, document, environment)

        self.assertIsInstance(self.runtime._SUITE_ALLOWED, bool)
        self.assertIsInstance(self.runtime._TEST_DECISIONS, dict)
        self.assertTrue(all(isinstance(v, bool) for v in self.runtime._TEST_DECISIONS.values()))

        # No module global may retain the fetched capability, the session, or the response.
        retained = list(vars(self.runtime).values())
        self.assertFalse(any(v is capability for v in retained), "capability document must not be retained")
        self.assertFalse(any(v is session for v in retained), "requests session must not be retained")
        self.assertFalse(any(v is response for v in retained), "metadata response must not be retained")


class EngineClearTests(EngineLifecycleTestCase):
    def test_clear_engine_resets_decisions_and_ordinals(self):
        clear = get_fn(self, self.runtime, "clear_engine")
        self.runtime._SUITE_ALLOWED = False
        self.runtime._TEST_DECISIONS = {"test.0": False}
        next(self.runtime._USER_ORDINALS)

        clear()

        self.assertTrue(self.runtime._SUITE_ALLOWED)
        self.assertEqual({}, self.runtime._TEST_DECISIONS)
        self.assertEqual(0, next(self.runtime._USER_ORDINALS))


if __name__ == "__main__":
    unittest.main()

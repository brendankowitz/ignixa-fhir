"""The Python half of the cross-language runtime contract.

Loads the exact same reviewed, immutable ``Contracts/runtime-cases.json`` the C#
``RuntimeContractTests`` drives through the real .NET engines, then drives every case
through the production Locust runtime (``ignixa_testscript_runtime``) and compares the
observable results - outbound HTTP requests, extracted variables, setup/tests/teardown
phase outcomes, and the emitted native/semantic event stream - to the identical committed
expectations. The two engines therefore cannot silently diverge.

The contract is treated as immutable: this suite only reads it. Expected values are never
adjusted to the Python engine; where a genuine parity gap exists it must be reported and
fixed in production, not papered over here. Only representation noise is normalized (compact
JSON bodies, header handling, and outcome spelling), never semantics.

Determinism without a network: ``initialize_engine`` is exercised for real, but the one
uninstrumented ``GET /metadata`` capability probe is redirected to a fake ``requests.Session``
that raises a transport error, so the engine fails OPEN (capability unknown -> everything
allowed) exactly as it would offline. No contract case declares ``requiresCapability``, so the
fail-open decision is the deterministic, correct one for every case.
"""

import copy
import json
import unittest
from pathlib import Path
from unittest.mock import patch

import requests

import fakes


def _contract_path():
    return Path(__file__).resolve().parents[1] / "Contracts" / "runtime-cases.json"


def _load_contract():
    with open(_contract_path(), encoding="utf-8") as handle:
        return json.load(handle)


def _build_response(spec):
    status = spec.get("status", 200)
    headers = spec.get("headers", {})
    body = spec.get("body")
    if body is None:
        return fakes.FakeResponse(status_code=status, headers=headers)
    content = json.dumps(body, separators=(",", ":")).encode("utf-8")
    return fakes.FakeResponse(
        status_code=status, headers=headers, content=content, json_data=body
    )


def _normalize_body(call):
    data = call.get("data")
    if data is None:
        if call.get("json") is not None:
            return {"json": call["json"]}
        return None
    if isinstance(data, (bytes, bytearray)):
        data = data.decode("utf-8")
    if isinstance(data, str):
        if data.lstrip()[:1] in ("{", "["):
            try:
                return {"json": json.loads(data)}
            except json.JSONDecodeError:
                return {"form": data}
        return {"form": data}
    return {"json": data}


def _normalize_request(call):
    return {"method": call["method"], "url": call["url"], "body": _normalize_body(call)}


def _lower_headers(call):
    return {str(k).lower(): v for k, v in (call.get("headers") or {}).items()}


def _phase_present(document):
    fixtures = document.get("fixtures", []) or []
    setup_present = bool(fixtures) or bool(document.get("setup", []))
    teardown_present = bool(document.get("teardown", [])) or any(
        fixture.get("autodelete") for fixture in fixtures
    )
    return setup_present, teardown_present


def _capture_events(user):
    events = []
    for item in user.environment.events.request.items:
        events.append(
            {
                "type": item.get("request_type"),
                "name": item.get("name"),
                "failed": item.get("exception") is not None,
            }
        )
    return events


class RuntimeContractTests(unittest.TestCase):
    def setUp(self):
        self.contract = _load_contract()
        self.cases = self.contract["cases"]

    def _run_case(self, case):
        """Drive one contract case through a freshly loaded runtime, returning observations."""
        # Load the runtime fresh per case so engine capability decisions and per-user
        # ordinals from a prior case never leak into this one.
        runtime = fakes.load_runtime()
        document = copy.deepcopy(case["canonicalIr"])
        host = case.get("env", {}).get("host", "http://contract.test")

        client = fakes.FakeClient()
        user = fakes.FakeUser(client)
        user.environment.host = host
        for spec in case.get("responses", []):
            client.queue_response(_build_response(spec))

        session = fakes.FakeRequestsSession(
            error=requests.exceptions.ConnectionError("contract: no network")
        )
        with patch("requests.Session", lambda: session):
            environment = fakes.FakeEnvironment(host=host)
            runtime.initialize_engine(document, environment)
            state = runtime.initialize_user(document, user)
            outcome = runtime.execute(document, user, state)

        requests_out = [_normalize_request(call) for call in client.calls]
        events = _capture_events(user)
        variables = {}
        if outcome.get("context") is not None:
            variables = dict(outcome["context"].get("variables", {}))
        return outcome, document, requests_out, events, variables, client.calls

    def test_contract_has_every_required_scenario(self):
        names = {case["name"] for case in self.cases}
        required = {
            "crud",
            "post-search",
            "history",
            "polling-success",
            "polling-timeout",
            "warning-only-assertion",
            "skipped-assertion",
            "any-of-pass",
            "any-of-aggregate-fail",
            "any-of-none-applicable",
            "setup-failure",
            "fixture-autocreate-autodelete",
        }
        self.assertTrue(
            required.issubset(names),
            f"contract is missing required scenarios: {sorted(required - names)}",
        )

    def test_outbound_requests_match_contract(self):
        for case in self.cases:
            with self.subTest(case=case["name"]):
                _, _, requests_out, _, _, calls = self._run_case(case)
                expected_requests = case["expectedRequests"]
                self.assertEqual(
                    len(expected_requests),
                    len(requests_out),
                    f"{case['name']}: outbound request count",
                )
                for index, (expected, actual, call) in enumerate(
                    zip(expected_requests, requests_out, calls)
                ):
                    self.assertEqual(
                        expected["method"], actual["method"], f"{case['name']}: request[{index}].method"
                    )
                    self.assertEqual(
                        expected["url"], actual["url"], f"{case['name']}: request[{index}].url"
                    )
                    self.assertEqual(
                        expected["body"], actual["body"], f"{case['name']}: request[{index}].body"
                    )
                    # Headers that matter are compared by containment (case-insensitive):
                    # the default Content-Type is deliberately not pinned because the .NET
                    # provider never sees it, so only accept/custom headers are asserted.
                    actual_headers = _lower_headers(call)
                    for header_name, header_value in (expected.get("headers") or {}).items():
                        self.assertIn(
                            header_name,
                            actual_headers,
                            f"{case['name']}: request[{index}] missing header '{header_name}'",
                        )
                        self.assertEqual(
                            header_value,
                            actual_headers[header_name],
                            f"{case['name']}: request[{index}] header '{header_name}' value",
                        )

    def test_extracted_variables_match_contract(self):
        for case in self.cases:
            with self.subTest(case=case["name"]):
                _, _, _, _, variables, _ = self._run_case(case)
                self.assertEqual(case["expectedVariables"], variables)

    def test_phase_outcomes_match_contract(self):
        for case in self.cases:
            with self.subTest(case=case["name"]):
                outcome, document, _, _, _, _ = self._run_case(case)
                setup_present, teardown_present = _phase_present(document)
                expected = case["expectedPhases"]

                # Setup: absent-ness and failed classification (the rich outcome token is a
                # .NET-only projection; both engines agree on absent + failed).
                self.assertEqual(
                    expected["setup"]["outcome"] == "absent",
                    not setup_present,
                    f"{case['name']}: setup absent-ness",
                )
                self.assertEqual(
                    expected["setup"]["failed"],
                    bool(outcome.get("setup_failed")) if setup_present else False,
                    f"{case['name']}: setup failed",
                )

                # Teardown.
                self.assertEqual(
                    expected["teardown"]["outcome"] == "absent",
                    not teardown_present,
                    f"{case['name']}: teardown absent-ness",
                )
                self.assertEqual(
                    expected["teardown"]["failed"],
                    bool(outcome.get("teardown_failed")) if teardown_present else False,
                    f"{case['name']}: teardown failed",
                )

                # Tests: failed + skipped booleans, in order.
                actual_tests = outcome.get("tests", [])
                self.assertEqual(
                    len(expected["tests"]),
                    len(actual_tests),
                    f"{case['name']}: test count",
                )
                for expected_test, actual_test in zip(expected["tests"], actual_tests):
                    self.assertEqual(
                        expected_test["failed"],
                        bool(actual_test.get("failed")),
                        f"{case['name']}: test '{expected_test['name']}' failed",
                    )
                    self.assertEqual(
                        expected_test["skipped"],
                        bool(actual_test.get("skipped")),
                        f"{case['name']}: test '{expected_test['name']}' skipped",
                    )

    def test_emitted_events_match_contract(self):
        for case in self.cases:
            with self.subTest(case=case["name"]):
                _, _, _, events, _, _ = self._run_case(case)
                self.assertEqual(case["expectedEvents"], events)

    def test_polling_timeout_message_matches_contract(self):
        case = next(c for c in self.cases if c["name"] == "polling-timeout")
        expected_message = case["expectedPollingTimeoutMessage"]

        runtime = fakes.load_runtime()
        document = copy.deepcopy(case["canonicalIr"])
        host = case.get("env", {}).get("host", "http://contract.test")
        client = fakes.FakeClient()
        user = fakes.FakeUser(client)
        user.environment.host = host
        for spec in case.get("responses", []):
            client.queue_response(_build_response(spec))

        session = fakes.FakeRequestsSession(
            error=requests.exceptions.ConnectionError("contract: no network")
        )
        with patch("requests.Session", lambda: session):
            environment = fakes.FakeEnvironment(host=host)
            runtime.initialize_engine(document, environment)
            state = runtime.initialize_user(document, user)
            runtime.execute(document, user, state)

        semantic_failures = [
            item
            for item in user.environment.events.request.items
            if item.get("request_type") == "TESTSCRIPT_OPERATION"
            and item.get("exception") is not None
        ]
        self.assertEqual(1, len(semantic_failures))
        self.assertEqual(expected_message, str(semantic_failures[0]["exception"]))


if __name__ == "__main__":
    unittest.main()

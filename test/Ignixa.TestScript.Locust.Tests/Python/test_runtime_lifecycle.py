import copy
import os
import unittest
from unittest.mock import patch

import fakes


def _document(**overrides):
    base = {
        "schemaVersion": "1.0",
        "variables": [],
        "fixtures": [],
        "setup": [],
        "tests": [],
        "teardown": [],
    }
    base.update(overrides)
    return base


def _action(action_id, kind="operation", **kwargs):
    action = {"id": action_id, "kind": kind}
    action.update(kwargs)
    return action


def _test(test_id, actions=None, discard=False, initial_variables=None):
    return {
        "id": test_id,
        "name": test_id,
        "discardContextAfterExecution": discard,
        "initialVariables": initial_variables or {},
        "actions": actions or [],
    }


def _fixture(fixture_id, variants):
    return {
        "id": fixture_id,
        "autocreate": False,
        "autodelete": False,
        "variants": variants,
    }


class RuntimeLifecycleTests(unittest.TestCase):
    def setUp(self):
        # Loading a fresh module for every test guarantees user ordinals, engine
        # decisions, and every other module-local state cannot leak across tests.
        self.runtime = fakes.load_runtime()

    # ------------------------------------------------------------------
    # Schema validation
    # ------------------------------------------------------------------

    def test_schema_major_mismatch_raises_before_ordinal_assigned(self):
        mismatched = _document(schemaVersion="2.0")
        user = fakes.FakeUser(client=None)

        with self.assertRaises(RuntimeError):
            self.runtime.initialize_user(mismatched, user)

        matching = _document(schemaVersion="1.0")
        state = self.runtime.initialize_user(matching, user)
        self.assertEqual(0, state["ordinal"])

    # ------------------------------------------------------------------
    # User state / ordinals
    # ------------------------------------------------------------------

    def test_user_state_is_minimal_and_ordinals_are_deterministic(self):
        document = _document()
        user1 = fakes.FakeUser(client=None)
        user2 = fakes.FakeUser(client=None)

        state1 = self.runtime.initialize_user(document, user1)
        state2 = self.runtime.initialize_user(document, user2)

        self.assertEqual({"iteration": 0, "ordinal": 0}, state1)
        self.assertEqual({"iteration": 0, "ordinal": 1}, state2)

    # ------------------------------------------------------------------
    # Fresh context shape
    # ------------------------------------------------------------------

    def test_new_context_has_expected_shape_and_skips_missing_defaults(self):
        document = _document(
            variables=[
                {"name": "a", "defaultValue": "1"},
                {"name": "b", "defaultValue": None},
            ]
        )

        context = self.runtime._new_context(document, {"iteration": 0, "ordinal": 0})

        self.assertEqual(
            {
                "variables": {"a": "1"},
                "fixtures": {},
                "requests": {},
                "responses": {},
                "last_request": None,
                "last_response": None,
                "user_state": {"iteration": 0, "ordinal": 0},
            },
            context,
        )

    # ------------------------------------------------------------------
    # Action dispatcher scaffold
    # ------------------------------------------------------------------

    def test_execute_action_dispatches_operation_kind_to_execute_operation(self):
        # The dispatcher's job is only to route operation actions to
        # ``_execute_operation``. This test locks that wiring by replacing the
        # executor directly.
        calls = []

        def fake_execute_operation(document, user, context, action):
            calls.append((document, user, context, action))
            return {"applicable": True, "failed": False}

        self.runtime._execute_operation = fake_execute_operation

        document = _document()
        context = self.runtime._new_context(document, {"iteration": 0, "ordinal": 0})
        user = fakes.FakeUser(client=None)
        action = _action("op-1", kind="operation")

        result = self.runtime._execute_action(document, user, context, action)

        self.assertEqual(1, len(calls))
        self.assertEqual((document, user, context, action), calls[0])
        self.assertEqual({"applicable": True, "failed": False}, result)

    def test_execute_action_default_dispatch_raises_for_assertion(self):
        context = self.runtime._new_context(_document(), {"iteration": 0, "ordinal": 0})
        with self.assertRaises(RuntimeError):
            self.runtime._execute_action(
                _document(), fakes.FakeUser(client=None), context, _action("assert-1", kind="assert")
            )

    def test_execute_action_unknown_kind_raises(self):
        context = self.runtime._new_context(_document(), {"iteration": 0, "ordinal": 0})
        with self.assertRaises(RuntimeError):
            self.runtime._execute_action(
                _document(), fakes.FakeUser(client=None), context, _action("bogus-1", kind="bogus")
            )

    # ------------------------------------------------------------------
    # Full lifecycle ordering
    # ------------------------------------------------------------------

    def test_lifecycle_runs_fixture_setup_tests_teardown_in_order_continuing_after_failure(self):
        calls = []

        def fake(document, user, context, action):
            calls.append(action["id"])
            failed = action["id"] == "test-1-fail"
            return {"applicable": True, "failed": failed}

        self.runtime._execute_action = fake

        document = _document(
            fixtures=[_fixture("patient", [{"resourceType": "Patient"}])],
            setup=[_action("setup-1"), _action("setup-2")],
            tests=[
                _test("test-1", actions=[_action("test-1-fail"), _action("test-1-after")]),
                _test("test-2", actions=[_action("test-2-a")]),
            ],
            teardown=[_action("teardown-1"), _action("teardown-2")],
        )
        user = fakes.FakeUser(client=None)
        state = self.runtime.initialize_user(document, user)

        outcome = self.runtime.execute(document, user, state)

        self.assertEqual(
            ["setup-1", "setup-2", "test-1-fail", "test-1-after", "test-2-a", "teardown-1", "teardown-2"],
            calls,
        )
        self.assertFalse(outcome["setup_failed"])
        self.assertEqual(2, len(outcome["tests"]))
        self.assertFalse(outcome["tests"][0]["skipped"])
        self.assertTrue(outcome["tests"][0]["failed"])
        self.assertFalse(outcome["tests"][1]["skipped"])
        self.assertFalse(outcome["tests"][1]["failed"])
        self.assertTrue(outcome["teardown_ran"])
        self.assertTrue(outcome["failed"])
        self.assertEqual({"resourceType": "Patient"}, outcome["context"]["fixtures"]["patient"])

    def test_setup_failure_skips_all_tests_but_teardown_still_runs(self):
        calls = []

        def fake(document, user, context, action):
            calls.append(action["id"])
            return {"applicable": True, "failed": action["id"] == "setup-1"}

        self.runtime._execute_action = fake

        document = _document(
            setup=[_action("setup-1")],
            tests=[_test("test-1", actions=[_action("test-1-a")])],
            teardown=[_action("teardown-1")],
        )
        user = fakes.FakeUser(client=None)
        state = self.runtime.initialize_user(document, user)

        outcome = self.runtime.execute(document, user, state)

        self.assertEqual(["setup-1", "teardown-1"], calls)
        self.assertTrue(outcome["setup_failed"])
        self.assertEqual(1, len(outcome["tests"]))
        self.assertTrue(outcome["tests"][0]["skipped"])
        self.assertFalse(outcome["tests"][0]["failed"])
        self.assertTrue(outcome["teardown_ran"])
        self.assertTrue(outcome["failed"])

    def test_test_failure_does_not_suppress_later_tests_or_teardown_and_marks_aggregate_failed(self):
        calls = []

        def fake(document, user, context, action):
            calls.append(action["id"])
            return {"applicable": True, "failed": action["id"] == "test-1-a"}

        self.runtime._execute_action = fake

        document = _document(
            setup=[_action("setup-1")],
            tests=[
                _test("test-1", actions=[_action("test-1-a")]),
                _test("test-2", actions=[_action("test-2-a")]),
            ],
            teardown=[_action("teardown-1")],
        )
        user = fakes.FakeUser(client=None)
        state = self.runtime.initialize_user(document, user)

        outcome = self.runtime.execute(document, user, state)

        self.assertEqual(["setup-1", "test-1-a", "test-2-a", "teardown-1"], calls)
        self.assertFalse(outcome["setup_failed"])
        self.assertTrue(outcome["tests"][0]["failed"])
        self.assertFalse(outcome["tests"][1]["failed"])
        self.assertTrue(outcome["teardown_ran"])
        self.assertTrue(outcome["failed"])

    def test_suite_rejection_performs_zero_work_and_reports_skips(self):
        calls = []

        def fake(document, user, context, action):
            calls.append(action["id"])
            return {"applicable": True, "failed": False}

        self.runtime._execute_action = fake
        self.runtime._SUITE_ALLOWED = False

        # An empty fixture variant pool would normally raise; the suite gate must
        # short-circuit before fixture materialization is ever attempted.
        document = _document(
            fixtures=[_fixture("patient", [])],
            setup=[_action("setup-1")],
            tests=[_test("test-1", actions=[_action("test-1-a")])],
            teardown=[_action("teardown-1")],
        )
        user = fakes.FakeUser(client=None)
        state = self.runtime.initialize_user(document, user)

        outcome = self.runtime.execute(document, user, state)

        self.assertEqual([], calls)
        self.assertTrue(outcome["suite_skipped"])
        self.assertFalse(outcome["teardown_ran"])
        self.assertIsNone(outcome["context"])
        self.assertEqual(1, len(outcome["tests"]))
        self.assertTrue(outcome["tests"][0]["skipped"])
        self.assertFalse(outcome["tests"][0]["failed"])
        self.assertFalse(outcome["failed"])

    # ------------------------------------------------------------------
    # Fresh context per invocation / per user
    # ------------------------------------------------------------------

    def test_execute_creates_fresh_context_every_invocation(self):
        records = []

        def fake(document, user, context, action):
            if action["id"] == "record":
                records.append(copy.deepcopy(context))
            elif action["id"] == "mutate":
                context["variables"]["greeting"] = "mutated"
                context["fixtures"]["extra"] = {"z": 1}
                context["requests"]["r1"] = {"a": 1}
                context["responses"]["r1"] = {"b": 1}
                context["last_request"] = {"c": 1}
                context["last_response"] = {"d": 1}
            return {"applicable": True, "failed": False}

        self.runtime._execute_action = fake

        document = _document(
            variables=[{"name": "greeting", "defaultValue": "hello"}],
            setup=[_action("record"), _action("mutate")],
        )
        user = fakes.FakeUser(client=None)
        state = self.runtime.initialize_user(document, user)

        self.runtime.execute(document, user, state)
        self.runtime.execute(document, user, state)

        self.assertEqual(2, len(records))
        for record in records:
            self.assertEqual({"greeting": "hello"}, record["variables"])
            self.assertEqual({}, record["fixtures"])
            self.assertEqual({}, record["requests"])
            self.assertEqual({}, record["responses"])
            self.assertIsNone(record["last_request"])
            self.assertIsNone(record["last_response"])

        self.assertEqual(2, state["iteration"])

    def test_two_users_share_no_execution_context(self):
        def fake(document, user, context, action):
            context["variables"]["touched_by"] = context["user_state"]["ordinal"]
            return {"applicable": True, "failed": False}

        self.runtime._execute_action = fake

        document = _document(setup=[_action("touch")])
        user1 = fakes.FakeUser(client=None)
        user2 = fakes.FakeUser(client=None)
        state1 = self.runtime.initialize_user(document, user1)
        state2 = self.runtime.initialize_user(document, user2)

        outcome1 = self.runtime.execute(document, user1, state1)
        outcome2 = self.runtime.execute(document, user2, state2)

        self.assertIsNot(outcome1["context"], outcome2["context"])
        self.assertEqual(0, outcome1["context"]["variables"]["touched_by"])
        self.assertEqual(1, outcome2["context"]["variables"]["touched_by"])
        self.assertEqual(1, state1["iteration"])
        self.assertEqual(1, state2["iteration"])
        self.assertEqual(0, state1["ordinal"])
        self.assertEqual(1, state2["ordinal"])

    # ------------------------------------------------------------------
    # Parameter expansion clone / discard semantics
    # ------------------------------------------------------------------

    def test_parameter_expansion_discards_context_but_ordinary_tests_share_state(self):
        snapshots = {}

        def fake(document, user, context, action):
            action_id = action["id"]
            if action_id == "setup-var":
                context["variables"]["from_setup"] = "s"
            elif action_id == "ordinary-1-mutate":
                context["variables"]["shared"] = "ordinary-value"
            elif action_id == "expansion-check-setup":
                if context["variables"].get("from_setup") != "s":
                    raise AssertionError("setup state not visible inside clone")
            elif action_id == "expansion-mutate":
                context["variables"]["shared"] = "expansion-value"
                context["fixtures"]["expansion-only"] = {"e": 1}
                context["requests"]["expansion-only"] = {"r": 1}
                context["responses"]["expansion-only"] = {"s": 1}
                context["last_request"] = {"lr": 1}
                context["last_response"] = {"lp": 1}
            elif action_id == "ordinary-2-snapshot":
                snapshots["ordinary-2"] = copy.deepcopy(context)
            return {"applicable": True, "failed": False}

        self.runtime._execute_action = fake

        document = _document(
            setup=[_action("setup-var")],
            tests=[
                _test("ordinary-1", actions=[_action("ordinary-1-mutate")]),
                _test(
                    "expansion-1",
                    actions=[_action("expansion-check-setup"), _action("expansion-mutate")],
                    discard=True,
                    initial_variables={"param": "x"},
                ),
                _test("ordinary-2", actions=[_action("ordinary-2-snapshot")]),
            ],
        )
        user = fakes.FakeUser(client=None)
        state = self.runtime.initialize_user(document, user)

        outcome = self.runtime.execute(document, user, state)

        self.assertFalse(outcome["tests"][1]["failed"])

        view = snapshots["ordinary-2"]
        self.assertEqual("ordinary-value", view["variables"]["shared"])
        self.assertNotIn("param", view["variables"])
        self.assertNotIn("expansion-only", view["fixtures"])
        self.assertNotIn("expansion-only", view["requests"])
        self.assertNotIn("expansion-only", view["responses"])
        self.assertIsNone(view["last_request"])
        self.assertIsNone(view["last_response"])

        self.assertEqual("ordinary-value", outcome["context"]["variables"]["shared"])
        self.assertNotIn("expansion-only", outcome["context"]["fixtures"])
        self.assertNotIn("param", outcome["context"]["variables"])

    # ------------------------------------------------------------------
    # Phase failure aggregation for warning-only / inapplicable assertions
    # ------------------------------------------------------------------

    def test_warning_only_and_inapplicable_assertions_do_not_fail_phase(self):
        def fake(document, user, context, action):
            if action["id"] == "warn-fail":
                return {"applicable": True, "failed": True}
            if action["id"] == "inapplicable-fail":
                return {"applicable": False, "failed": True}
            return {"applicable": True, "failed": False}

        self.runtime._execute_action = fake

        document = _document(
            setup=[
                _action("warn-fail", kind="assert", warningOnly=True),
                _action("inapplicable-fail", kind="assert", warningOnly=False),
            ],
        )
        user = fakes.FakeUser(client=None)
        state = self.runtime.initialize_user(document, user)

        outcome = self.runtime.execute(document, user, state)

        self.assertFalse(outcome["setup_failed"])
        self.assertFalse(outcome["failed"])

    def test_applicable_non_warning_failed_assertion_fails_phase(self):
        def fake(document, user, context, action):
            return {"applicable": True, "failed": action["id"] == "real-fail"}

        self.runtime._execute_action = fake

        document = _document(
            setup=[_action("real-fail", kind="assert", warningOnly=False)],
        )
        user = fakes.FakeUser(client=None)
        state = self.runtime.initialize_user(document, user)

        outcome = self.runtime.execute(document, user, state)

        self.assertTrue(outcome["setup_failed"])
        self.assertTrue(outcome["failed"])

    # ------------------------------------------------------------------
    # Deterministic fixture variant selection
    # ------------------------------------------------------------------

    def test_fixture_variant_index_matches_pinned_sequence_and_is_repeatable(self):
        sequence = [
            self.runtime._fixture_variant_index("", "engine-a", 0, iteration, "patient", 3)
            for iteration in range(1, 7)
        ]
        self.assertEqual([1, 1, 2, 0, 0, 2], sequence)

        repeat = [
            self.runtime._fixture_variant_index("", "engine-a", 0, iteration, "patient", 3)
            for iteration in range(1, 7)
        ]
        self.assertEqual(sequence, repeat)

    def test_fixture_variant_index_rejects_nonpositive_pool_length(self):
        with self.assertRaises(RuntimeError):
            self.runtime._fixture_variant_index("", "host", 0, 1, "patient", 0)
        with self.assertRaises(RuntimeError):
            self.runtime._fixture_variant_index("", "host", 0, 1, "patient", -1)

    def test_execute_selects_fixture_variant_via_seed_hostname_ordinal_iteration(self):
        variants = [{"v": 0}, {"v": 1}, {"v": 2}]

        def fake(document, user, context, action):
            return {"applicable": True, "failed": False}

        self.runtime._execute_action = fake

        document = _document(fixtures=[_fixture("patient", variants)])
        user = fakes.FakeUser(client=None)
        state = self.runtime.initialize_user(document, user)

        with patch.object(self.runtime.socket, "gethostname", return_value="engine-a"):
            with patch.dict(os.environ, {"IGNIXA_FIXTURE_SEED": ""}, clear=False):
                outcome = self.runtime.execute(document, user, state)

        expected_index = self.runtime._fixture_variant_index("", "engine-a", 0, 1, "patient", 3)
        self.assertEqual(variants[expected_index], outcome["context"]["fixtures"]["patient"])
        # Materialized fixtures must be independent copies of the IR variant.
        self.assertIsNot(variants[expected_index], outcome["context"]["fixtures"]["patient"])

    def test_empty_fixture_variant_pool_raises_but_teardown_still_runs(self):
        teardown_calls = []

        def fake(document, user, context, action):
            teardown_calls.append(action["id"])
            return {"applicable": True, "failed": False}

        self.runtime._execute_action = fake

        document = _document(
            fixtures=[_fixture("patient", [])],
            teardown=[_action("teardown-1")],
        )
        user = fakes.FakeUser(client=None)
        state = self.runtime.initialize_user(document, user)

        with self.assertRaises(RuntimeError):
            self.runtime.execute(document, user, state)

        self.assertEqual(["teardown-1"], teardown_calls)


if __name__ == "__main__":
    unittest.main()

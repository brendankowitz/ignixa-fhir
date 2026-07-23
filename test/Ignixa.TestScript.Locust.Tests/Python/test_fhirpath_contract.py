"""Task 9 RED-phase tests: the Python half of the shared FHIRPath contract.

Loads the exact same ``Contracts/fhirpath-cases.json`` the C# ``FhirPathContractTests``
pins against Ignixa's real FhirPath engine, then holds the *wished-for* Task 9 runtime
adapter ``_evaluate_fhirpath(expression, resource, shape)`` to the identical expected
values. Because the adapter does not exist yet (Task 9 introduces it), every case here
fails cleanly as a missing-API assertion failure rather than an error - proving the gap
is the absent production adapter, not a broken test.

The contract's expected values encode Ignixa semantics only; they are never adjusted to
match fhirpathpy. Where fhirpathpy diverges from Ignixa, bridging that divergence is
exactly the Task 9 adapter's job, and these cases are what will hold it accountable.
"""

import json
import unittest
from pathlib import Path

import fakes


def _contract_path():
    return (
        Path(__file__).resolve().parents[1]
        / "Contracts"
        / "fhirpath-cases.json"
    )


def _load_cases():
    with open(_contract_path(), encoding="utf-8") as handle:
        document = json.load(handle)
    return document


class FhirPathContractTests(unittest.TestCase):
    def setUp(self):
        self.runtime = fakes.load_runtime()
        self.cases = _load_cases()

    def _evaluator(self):
        """Return ``runtime._evaluate_fhirpath`` or fail cleanly if Task 9 has not added it."""
        fn = getattr(self.runtime, "_evaluate_fhirpath", None)
        if fn is None:
            self.fail(
                "runtime._evaluate_fhirpath(expression, resource, shape) is not implemented "
                "yet (Task 9 FHIRPath adapter missing)"
            )
        return fn

    # ------------------------------------------------------------------
    # The three explicitly required seed cases, asserted individually so
    # each shows up as its own named RED failure.
    # ------------------------------------------------------------------

    def test_seed_id_exists_boolean_true(self):
        evaluate = self._evaluator()
        result = evaluate("Patient.id.exists()", {"resourceType": "Patient", "id": "p1"}, "boolean")
        self.assertIs(result, True)

    def test_seed_active_scalar_lowercase_true(self):
        evaluate = self._evaluator()
        result = evaluate("Patient.active", {"resourceType": "Patient", "id": "p1", "active": True}, "scalar")
        self.assertEqual("true", result)

    def test_seed_missing_id_scalar_null(self):
        evaluate = self._evaluator()
        result = evaluate("Patient.id", {"resourceType": "Patient", "active": True}, "scalar")
        self.assertIsNone(result)

    # ------------------------------------------------------------------
    # Every shared contract case, driven from the JSON file. Each case is a
    # sub-test so a future GREEN run reports precisely which expressions the
    # adapter still mishandles.
    # ------------------------------------------------------------------

    def test_all_shared_contract_cases_match_ignixa_expected(self):
        evaluate = self._evaluator()
        self.assertGreater(len(self.cases), 3, "contract must contain more than the three seed cases")

        for case in self.cases:
            with self.subTest(case=case["name"], expression=case["expression"], shape=case["shape"]):
                actual = evaluate(case["expression"], case["resource"], case["shape"])
                if case["shape"] == "boolean":
                    self.assertIsInstance(
                        actual, bool, f"boolean shape must return a bool for '{case['name']}'"
                    )
                    self.assertEqual(case["expected"], actual)
                elif case["shape"] == "scalar":
                    # Scalar expected is either a string or JSON null (-> Python None).
                    self.assertEqual(case["expected"], actual)
                else:
                    self.fail(f"unknown contract shape '{case['shape']}' for case '{case['name']}'")

    def test_boolean_shape_never_returns_non_bool(self):
        """A false/absent boolean predicate must be exactly ``False`` (single-true semantics)."""
        evaluate = self._evaluator()
        result = evaluate("Patient.id.exists()", {"resourceType": "Patient", "active": True}, "boolean")
        self.assertIs(result, False)

    def test_scalar_shape_multi_or_complex_result_is_none(self):
        """A complex/multi-valued scalar selection coerces to ``None`` like Ignixa AsString()."""
        evaluate = self._evaluator()
        result = evaluate(
            "Patient.name",
            {"resourceType": "Patient", "id": "p1", "name": [{"family": "Smith", "given": ["John"]}]},
            "scalar",
        )
        self.assertIsNone(result)


if __name__ == "__main__":
    unittest.main()

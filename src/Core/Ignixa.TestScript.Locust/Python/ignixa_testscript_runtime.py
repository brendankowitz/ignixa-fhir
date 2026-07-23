import copy
import hashlib
import itertools
import os
import socket


SUPPORTED_SCHEMA_MAJOR = 1

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


def _execute_operation(document, user, context, action):
    """Placeholder operation executor filled in by Task 8."""
    raise RuntimeError(
        "TestScript operation execution is not implemented yet "
        f"(action '{action.get('id')}')"
    )


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

        setup_failed, setup_results = _run_phase(
            document, user, context, document.get("setup", [])
        )
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
        # the only short-circuit that returns before this point.
        teardown_failed, teardown_results = _run_phase(
            document, user, context, document.get("teardown", [])
        )
        outcome["teardown_ran"] = True
        outcome["teardown_failed"] = teardown_failed
        outcome["teardown_results"] = teardown_results

    outcome["failed"] = (
        outcome["setup_failed"]
        or any(test["failed"] for test in outcome["tests"])
        or outcome["teardown_failed"]
    )

    return outcome

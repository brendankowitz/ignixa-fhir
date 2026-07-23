import json
import os
from pathlib import Path

from locust import HttpUser, between, events, task

import ignixa_testscript_runtime as runtime


_IR_PATH = Path(__file__).with_name("testscript.ir.json")
_DOCUMENT = json.loads(_IR_PATH.read_text(encoding="utf-8"))


@events.test_start.add_listener
def _on_test_start(environment, **kwargs):
    # Derive the immutable suite/test capability decisions once per run, before any
    # virtual user is spawned. Startup validates the IR schema, fetches the target
    # CapabilityStatement, and resets per-run state (decisions + user ordinals).
    runtime.initialize_engine(_DOCUMENT, environment)


@events.test_stop.add_listener
def _on_test_stop(environment, **kwargs):
    # Clear the cached decisions and restart user ordinals so a subsequent run begins
    # from a clean, fail-open state.
    runtime.clear_engine()


class IgnixaTestScriptUser(HttpUser):
    wait_time = between(
        float(os.getenv("IGNIXA_WAIT_MIN_SECONDS", "0.5")),
        float(os.getenv("IGNIXA_WAIT_MAX_SECONDS", "1.5")),
    )
    host = os.getenv("IGNIXA_BASE_URL")

    def on_start(self):
        self.ignixa_state = runtime.initialize_user(_DOCUMENT, self)

    @task
    def execute_testscript(self):
        runtime.execute(_DOCUMENT, self, self.ignixa_state)

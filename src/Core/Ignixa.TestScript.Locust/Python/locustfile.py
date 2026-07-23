import json
import os
from pathlib import Path

from locust import HttpUser, between, task

import ignixa_testscript_runtime as runtime


_IR_PATH = Path(__file__).with_name("testscript.ir.json")
_DOCUMENT = json.loads(_IR_PATH.read_text(encoding="utf-8"))


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

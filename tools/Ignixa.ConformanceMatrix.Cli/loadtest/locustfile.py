"""
Locust load test for Azure Load Testing (ALT) — FHIR TestScript runner.

Executes weighted TestScript mixes against a co-located Ignixa runner, reporting
per-operation metrics to ALT's sampler dashboard via events.request.fire().

Environment variables:
  TESTSCRIPT_MIX       - JSON object: {"ScriptId": weight, ...} (required)
  FHIR_BASE_URL        - FHIR service base URL (required)
  RUN_MODE             - "performance" or "conformance" (default: "performance")
  TESTSCRIPT_OPTIONS   - JSON object merged into request body as "options" (optional)
  RUNNER_PORT          - Port for co-located runner (default: 5599)
"""

import json
import logging
import os
import subprocess
import time
import random
import requests
from locust import HttpUser, task, between, events


RUNNER_PORT = int(os.environ.get("RUNNER_PORT", "5599"))
RUNNER_URL = f"http://127.0.0.1:{RUNNER_PORT}"

logger = logging.getLogger(__name__)

# Module-global handle to keep runner process alive
_runner_process = None
_runner_log_path = None


@events.test_start.add_listener
def start_runner(environment, **kwargs):
    """Start the TestScript runner once per load-test engine at test start."""
    global _runner_process, _runner_log_path

    if not os.environ.get("FHIR_BASE_URL"):
        raise RuntimeError("FHIR_BASE_URL environment variable is required")

    # Resolve runner binary relative to this script's location
    script_dir = os.path.dirname(os.path.abspath(__file__))
    binary = os.path.join(script_dir, "runner", "ignixa-matrix")

    if not os.path.isfile(binary):
        raise RuntimeError(
            f"Runner binary not found at {binary}. "
            "Ensure it is included in the artifact zip and extracted."
        )

    # Make executable
    os.chmod(binary, 0o755)

    # Resolve testscripts directory relative to this script
    testscripts_dir = os.path.join(script_dir, "testscripts")

    # Redirect runner output to a file rather than PIPE: an unread PIPE fills its
    # buffer and blocks the runner's writes, deadlocking it mid-test.
    _runner_log_path = os.path.join(script_dir, "runner.log")
    runner_log = open(_runner_log_path, "w")  # noqa: SIM115 - handle outlives this scope

    # Spawn runner process
    try:
        _runner_process = subprocess.Popen(
            [binary, "serve", "--tests", testscripts_dir, "--port", str(RUNNER_PORT)],
            stdout=runner_log,
            stderr=subprocess.STDOUT,
        )
    except Exception as e:
        runner_log.close()
        raise RuntimeError(f"Failed to spawn runner process: {e}")

    # Wait for /healthz with timeout and fail-fast on process exit
    max_wait_seconds = 60
    for _ in range(max_wait_seconds):
        # Check if process has exited
        poll_result = _runner_process.poll()
        if poll_result is not None:
            raise RuntimeError(
                f"TestScript runner process exited with code {poll_result}. "
                f"log tail: {_read_runner_log_tail()}"
            )

        try:
            response = requests.get(f"{RUNNER_URL}/healthz", timeout=1)
            if response.status_code == 200:
                return  # Runner is ready
        except requests.RequestException:
            pass

        time.sleep(1)

    raise RuntimeError(
        f"TestScript runner failed to start after {max_wait_seconds} seconds. "
        f"log tail: {_read_runner_log_tail()}"
    )


def _read_runner_log_tail(max_bytes=4096):
    """Return the tail of the runner's log file for error diagnostics."""
    if not _runner_log_path or not os.path.isfile(_runner_log_path):
        return "(no log)"
    try:
        with open(_runner_log_path, "rb") as f:
            f.seek(0, os.SEEK_END)
            size = f.tell()
            f.seek(max(0, size - max_bytes))
            return f.read().decode("utf-8", errors="replace")
    except OSError as e:
        return f"(log unreadable: {e})"


@events.test_stop.add_listener
def stop_runner(environment, **kwargs):
    """Terminate the runner process when the load test stops."""
    global _runner_process

    if _runner_process is None:
        return

    try:
        _runner_process.terminate()
        _runner_process.wait(timeout=5)
    except Exception:
        # Attempt kill if terminate fails
        try:
            _runner_process.kill()
        except Exception:
            pass


class FhirTestScriptUser(HttpUser):
    """Locust user that runs FHIR TestScripts against the co-located runner."""

    host = RUNNER_URL
    wait_time = between(0.1, 1.0)

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        # Parse TESTSCRIPT_MIX from environment
        try:
            mix_json = os.environ.get("TESTSCRIPT_MIX", "{}")
            self.script_weights = json.loads(mix_json)
            if not self.script_weights:
                raise ValueError("TESTSCRIPT_MIX is empty or invalid")
        except json.JSONDecodeError as e:
            raise RuntimeError(f"Failed to parse TESTSCRIPT_MIX: {e}")

        # Precompute weighted choice list
        self.script_ids = list(self.script_weights.keys())
        self.weights = [self.script_weights[sid] for sid in self.script_ids]

    @task
    def run_testscript(self):
        """Execute a TestScript and fire per-operation metrics."""
        # Pick a script based on weighted distribution
        script_id = random.choices(self.script_ids, weights=self.weights, k=1)[0]

        # Build request payload
        payload = {
            "testScriptId": script_id,
            "fhirBaseUrl": os.environ.get("FHIR_BASE_URL"),
            "mode": os.environ.get("RUN_MODE", "performance"),
        }

        # Merge optional TESTSCRIPT_OPTIONS if present
        try:
            options_json = os.environ.get("TESTSCRIPT_OPTIONS")
            if options_json:
                payload["options"] = json.loads(options_json)
        except json.JSONDecodeError as e:
            logger.warning(f"Failed to parse TESTSCRIPT_OPTIONS: {e}")

        # Start timing the e2e request
        e2e_started = time.perf_counter()

        # POST to runner; catch_response so a 200 carrying passed:false still counts
        # as a failed sampler entry, not a success.
        with self.client.post(
            "/run",
            json=payload,
            name=f"TestScript/{script_id}",
            catch_response=True,
        ) as resp:
            if resp.status_code != 200:
                resp.failure(f"runner returned HTTP {resp.status_code}: {resp.text[:200]}")
                return

            try:
                result = resp.json()
            except json.JSONDecodeError as e:
                resp.failure(f"unparseable runner response: {e}")
                return

            if result.get("passed", False):
                resp.success()
            else:
                resp.failure(result.get("summary", "TestScript failed"))

        # Fire per-operation events
        operations = result.get("operations", [])
        for op in operations:
            exception = None
            if not op.get("passed", False):
                exception = Exception(f"Operation failed: {op.get('statusCode', 'unknown')}")

            events.request.fire(
                request_type=op.get("method", "UNKNOWN"),
                name=f'{op.get("name", "unknown")} [{script_id}]',
                response_time=op.get("durationMs", 0),
                response_length=op.get("responseBytes", 0),
                exception=exception,
            )

        # Fire e2e event
        e2e_duration_ms = (time.perf_counter() - e2e_started) * 1000
        e2e_exception = None
        if not result.get("passed", False):
            e2e_exception = Exception(result.get("summary", "TestScript failed"))

        events.request.fire(
            request_type="SCRIPT",
            name=f"e2e [{script_id}]",
            response_time=e2e_duration_ms,
            response_length=0,
            exception=e2e_exception,
        )

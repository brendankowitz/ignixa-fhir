"""End-to-end smoke test for a freshly generated Locust artifact.

This test exercises the *real* pipeline for the shipped ``CRUD/basic.json`` TestScript:

1. It invokes the real ``compile-locust`` CLI (via ``dotnet run``) to produce a fresh,
   flat five-file Locust artifact in a throwaway temporary directory.
2. It imports the generated ``locustfile.py`` (and, transitively, the generated
   ``ignixa_testscript_runtime.py``) *from that temporary directory* through the real
   import machinery -- never the in-repo source runtime.
3. It runs exactly one generated virtual-user iteration against a deterministic loopback
   HTTP server, driving Locust's own environment/user APIs, and asserts on both the HTTP
   requests the server observed and the native + semantic metric events Locust fired.

The test is fully local and deterministic: no Azure credentials, no external network, no
sleeps, and every temporary file, imported module, environment variable, event listener,
``sys.path`` entry, and server thread it creates is torn down again.

``locust`` must be imported before any loopback server thread is created: importing it
gevent-monkey-patches ``threading``/``socket`` process-wide, and a server thread started
before that patch would not cooperate with the patched client sockets. Keeping the import
at module top guarantees the correct ordering for every code path below.
"""

import locust  # noqa: E402  # imported first: gevent-monkey-patches threading/socket process-wide
from locust.env import Environment  # noqa: E402

import importlib.util
import json
import os
import shutil
import subprocess
import sys
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


# --------------------------------------------------------------------------------------
# Repository-relative locations. ``parents[3]`` walks Python -> Tests project -> test ->
# repository root, matching the convention already used by ``fakes.load_runtime``.
# --------------------------------------------------------------------------------------
_REPO_ROOT = Path(__file__).resolve().parents[3]
_TESTSCRIPT = _REPO_ROOT / "src" / "Core" / "Ignixa.TestScript.Suites" / "testscripts" / "CRUD" / "basic.json"
_CLI_PROJECT = _REPO_ROOT / "tools" / "Ignixa.ConformanceMatrix.Cli"

# The artifact writer emits exactly these five flat files and nothing else.
_EXPECTED_ARTIFACT_FILES = frozenset(
    {
        "testscript.ir.json",
        "diagnostics.json",
        "locustfile.py",
        "ignixa_testscript_runtime.py",
        "requirements.txt",
    }
)

# ``CRUD/basic.json`` ships a single fhirfakes fixture, so a fixture-variant count is
# required. Exactly one variant makes fixture selection deterministic regardless of seed.
_FIXTURE_VARIANTS = "1"
_FHIR_VERSION = "4.0"

# Fixed identifier the loopback server assigns at create; the runtime extracts it into the
# ``patientId`` variable and every subsequent request path is derived from it.
_PATIENT_ID = "patient-smoke-0001"

# The generated runtime imports itself under this fixed module name; the generated
# locustfile is loaded under a private name so neither can leak between test runs.
_RUNTIME_MODULE = "ignixa_testscript_runtime"
_GENERATED_LOCUSTFILE_MODULE = "ignixa_generated_locustfile_under_test"

# Deterministic engine inputs. Wait is pinned to zero and the per-iteration task method is
# invoked directly (never the Locust scheduler), so no wait is ever applied.
_ENV_BASE_URL = "IGNIXA_BASE_URL"
_ENV_WAIT_MIN = "IGNIXA_WAIT_MIN_SECONDS"
_ENV_WAIT_MAX = "IGNIXA_WAIT_MAX_SECONDS"
_ENV_FIXTURE_SEED = "IGNIXA_FIXTURE_SEED"
_ENV_AUTH_MODE = "IGNIXA_AUTH_MODE"
_ENV_AUTH_SCOPE = "IGNIXA_AUTH_SCOPE"
_ENV_MANAGED_IDENTITY_CLIENT_ID = "IGNIXA_MANAGED_IDENTITY_CLIENT_ID"
_MANAGED_ENV_KEYS = (
    _ENV_BASE_URL,
    _ENV_WAIT_MIN,
    _ENV_WAIT_MAX,
    _ENV_FIXTURE_SEED,
    _ENV_AUTH_MODE,
    _ENV_AUTH_SCOPE,
    _ENV_MANAGED_IDENTITY_CLIENT_ID,
)
_FIXTURE_SEED = "ignixa-smoke-seed"

_HTTP_METHODS = frozenset({"GET", "POST", "PUT", "DELETE", "PATCH"})
_SOURCE_PREFIX = "basic.json::"


# --------------------------------------------------------------------------------------
# Deterministic loopback FHIR server.
# --------------------------------------------------------------------------------------
class _LoopbackState:
    """Thread-safe request log and lifecycle flag shared with the request handler."""

    def __init__(self):
        self._lock = threading.Lock()
        self._requests = []
        self.deleted = False

    def record(self, method, path):
        with self._lock:
            self._requests.append((method, path))

    def snapshot(self):
        with self._lock:
            return list(self._requests)

    def mark_deleted(self):
        with self._lock:
            self.deleted = True

    def mark_present(self):
        with self._lock:
            self.deleted = False

    def is_deleted(self):
        with self._lock:
            return self.deleted


def _patient_body():
    return {"resourceType": "Patient", "id": _PATIENT_ID}


def _capability_statement():
    # A minimal, well-formed CapabilityStatement. ``CRUD/basic.json`` declares no
    # ``requiresCapability`` predicates, so the body only needs to parse as a JSON object.
    return {
        "resourceType": "CapabilityStatement",
        "status": "active",
        "date": "2024-01-01",
        "kind": "instance",
        "fhirVersion": "4.0.1",
        "format": ["json"],
    }


class _FhirRequestHandler(BaseHTTPRequestHandler):
    """Serves the minimal CapabilityStatement/CRUD responses one iteration needs.

    The create/read/update/delete lifecycle is modelled just enough to satisfy the shipped
    script's assertions: create returns 201 with a Patient carrying an id; reads return 200
    with that Patient until a delete has occurred, after which reads return 410 Gone (the
    script's primary read-after-delete assertion); update returns 200; delete returns 204.
    """

    def log_message(self, *args):  # silence the default stderr access log
        pass

    def _state(self):
        return self.server.ignixa_state

    def _drain_body(self):
        length = int(self.headers.get("Content-Length", 0) or 0)
        if length:
            self.rfile.read(length)

    def _send_json(self, status_code, payload):
        body = b"" if payload is None else json.dumps(payload).encode("utf-8")
        self.send_response(status_code)
        self.send_header("Content-Type", "application/fhir+json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        if body:
            self.wfile.write(body)

    def do_GET(self):
        self._state().record("GET", self.path)
        self._drain_body()
        if self.path.endswith("/metadata"):
            self._send_json(200, _capability_statement())
            return
        if self._state().is_deleted():
            self._send_json(410, {"resourceType": "OperationOutcome"})
        else:
            self._send_json(200, _patient_body())

    def do_POST(self):
        self._state().record("POST", self.path)
        self._drain_body()
        self._state().mark_present()
        self._send_json(201, _patient_body())

    def do_PUT(self):
        self._state().record("PUT", self.path)
        self._drain_body()
        self._send_json(200, _patient_body())

    def do_DELETE(self):
        self._state().record("DELETE", self.path)
        self._drain_body()
        self._state().mark_deleted()
        self._send_json(204, None)


def _start_loopback_server():
    """Bind a threaded HTTP server to an ephemeral loopback port and start serving."""
    server = ThreadingHTTPServer(("127.0.0.1", 0), _FhirRequestHandler)
    server.ignixa_state = _LoopbackState()
    thread = threading.Thread(target=server.serve_forever, name="ignixa-smoke-loopback", daemon=True)
    thread.start()
    return server, thread


class GeneratedArtifactSmokeTest(unittest.TestCase):
    """Compiles the shipped CRUD script once, then asserts flat layout, import, and run."""

    _work_dir = None
    _artifact_dir = None
    _compile_stdout = ""
    _compile_stderr = ""

    @classmethod
    def setUpClass(cls):
        cls._work_dir = Path(tempfile.mkdtemp(prefix="ignixa_locust_smoke_"))
        # The writer creates (and atomically replaces) the --out directory itself, so it
        # must not pre-exist as a directory here.
        cls._artifact_dir = cls._work_dir / "artifact"

        command = [
            "dotnet",
            "run",
            "--project",
            str(_CLI_PROJECT),
            "--",
            "compile-locust",
            "--test",
            str(_TESTSCRIPT),
            "--out",
            str(cls._artifact_dir),
            "--fhir-version",
            _FHIR_VERSION,
            "--fixture-variants",
            _FIXTURE_VARIANTS,
        ]
        result = subprocess.run(
            command,
            cwd=str(_REPO_ROOT),
            env={
                **os.environ,
                _ENV_AUTH_MODE: "managed-identity",
                _ENV_AUTH_SCOPE: "api://sentinel-fhir/.default",
                _ENV_MANAGED_IDENTITY_CLIENT_ID: "sentinel-client-id",
            },
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            universal_newlines=True,
            timeout=900,
        )
        cls._compile_stdout = result.stdout or ""
        cls._compile_stderr = result.stderr or ""
        if result.returncode != 0:
            shutil.rmtree(cls._work_dir, ignore_errors=True)
            raise AssertionError(
                "compile-locust failed (exit {code}).\n"
                "command: {cmd}\n--- stdout ---\n{out}\n--- stderr ---\n{err}".format(
                    code=result.returncode,
                    cmd=" ".join(command),
                    out=cls._compile_stdout,
                    err=cls._compile_stderr,
                )
            )

    @classmethod
    def tearDownClass(cls):
        if cls._work_dir is not None:
            shutil.rmtree(cls._work_dir, ignore_errors=True)

    # ---- helpers ---------------------------------------------------------------------

    def _purge_generated_modules(self):
        for name in (_GENERATED_LOCUSTFILE_MODULE, _RUNTIME_MODULE):
            sys.modules.pop(name, None)

    def _load_generated_locustfile(self):
        locustfile_path = self._artifact_dir / "locustfile.py"
        spec = importlib.util.spec_from_file_location(_GENERATED_LOCUSTFILE_MODULE, str(locustfile_path))
        self.assertIsNotNone(spec, "could not create import spec for generated locustfile")
        self.assertIsNotNone(spec.loader, "generated locustfile spec has no loader")
        module = importlib.util.module_from_spec(spec)
        sys.modules[_GENERATED_LOCUSTFILE_MODULE] = module
        spec.loader.exec_module(module)
        return module

    # ---- tests -----------------------------------------------------------------------

    def test_compiled_artifact_is_exactly_five_flat_files(self):
        entries = list(self._artifact_dir.iterdir())
        names = sorted(entry.name for entry in entries)

        self.assertEqual(
            names,
            sorted(_EXPECTED_ARTIFACT_FILES),
            "compiled artifact must contain exactly the five flat files",
        )
        subdirectories = [entry.name for entry in entries if entry.is_dir()]
        self.assertEqual(subdirectories, [], "compiled artifact must contain no subdirectories")
        for entry in entries:
            self.assertTrue(entry.is_file(), "artifact entry '{0}' must be a flat file".format(entry.name))

    def test_generated_files_carry_source_mapping_and_pinned_dependency(self):
        # Behavioral coverage lives in the loopback run below; these are cheap,
        # supporting checks on the generated files' provenance and dependency pin.
        requirements = (self._artifact_dir / "requirements.txt").read_text(encoding="utf-8")
        self.assertIn(
            "fhirpathpy==2.1.0",
            requirements.splitlines(),
            "generated requirements must pin fhirpathpy==2.1.0",
        )
        self.assertIn(
            "azure-identity==1.25.3",
            requirements.splitlines(),
            "generated requirements must pin azure-identity==1.25.3",
        )

        generated_text = "".join(
            entry.read_text(encoding="utf-8")
            for entry in self._artifact_dir.iterdir()
            if entry.suffix in {".json", ".py", ".txt"}
        )
        self.assertNotIn("api://sentinel-fhir/.default", generated_text)
        self.assertNotIn("sentinel-client-id", generated_text)

        ir = json.loads((self._artifact_dir / "testscript.ir.json").read_text(encoding="utf-8"))
        self.assertEqual(ir["metadata"]["source"], "basic.json")

        diagnostics = json.loads((self._artifact_dir / "diagnostics.json").read_text(encoding="utf-8"))
        metric_diagnostics = [d for d in diagnostics if d.get("code") == "LOCUST_METRIC"]
        self.assertTrue(metric_diagnostics, "diagnostics must include LOCUST_METRIC source-mapping entries")
        for diagnostic in metric_diagnostics:
            # Each metric diagnostic maps an IR source path to a source-qualified metric name.
            self.assertTrue(diagnostic.get("source"), "metric diagnostic must carry an IR source")
            self.assertIn(_SOURCE_PREFIX, diagnostic.get("message", ""))

    def test_generated_artifact_runs_one_iteration_against_loopback(self):
        server, thread = _start_loopback_server()
        port = server.server_address[1]
        base_url = "http://127.0.0.1:{0}/".format(port)  # trailing slash: runtime joins relative paths

        env_backup = {key: os.environ.get(key) for key in _MANAGED_ENV_KEYS}
        # Snapshot the global event handler lists so the locustfile's test_start/test_stop
        # registrations and our request listener can be removed again exactly.
        saved_start = list(locust.events.test_start._handlers)
        saved_stop = list(locust.events.test_stop._handlers)
        saved_request = list(locust.events.request._handlers)

        artifact_path = str(self._artifact_dir)
        path_inserted = False

        try:
            os.environ[_ENV_BASE_URL] = base_url
            os.environ[_ENV_WAIT_MIN] = "0"
            os.environ[_ENV_WAIT_MAX] = "0"
            os.environ[_ENV_FIXTURE_SEED] = _FIXTURE_SEED
            os.environ.pop(_ENV_AUTH_MODE, None)
            os.environ.pop(_ENV_AUTH_SCOPE, None)
            os.environ.pop(_ENV_MANAGED_IDENTITY_CLIENT_ID, None)

            # Front-load the temp artifact dir so the generated locustfile's
            # ``import ignixa_testscript_runtime`` resolves the *generated* runtime, never
            # any repository copy or a leftover module from an earlier run.
            self._purge_generated_modules()
            sys.path.insert(0, artifact_path)
            path_inserted = True

            generated = self._load_generated_locustfile()

            runtime_module = sys.modules.get(_RUNTIME_MODULE)
            self.assertIsNotNone(runtime_module, "generated locustfile did not import its runtime")
            loaded_from = str(Path(runtime_module.__file__).resolve())
            self.assertTrue(
                loaded_from.startswith(str(self._artifact_dir.resolve())),
                "runtime must load from the generated artifact dir, not the repository: {0}".format(loaded_from),
            )

            events = []

            def _on_request(request_type, name, response_time, response_length, exception, **kwargs):
                events.append((request_type, name, exception))

            # Passing the global ``locust.events`` wires the locustfile's test_start/test_stop
            # listeners to this environment, exactly as the real Locust bootstrap does.
            environment = Environment(
                user_classes=[generated.IgnixaTestScriptUser],
                host=base_url,
                events=locust.events,
            )
            environment.events.request.add_listener(_on_request)

            # Drive the generated engine through Locust's own lifecycle: startup fetches the
            # CapabilityStatement and resets state; the user runs exactly one iteration.
            environment.events.test_start.fire(environment=environment)
            user = generated.IgnixaTestScriptUser(environment)
            user.on_start()
            user.execute_testscript()
            environment.events.test_stop.fire(environment=environment)

            observed_requests = server.ignixa_state.snapshot()

            # -- (6) the server observed exactly one iteration's worth of requests --------
            patient_url = "/Patient/{0}".format(_PATIENT_ID)
            expected_requests = [
                ("GET", "/metadata"),          # uninstrumented CapabilityStatement fetch (test_start)
                ("POST", "/Patient"),          # setup.0 create
                ("GET", patient_url),          # test.1 read
                ("GET", patient_url),          # test.2 pre-update read
                ("PUT", patient_url),          # test.2 update
                ("GET", patient_url),          # test.3 post-update read
                ("DELETE", patient_url),       # test.4 delete
                ("GET", patient_url),          # test.5 read-after-delete (-> 410)
                ("DELETE", patient_url),       # teardown.0 delete
            ]
            self.assertEqual(observed_requests, expected_requests)

            # -- (7a) native HTTP request events, source-qualified, one per operation ------
            native_events = [(rt, name) for (rt, name, _exc) in events if rt in _HTTP_METHODS]
            expected_native = [
                ("POST", _SOURCE_PREFIX + "setup.0"),
                ("GET", _SOURCE_PREFIX + "test.1.action.0"),
                ("GET", _SOURCE_PREFIX + "test.2.action.0"),
                ("PUT", _SOURCE_PREFIX + "test.2.action.1"),
                ("GET", _SOURCE_PREFIX + "test.3.action.0"),
                ("DELETE", _SOURCE_PREFIX + "test.4.action.0"),
                ("GET", _SOURCE_PREFIX + "test.5.action.0"),
                ("DELETE", _SOURCE_PREFIX + "teardown.0"),
            ]
            self.assertEqual(native_events, expected_native)
            self.assertTrue(
                all(exc is None for (rt, _name, exc) in events if rt in _HTTP_METHODS),
                "every native HTTP request event must be a success for this happy-path artifact",
            )

            # -- (7b) semantic TESTSCRIPT_ASSERT events, source-qualified, all passing -----
            assert_events = [(name, exc) for (rt, name, exc) in events if rt == "TESTSCRIPT_ASSERT"]
            expected_assert_names = [
                _SOURCE_PREFIX + "test.0.action.0",  # create returned 201
                _SOURCE_PREFIX + "test.0.action.1",  # body is a Patient
                _SOURCE_PREFIX + "test.0.action.2",  # Patient.id.exists()
                _SOURCE_PREFIX + "test.1.action.1",  # read 200
                _SOURCE_PREFIX + "test.1.action.2",  # read body is Patient
                _SOURCE_PREFIX + "test.1.action.3",  # read id == patientId
                _SOURCE_PREFIX + "test.2.action.2",  # update 2xx
                _SOURCE_PREFIX + "test.3.action.1",  # post-update read 200
                _SOURCE_PREFIX + "test.3.action.2",  # post-update id stable
                _SOURCE_PREFIX + "test.4.action.1",  # delete 2xx
                _SOURCE_PREFIX + "test.5.action.1",  # read-after-delete 410 (gone)
            ]
            self.assertEqual([name for (name, _exc) in assert_events], expected_assert_names)
            self.assertTrue(
                all(exc is None for (_name, exc) in assert_events),
                "every applicable assertion must pass for this happy-path artifact",
            )
            for name, _exc in assert_events:
                self.assertTrue(name.startswith(_SOURCE_PREFIX), "assertion metric must be source-qualified")

            # The script's warning-only ``notFound`` alternative (test.5.action.2) fails
            # against the 410 response but, being warning-only, fires no event -- so it must
            # be absent here even though a native GET for that read is present above.
            self.assertNotIn(
                _SOURCE_PREFIX + "test.5.action.2",
                [name for (name, _exc) in assert_events],
            )

            # -- (7c) TESTSCRIPT_OPERATION events fire only on operation failure -----------
            # A fully successful iteration of this artifact fires none.
            operation_events = [name for (rt, name, _exc) in events if rt == "TESTSCRIPT_OPERATION"]
            self.assertEqual(operation_events, [], "no operation should fail on the happy path")

            # No event of any kind carried an exception: the whole iteration succeeded.
            self.assertTrue(all(exc is None for (_rt, _name, exc) in events))
        finally:
            # Restore global event handler lists (removes locustfile + our registrations).
            locust.events.test_start._handlers[:] = saved_start
            locust.events.test_stop._handlers[:] = saved_stop
            locust.events.request._handlers[:] = saved_request

            server.shutdown()
            server.server_close()
            thread.join(timeout=10)

            if path_inserted:
                try:
                    sys.path.remove(artifact_path)
                except ValueError:
                    pass
            self._purge_generated_modules()

            for key, value in env_backup.items():
                if value is None:
                    os.environ.pop(key, None)
                else:
                    os.environ[key] = value


if __name__ == "__main__":
    unittest.main()

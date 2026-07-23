import importlib.util
from pathlib import Path


def load_runtime():
    runtime_path = (
        Path(__file__).resolve().parents[3]
        / "src"
        / "Core"
        / "Ignixa.TestScript.Locust"
        / "Python"
        / "ignixa_testscript_runtime.py"
    )
    spec = importlib.util.spec_from_file_location(
        "ignixa_testscript_runtime_under_test",
        runtime_path,
    )
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load runtime from {runtime_path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class FakeRequestEvents:
    def __init__(self):
        self.items = []

    def fire(self, **kwargs):
        self.items.append(kwargs)


class FakeEnvironment:
    def __init__(self):
        self.events = type(
            "Events",
            (),
            {"request": FakeRequestEvents()},
        )()


class FakeUser:
    def __init__(self, client):
        self.client = client
        self.environment = FakeEnvironment()
        self.host = "http://example.test"

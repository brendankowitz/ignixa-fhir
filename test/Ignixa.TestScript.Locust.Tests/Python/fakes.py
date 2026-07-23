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


class _CaseInsensitiveDict:
    """Minimal case-insensitive string-keyed mapping used for fake HTTP headers.

    Deliberately dependency-free (no import of ``requests``) so this module
    keeps loading under bare Python 3.9 for the Task 7 lifecycle suite, which
    imports this file even though it never exercises operation execution.
    """

    def __init__(self, data=None):
        self._store = {}
        if data:
            for key, value in dict(data).items():
                self[key] = value

    def __setitem__(self, key, value):
        self._store[key.lower()] = (key, value)

    def __getitem__(self, key):
        return self._store[key.lower()][1]

    def __delitem__(self, key):
        del self._store[key.lower()]

    def __contains__(self, key):
        return key.lower() in self._store

    def __iter__(self):
        return (original for original, _ in self._store.values())

    def __len__(self):
        return len(self._store)

    def __eq__(self, other):
        if isinstance(other, _CaseInsensitiveDict):
            return dict(self.items()) == dict(other.items())
        return dict(self.items()) == other

    def get(self, key, default=None):
        item = self._store.get(key.lower())
        return item[1] if item else default

    def items(self):
        return [(original, value) for original, value in self._store.values()]

    def keys(self):
        return [original for original, _ in self._store.values()]

    def __repr__(self):
        return f"_CaseInsensitiveDict({dict(self.items())!r})"


class FakeRequestWire:
    """Captures the method/url/headers/body a fake HTTP client actually sent."""

    def __init__(self, method, url, headers, body):
        self.method = method
        self.url = url
        self.headers = _CaseInsensitiveDict(headers or {})
        self.body = body


class FakeResponse:
    """A queued fake HTTP response supporting ``with ... as response:``.

    Mirrors real Locust ``ResponseContextManager``/``LocustResponse``
    semantics closely enough for tests to prove the production runtime
    always calls ``success()`` for every received response: real Locust's
    default (no manual ``success()``/``failure()`` call) behavior is to call
    ``raise_for_status()`` on ``__exit__`` -- which raises (and is reported
    as a failed native event) for a transport error (``response.error`` set)
    or any 4xx/5xx status. Calling ``success()`` suppresses that regardless
    of status code; calling ``failure(...)`` reports the given exception
    regardless of status/error.
    """

    def __init__(self, status_code=200, headers=None, content=b"", error=None):
        self.status_code = status_code
        self.headers = _CaseInsensitiveDict(headers or {})
        if isinstance(content, str):
            content = content.encode("utf-8")
        self.content = content
        self.error = error
        self.request = None
        self._manual_result = None
        self._client = None
        self._name = None
        self._request_type = None

    @property
    def text(self):
        return self.content.decode("utf-8")

    def json(self):
        import json as _json

        return _json.loads(self.text)

    def success(self):
        self._manual_result = True

    def failure(self, exc_or_message):
        self._manual_result = (
            exc_or_message if isinstance(exc_or_message, Exception) else RuntimeError(exc_or_message)
        )

    @property
    def success_called(self):
        return self._manual_result is True

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, tb):
        if self._manual_result is None:
            if self.error is not None:
                exception = self.error
            elif self.status_code >= 400:
                exception = RuntimeError(f"HTTP {self.status_code}")
            else:
                exception = None
        elif self._manual_result is True:
            exception = None
        else:
            exception = self._manual_result

        if self._client is not None:
            self._client._fire_request_event(self, exception)
        return False


class FakeClient:
    """A queued fake HTTP client implementing ``request(..., catch_response=True)``.

    Tests queue responses (``queue_response``) or transport exceptions
    (``queue_transport_exception``) in send order. Every call is captured in
    ``self.calls`` for request-shape assertions.
    """

    def __init__(self):
        self._queue = []
        self.calls = []
        self._environment = None

    def bind_environment(self, environment):
        self._environment = environment

    def queue_response(self, response):
        self._queue.append(("response", response))

    def queue_transport_exception(self, exc):
        self._queue.append(("raise", exc))

    def request(self, method, url, name=None, catch_response=False, headers=None, data=None, **kwargs):
        call = {
            "method": method,
            "url": url,
            "headers": dict(headers or {}),
            "data": data,
            "name": name,
            "catch_response": catch_response,
        }
        self.calls.append(call)

        if not self._queue:
            raise AssertionError("FakeClient.request called with no queued response")

        kind, item = self._queue.pop(0)
        if kind == "raise":
            raise item

        response = item
        response.request = FakeRequestWire(method, url, headers, data)
        response._client = self
        response._name = name
        response._request_type = method
        return response

    def _fire_request_event(self, response, exception):
        if self._environment is None:
            return
        self._environment.events.request.fire(
            request_type=response._request_type,
            name=response._name,
            response_time=0,
            response_length=len(response.content or b""),
            exception=exception,
            context={},
        )


class FakeUser:
    def __init__(self, client):
        self.client = client
        self.environment = FakeEnvironment()
        self.host = "http://example.test"
        if client is not None and hasattr(client, "bind_environment"):
            client.bind_environment(self.environment)

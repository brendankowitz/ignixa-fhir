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
    def __init__(self, host=None):
        # ``host`` is the Locust ``environment.host`` engine startup resolves the
        # FHIR base URL from first (before falling back to ``IGNIXA_BASE_URL``).
        # It defaults to ``None`` so every existing lifecycle/operation test that
        # constructs ``FakeEnvironment()`` is unaffected.
        self.host = host
        self.events = type(
            "Events",
            (),
            {"request": FakeRequestEvents()},
        )()


class _MissingJson:
    """Sentinel marking a :class:`FakeResponse` with no configured JSON payload."""


_NO_JSON = _MissingJson()


class _CaseInsensitiveDict:
    """Minimal case-insensitive string-keyed mapping.

    Deliberately reimplemented rather than importing
    ``requests.structures.CaseInsensitiveDict``: this module must stay
    dependency-free at import time so lifecycle tests without
    ``requests``/``locust``/``fhirpathpy`` installed still succeed. Only
    *constructing*/*using* an instance ever executes this code, and it needs
    no third-party import at all.
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

    def get(self, key, default=None):
        item = self._store.get(key.lower())
        return item[1] if item is not None else default

    def items(self):
        return [(original, value) for original, value in self._store.values()]

    def keys(self):
        return [original for original, _ in self._store.values()]

    def values(self):
        return [value for _, value in self._store.values()]

    def __eq__(self, other):
        if isinstance(other, _CaseInsensitiveDict):
            return dict(self.items()) == dict(other.items())
        if isinstance(other, dict):
            return {k.lower(): v for k, v in self.items()} == {k.lower(): v for k, v in other.items()}
        return NotImplemented

    def __repr__(self):
        return f"_CaseInsensitiveDict({dict(self.items())!r})"


class FakeRequestInfo:
    """A minimal stand-in for ``requests.PreparedRequest``, captured on a response."""

    def __init__(self, method, url, headers=None, body=None):
        self.method = method
        self.url = url
        self.headers = _CaseInsensitiveDict(headers or {})
        self.body = body


class FakeResponse:
    """A minimal, queueable stand-in for a ``requests``/Locust HTTP response.

    Supports the ``catch_response=True`` context-manager protocol used by
    Locust's ``HttpSession``: entering the ``with`` block returns the
    response itself, and exiting it fires exactly one native Locust request
    event through the callback wired by :class:`FakeClient`, honoring an
    explicit ``success()``/``failure()`` call made inside the block. If
    neither is called, the response defaults to a failure for any status
    code >= 400 (or when ``error`` is set), matching Locust's own
    ``raise_for_status``-driven default for uninstrumented responses.
    """

    def __init__(self, status_code=200, headers=None, content=b"", json_data=_NO_JSON, text=None, error=None):
        self.status_code = status_code
        self.headers = _CaseInsensitiveDict(headers or {})
        self.content = content
        self.error = error
        self.request = None
        self._json_data = json_data
        self._text = text
        self.success_called = False
        self.failure_called = False
        self.failure_message = None
        self._fire_callback = None  # set by FakeClient before the response is returned

    @property
    def text(self):
        if self._text is not None:
            return self._text
        if isinstance(self.content, bytes):
            return self.content.decode("utf-8")
        return str(self.content)

    def json(self):
        if self._json_data is _NO_JSON:
            raise ValueError("FakeResponse has no configured JSON payload")
        return self._json_data

    def success(self):
        self.success_called = True
        self.failure_called = False
        self.failure_message = None

    def failure(self, message):
        self.failure_called = True
        self.success_called = False
        self.failure_message = message

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, tb):
        if exc_type is not None:
            return False
        if self._fire_callback is not None:
            if self.success_called:
                outcome, message = "success", None
            elif self.failure_called:
                outcome, message = "failure", self.failure_message
            else:
                is_failure = self.error is not None or self.status_code >= 400
                outcome, message = ("failure", self.error) if is_failure else ("success", None)
            self._fire_callback(self, outcome, message)
        return False


class FakeClient:
    """A minimal, queueable stand-in for a Locust ``HttpSession``.

    Every ``request()`` call pops the next queued item. A queued
    ``BaseException`` instance is raised directly - no response context ever
    exists for it, and (deliberately, unlike real Locust's own
    ``HttpSession``) this fake never fires a native event for it itself, so
    tests can assert the production runtime fires exactly one native event
    for a transport failure. A queued :class:`FakeResponse` is returned;
    when ``catch_response=True`` it is wired to fire exactly one native
    event through ``environment.events.request`` on context exit.
    """

    def __init__(self):
        self.calls = []
        self._queue = []
        self.events = None  # wired by FakeUser

    def queue_response(self, response):
        self._queue.append(response)
        return response

    def queue_exception(self, exc):
        self._queue.append(exc)
        return exc

    def request(self, method, url, name=None, catch_response=False, headers=None, data=None, json=None, **kwargs):
        self.calls.append({
            "method": method,
            "url": url,
            "headers": dict(headers or {}),
            "data": data,
            "json": json,
            "name": name,
            "catch_response": catch_response,
        })

        if not self._queue:
            raise AssertionError(
                f"FakeClient.request() called for {method} {url} (name={name!r}) with no queued "
                "response or exception; queue a FakeResponse/exception before executing the operation"
            )

        item = self._queue.pop(0)

        if isinstance(item, BaseException):
            raise item

        item.request = FakeRequestInfo(method, url, headers=headers, body=data if data is not None else json)

        def _fire(response, outcome, failure_message):
            if self.events is None:
                return
            exception = None
            if outcome == "failure":
                exception = failure_message if failure_message is not None else AssertionError(
                    f"HTTP {response.status_code}"
                )
            self.events.request.fire(
                request_type=method,
                name=name,
                response_time=0,
                response_length=len(response.content or b""),
                exception=exception,
                response=response,
            )

        if catch_response:
            item._fire_callback = _fire
            return item

        # Non-catch_response calls fire immediately with the default outcome,
        # mirroring Locust's own behavior for uninstrumented requests.
        _fire(item, "failure" if (item.error is not None or item.status_code >= 400) else "success", item.error)
        return item


class FakeUser:
    def __init__(self, client):
        self.client = client
        self.environment = FakeEnvironment()
        self.host = "http://example.test"
        if client is not None and hasattr(client, "events"):
            client.events = self.environment.events


class FakeMetadataResponse:
    """A minimal stand-in for the ``requests`` response returned by the metadata fetch.

    Mirrors just the surface engine startup touches: ``raise_for_status`` (which
    raises an ``HTTPError`` for a >= 400 status, exactly like ``requests``), and
    ``json`` (which returns the configured payload, or raises ``ValueError`` when a
    body was marked unparseable). It is deliberately dependency-free at import so a
    lifecycle tests still import this module without ``requests`` present.
    """

    def __init__(self, status_code=200, json_data=_NO_JSON, json_error=None):
        self.status_code = status_code
        self._json_data = json_data
        self._json_error = json_error

    def raise_for_status(self):
        if self.status_code >= 400:
            # Raise the same exception type real ``requests`` raises so a runtime
            # that fails open on HTTP errors can catch ``requests.HTTPError``.
            import requests

            raise requests.HTTPError(f"HTTP {self.status_code}")

    def json(self):
        if self._json_error is not None:
            raise self._json_error
        if self._json_data is _NO_JSON:
            raise ValueError("FakeMetadataResponse has no configured JSON payload")
        return self._json_data


class FakeRequestsSession:
    """A minimal, queue-free stand-in for ``requests.Session`` used by engine startup.

    Patch ``requests.Session`` with a zero-arg factory returning an instance of this
    class (e.g. ``patch('requests.Session', lambda: session)``). It supports the
    ``with requests.Session() as session:`` context-manager protocol the runtime uses,
    records every ``get`` call (url/timeout/headers) for assertions, and either returns
    the configured response or raises the configured transport error - never firing any
    Locust event, since the metadata probe is uninstrumented.
    """

    def __init__(self, response=None, error=None):
        self.response = response
        self.error = error
        self.get_calls = []
        self.entered = False
        self.closed = False

    def __enter__(self):
        self.entered = True
        return self

    def __exit__(self, exc_type, exc, tb):
        self.closed = True
        return False

    def get(self, url, timeout=None, headers=None, **kwargs):
        self.get_calls.append(
            {"url": url, "timeout": timeout, "headers": dict(headers or {})}
        )
        if self.error is not None:
            raise self.error
        return self.response

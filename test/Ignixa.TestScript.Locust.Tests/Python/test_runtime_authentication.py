import os
import threading
import unittest
from unittest.mock import patch

import fakes


class FakeAccessToken:
    def __init__(self, token, expires_on):
        self.token = token
        self.expires_on = expires_on


class FakeManagedIdentityCredential:
    def __init__(self, tokens, on_get_token=None, close_exception=None):
        self._tokens = list(tokens)
        self._on_get_token = on_get_token
        self._close_exception = close_exception
        self.calls = []
        self.close_calls = 0

    def get_token(self, scope):
        self.calls.append(scope)
        if self._on_get_token is not None:
            self._on_get_token()
        if not self._tokens:
            raise AssertionError("Unexpected get_token call")
        return self._tokens.pop(0)

    def close(self):
        self.close_calls += 1
        if self._close_exception is not None:
            raise self._close_exception


class ManagedIdentityAuthenticationTests(unittest.TestCase):
    def setUp(self):
        self.runtime = fakes.load_runtime()

    def _create_provider(self, env):
        with patch.dict(os.environ, env, clear=True):
            return self.runtime._create_auth_provider()

    def test_absent_mode_defaults_to_no_auth_provider(self):
        provider = self._create_provider({})

        self.assertIsInstance(provider, self.runtime._NoAuthProvider)
        self.assertIsNone(provider.authorization_value())

    def test_empty_and_unknown_modes_raise_runtime_error(self):
        for mode in ("", "client-credentials"):
            with self.subTest(mode=mode):
                with patch.dict(os.environ, {"IGNIXA_AUTH_MODE": mode}, clear=True):
                    with self.assertRaises(RuntimeError):
                        self.runtime._create_auth_provider()

    def test_managed_identity_requires_auth_scope(self):
        for scope in (None, "", "   "):
            with self.subTest(scope=scope):
                env = {"IGNIXA_AUTH_MODE": "managed-identity"}
                if scope is not None:
                    env["IGNIXA_AUTH_SCOPE"] = scope
                with patch.dict(os.environ, env, clear=True):
                    with self.assertRaises(RuntimeError):
                        self.runtime._create_auth_provider()

    def test_empty_managed_identity_client_id_raises(self):
        with patch.dict(
            os.environ,
            {
                "IGNIXA_AUTH_MODE": "managed-identity",
                "IGNIXA_AUTH_SCOPE": "https://example/.default",
                "IGNIXA_MANAGED_IDENTITY_CLIENT_ID": "   ",
            },
            clear=True,
        ):
            with self.assertRaises(RuntimeError):
                self.runtime._create_auth_provider()

    def test_legacy_auth_header_is_rejected_without_echoing_value(self):
        secret = "Authorization: super-secret-token"

        with patch.dict(os.environ, {"IGNIXA_AUTH_HEADER": secret}, clear=True):
            with self.assertRaises(RuntimeError) as context:
                self.runtime._create_auth_provider()

        self.assertNotIn(secret, str(context.exception))

    def test_system_assigned_mode_calls_credential_factory_without_client_id(self):
        calls = []

        def fake_factory(client_id):
            calls.append(client_id)
            return object()

        with patch.dict(
            os.environ,
            {
                "IGNIXA_AUTH_MODE": "managed-identity",
                "IGNIXA_AUTH_SCOPE": "https://example/.default",
            },
            clear=True,
        ), patch.object(self.runtime, "_create_managed_identity_credential", side_effect=fake_factory):
            provider = self.runtime._create_auth_provider()

        self.assertIsInstance(provider, self.runtime._ManagedIdentityAuthProvider)
        self.assertEqual([None], calls)

    def test_user_assigned_mode_passes_the_configured_client_id(self):
        calls = []

        def fake_factory(client_id):
            calls.append(client_id)
            return object()

        with patch.dict(
            os.environ,
            {
                "IGNIXA_AUTH_MODE": "managed-identity",
                "IGNIXA_AUTH_SCOPE": "https://example/.default",
                "IGNIXA_MANAGED_IDENTITY_CLIENT_ID": "client-123",
            },
            clear=True,
        ), patch.object(self.runtime, "_create_managed_identity_credential", side_effect=fake_factory):
            provider = self.runtime._create_auth_provider()

        self.assertIsInstance(provider, self.runtime._ManagedIdentityAuthProvider)
        self.assertEqual(["client-123"], calls)

    def test_credential_construction_failures_are_wrapped_without_values(self):
        def fake_factory(_client_id):
            raise ValueError("scope=https://example/.default client-id=client-123 body=leak")

        with patch.dict(
            os.environ,
            {
                "IGNIXA_AUTH_MODE": "managed-identity",
                "IGNIXA_AUTH_SCOPE": "https://example/.default",
                "IGNIXA_MANAGED_IDENTITY_CLIENT_ID": "client-123",
            },
            clear=True,
        ), patch.object(self.runtime, "_create_managed_identity_credential", side_effect=fake_factory):
            with self.assertRaises(RuntimeError) as context:
                self.runtime._create_auth_provider()

        message = str(context.exception)
        self.assertIn("ValueError", message)
        self.assertNotIn("https://example/.default", message)
        self.assertNotIn("client-123", message)
        self.assertNotIn("body=leak", message)

    def test_first_authorization_value_requests_exact_scope_and_returns_bearer_value(self):
        scope = "https://example/.default"
        credential = FakeManagedIdentityCredential([FakeAccessToken("abc", 5000)])
        provider = self.runtime._ManagedIdentityAuthProvider(scope, credential, clock=lambda: 1000)

        value = provider.authorization_value()

        self.assertEqual("Bearer abc", value)
        self.assertEqual([scope], credential.calls)

    def test_token_with_more_than_300_seconds_remaining_is_reused(self):
        scope = "https://example/.default"
        credential = FakeManagedIdentityCredential([FakeAccessToken("abc", 1301)])
        provider = self.runtime._ManagedIdentityAuthProvider(scope, credential, clock=lambda: 1000)

        first = provider.authorization_value()
        second = provider.authorization_value()

        self.assertEqual("Bearer abc", first)
        self.assertEqual("Bearer abc", second)
        self.assertEqual([scope], credential.calls)

    def test_token_with_exactly_300_seconds_remaining_refreshes(self):
        scope = "https://example/.default"
        credential = FakeManagedIdentityCredential(
            [
                FakeAccessToken("first", 1300),
                FakeAccessToken("second", 1600),
            ]
        )
        provider = self.runtime._ManagedIdentityAuthProvider(scope, credential, clock=lambda: 1000)

        first = provider.authorization_value()
        second = provider.authorization_value()

        self.assertEqual("Bearer first", first)
        self.assertEqual("Bearer second", second)
        self.assertEqual([scope, scope], credential.calls)

    def test_concurrent_callers_during_refresh_produce_one_credential_call(self):
        entered = threading.Event()
        release = threading.Event()

        def on_get_token():
            entered.set()
            self.assertTrue(release.wait(5))

        credential = FakeManagedIdentityCredential([FakeAccessToken("shared", 5000)], on_get_token=on_get_token)
        provider = self.runtime._ManagedIdentityAuthProvider("https://example/.default", credential, clock=lambda: 1000)
        results = []
        errors = []

        def call_authorization():
            try:
                results.append(provider.authorization_value())
            except BaseException as exc:  # noqa: BLE001 - test thread capture
                errors.append(exc)

        first = threading.Thread(target=call_authorization)
        second = threading.Thread(target=call_authorization)
        first.start()
        self.assertTrue(entered.wait(5))
        second.start()
        release.set()
        first.join(5)
        second.join(5)

        self.assertFalse(first.is_alive())
        self.assertFalse(second.is_alive())
        self.assertEqual([], errors)
        self.assertEqual(["Bearer shared", "Bearer shared"], results)
        self.assertEqual(["https://example/.default"], credential.calls)

    def test_invalidate_forces_the_next_call_to_refresh(self):
        scope = "https://example/.default"
        credential = FakeManagedIdentityCredential(
            [
                FakeAccessToken("first", 5000),
                FakeAccessToken("second", 6000),
            ]
        )
        provider = self.runtime._ManagedIdentityAuthProvider(scope, credential, clock=lambda: 1000)

        first = provider.authorization_value()
        provider.invalidate()
        second = provider.authorization_value()

        self.assertEqual("Bearer first", first)
        self.assertEqual("Bearer second", second)
        self.assertEqual([scope, scope], credential.calls)

    def test_token_acquisition_errors_are_sanitized_to_a_stable_message(self):
        def on_get_token():
            raise ValueError("scope=https://example/.default secret-body=leak")

        credential = FakeManagedIdentityCredential([], on_get_token=on_get_token)
        provider = self.runtime._ManagedIdentityAuthProvider("https://example/.default", credential, clock=lambda: 1000)

        with self.assertRaises(RuntimeError) as context:
            provider.authorization_value()

        message = str(context.exception)
        self.assertIn("ValueError", message)
        self.assertNotIn("https://example/.default", message)
        self.assertNotIn("secret-body=leak", message)

    def test_close_disposes_the_credential(self):
        credential = FakeManagedIdentityCredential([FakeAccessToken("ignored", 5000)])
        provider = self.runtime._ManagedIdentityAuthProvider("https://example/.default", credential, clock=lambda: 1000)

        provider.close()

        self.assertEqual(1, credential.close_calls)

    def test_close_failure_is_wrapped_without_source_message(self):
        failing = FakeManagedIdentityCredential(
            [FakeAccessToken("ignored", 5000)],
            close_exception=RuntimeError("close-body=leak"),
        )
        provider = self.runtime._ManagedIdentityAuthProvider("https://example/.default", failing, clock=lambda: 1000)

        with self.assertRaises(RuntimeError) as context:
            provider.close()

        message = str(context.exception)
        self.assertIn("RuntimeError", message)
        self.assertNotIn("close-body=leak", message)

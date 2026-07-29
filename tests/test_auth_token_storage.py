import unittest
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest.mock import Mock, patch

from garmin_export import _persist_auth_tokens, authenticate


class AuthTokenStorageTests(unittest.TestCase):
    def test_persists_tokens_with_legacy_garth_client(self):
        garth_client = Mock()
        garmin = Mock(garth=garth_client)

        _persist_auth_tokens(garmin, Path("tokens"))

        garth_client.dump.assert_called_once_with("tokens")

    def test_persists_tokens_with_current_native_client(self):
        native_client = Mock()

        class GarminStub:
            client = native_client

        _persist_auth_tokens(GarminStub(), Path("tokens"))

        native_client.dump.assert_called_once_with("tokens")

    def test_rejects_unknown_token_storage_api(self):
        with self.assertRaisesRegex(RuntimeError, "no se encontró la función para guardar tokens"):
            _persist_auth_tokens(object(), Path("tokens"))

    def test_non_interactive_auth_never_reads_environment_credentials(self):
        with TemporaryDirectory() as directory:
            tokenstore = Path(directory) / "sesion-inexistente"
            with patch("garmin_export._load_env_file") as load_env:
                with self.assertRaisesRegex(RuntimeError, "sesión válida"):
                    authenticate(
                        str(tokenstore),
                        use_credential_environment=True,
                        interactive=False,
                    )

            load_env.assert_not_called()


if __name__ == "__main__":
    unittest.main()

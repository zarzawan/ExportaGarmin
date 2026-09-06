"""Vinculación privada entre una cuenta de Garmin y los datos de un perfil."""
import hashlib
import hmac
import json
import re
from pathlib import Path


class ProfileBindingError(ValueError):
    pass


def owner_identifier(profile):
    if isinstance(profile, dict):
        for key in ("profileNumber", "userProfileNumber", "id", "userProfileId"):
            value = profile.get(key)
            if isinstance(value, (int, str)) and not isinstance(value, bool) and str(value).strip():
                return str(value).strip()
    raise ProfileBindingError("No se pudo confirmar la cuenta de Garmin. No se han reutilizado sus datos.")


def has_profile_data(cache_dir):
    root = Path(cache_dir)
    return any(any((root / folder).glob("*.json")) for folder in ("daily", "activities", "sections"))


def has_binding(cache_dir):
    return (Path(cache_dir) / ".account_binding.json").exists()


def verify_account(api, cache_dir, *, trusted_existing_session=False):
    from training_analysis import atomic_write_json, load_or_create_reference_secret
    root = Path(cache_dir)
    path = root / ".account_binding.json"
    secret = load_or_create_reference_secret(root)
    current = owner_identifier(api.get_user_profile())

    def fingerprint(identifier):
        return hmac.new(secret, ("account:" + identifier).encode("utf-8"), hashlib.sha256).hexdigest()

    current_hash = fingerprint(current)
    if path.exists():
        try:
            saved = json.loads(path.read_text(encoding="utf-8"))
            expected = saved["owner_hmac"]
            if saved.get("version") != 1 or not isinstance(expected, str) or not re.fullmatch(r"[0-9a-f]{64}", expected):
                raise ValueError()
        except (OSError, ValueError, KeyError, TypeError):
            raise ProfileBindingError("La vinculación del perfil está dañada. Conserva sus archivos y utiliza otro perfil hasta repararla.") from None
        if not hmac.compare_digest(expected, current_hash):
            raise ProfileBindingError("Esta cuenta de Garmin pertenece a otra persona. Crea otro perfil desde «Personas»; la sesión y los datos anteriores se conservan.")
        return
    # Migración: contrastar el propietario original si existe un perfil en caché.
    cached_profile = root / "sections" / "profile.json"
    if cached_profile.exists():
        try:
            saved = json.loads(cached_profile.read_text(encoding="utf-8"))
            data = saved.get("data", saved)
            old_owner = owner_identifier(data.get("user_profile"))
        except (OSError, ValueError, AttributeError):
            raise ProfileBindingError("No se pudo confirmar el propietario de los datos anteriores. Conserva este perfil y crea otro para continuar.") from None
        if not hmac.compare_digest(fingerprint(old_owner), current_hash):
            raise ProfileBindingError("La cuenta no coincide con los datos anteriores. Crea otro perfil desde «Personas».")
    elif has_profile_data(root) and not trusted_existing_session:
        raise ProfileBindingError("Antes de cambiar de cuenta, comprueba la sesión original o crea otro perfil desde «Personas».")
    atomic_write_json(path, {"version": 1, "owner_hmac": current_hash})

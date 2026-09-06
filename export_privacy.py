"""Privacidad, referencias privadas y configuración local."""
import hashlib, hmac, json, re, secrets
from pathlib import Path
from typing import Any, Iterable, Optional
from semantic_common import _normal_key
from export_io import atomic_write_json, atomic_write_text

_SENSITIVE_CONFIGURATION_KEYS = {
    "password",
    "contrasena",
    "contraseña",
    "mfa",
    "token",
    "tokens",
    "cookie",
    "cookies",
    "email",
    "correo",
    "garminemail",
    "garminpassword",
}


def load_or_create_reference_secret(cache_dir: Path) -> bytes:
    """Obtiene la clave local usada para pseudónimos estables.

    La clave vive junto a la caché privada y nunca se escribe en la exportación.
    """
    secret_path = Path(cache_dir) / ".privacy_reference_key"
    backup_path = secret_path.with_name(secret_path.name + ".bak")

    def read_valid(path: Path) -> Optional[bytes]:
        if not path.exists():
            return None
        try:
            value = bytes.fromhex(path.read_text(encoding="ascii").strip())
            if len(value) >= 32:
                return value
        except (OSError, ValueError):
            return None
        return None

    secret_exists = secret_path.exists()
    backup_exists = backup_path.exists()
    current = read_valid(secret_path)
    if current is not None:
        backup = read_valid(backup_path)
        if backup != current:
            try:
                atomic_write_text(backup_path, current.hex() + "\n")
            except OSError:
                # La clave principal sigue siendo válida; se reintentará después.
                pass
        return current

    backup = read_valid(backup_path)
    if backup is not None:
        try:
            atomic_write_text(secret_path, backup.hex() + "\n")
        except OSError:
            raise RuntimeError(
                "No se pudo restaurar la clave privada de referencias. "
                "No se ha sustituido ni generado una clave nueva."
            ) from None
        return backup

    if secret_exists or backup_exists:
        raise RuntimeError(
            "La clave privada de referencias está dañada. "
            "No se ha sustituido para evitar romper asociaciones antiguas."
        )

    secret = secrets.token_bytes(32)
    try:
        secret_path.parent.mkdir(parents=True, exist_ok=True)
        atomic_write_text(backup_path, secret.hex() + "\n")
        atomic_write_text(secret_path, secret.hex() + "\n")
    except OSError:
        raise RuntimeError(
            "No se pudo crear la clave privada de referencias."
        ) from None
    return secret


def private_reference(kind: str, raw_identifier: Any, secret: Optional[bytes]) -> str:
    """Crea una referencia local sin publicar el identificador de Garmin."""
    if raw_identifier is None:
        material = f"missing:{kind}".encode("utf-8")
    else:
        material = str(raw_identifier).encode("utf-8")
    key = secret or b"garmin-data-export-standalone-v3"
    digest = hmac.new(key, kind.encode("utf-8") + b":" + material, hashlib.sha256)
    return f"{kind}_{digest.hexdigest()[:12]}"


def _assert_no_sensitive_configuration(data: Any, location: str) -> None:
    if isinstance(data, dict):
        for key, value in data.items():
            if _normal_key(key) in {_normal_key(item) for item in _SENSITIVE_CONFIGURATION_KEYS}:
                raise ValueError(
                    f"{location} contiene el campo no permitido «{key}». "
                    "No guardes credenciales, MFA, tokens, cookies ni el correo en este archivo."
                )
            _assert_no_sensitive_configuration(value, location)
    elif isinstance(data, list):
        for value in data:
            _assert_no_sensitive_configuration(value, location)


def load_local_json(path: Optional[Path], kind: str) -> Any:
    if path is None:
        return None
    path = Path(path)
    if not path.exists():
        raise ValueError(f"No existe el archivo de {kind}.")
    if path.stat().st_size > 2 * 1024 * 1024:
        raise ValueError(f"El archivo de {kind} es demasiado grande.")
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ValueError(f"No se pudo leer el archivo de {kind}.") from None
    _assert_no_sensitive_configuration(value, kind)
    return value


_PERSONAL_KEY_PARTS = (
    "activityid",
    "applicationkey",
    "authid",
    "deviceid",
    "gearid",
    "ownerid",
    "profileid",
    "sessionid",
    "unitid",
    "userid",
    "uuid",
    "profilepk",
    "userpk",
    "userprofilepk",
    "userprofilenumber",
    "serialnumber",
    "address",
    "streetaddress",
    "postalcode",
    "postcode",
    "fullname",
    "firstname",
    "lastname",
    "ownerdisplayname",
    "ownername",
    "publicdisplayname",
    "username",
    "birthdate",
    "dateofbirth",
    "profileimage",
    "photourl",
    "imageurl",
    "url",
    "href",
    "token",
    "cookie",
    "password",
    "email",
    "phone",
)


_IDENTITY_PARENT_KEYS = {
    "account",
    "owner",
    "profile",
    "user",
    "userdata",
    "userinfo",
    "userinfodto",
    "userprofile",
}


def is_personal_data_key(key: Any, parents: Iterable[Any] = ()) -> bool:
    """Clasifica claves personales igual para el filtrado y para la auditoría."""
    normal = _normal_key(key)
    if normal in {"activityref", "gearref"}:
        return False
    if normal == "link":
        return True
    parent_keys = {_normal_key(parent) for parent in parents}
    if (
        normal == "displayname"
        and bool(parent_keys & _IDENTITY_PARENT_KEYS)
    ):
        return True
    if any(part in normal for part in _PERSONAL_KEY_PARTS):
        return True
    has_identifier_suffix = bool(
        re.search(r"(?:^|[_-])(?:id|uuid)$", str(key), re.IGNORECASE)
        or re.search(r"(?:Id|ID|Uuid|UUID)$", str(key))
        or re.search(r"(?:^|[_-])pk$", str(key), re.IGNORECASE)
        or re.search(r"(?:Pk|PK)$", str(key))
    )
    return has_identifier_suffix


def privacy_audit(
    model: dict,
    forbidden_values: Optional[Iterable[Any]] = None,
    forbidden_identifiers: Optional[Iterable[Any]] = None,
) -> dict:
    """Comprueba la única política: ocultar identidad y conservar deporte."""
    key_violations: list[str] = []
    scalar_values: list[tuple[str, str]] = []

    def visit(value: Any, path: str = "", parents: tuple[str, ...] = ()):
        if isinstance(value, dict):
            for key, nested in value.items():
                if is_personal_data_key(key, parents):
                    key_violations.append(f"{path}.{key}".strip("."))
                visit(
                    nested,
                    f"{path}.{key}".strip("."),
                    (*parents, str(key)),
                )
        elif isinstance(value, list):
            for index, nested in enumerate(value):
                visit(nested, f"{path}[{index}]", parents)
        elif value is not None:
            scalar_values.append((path, str(value)))

    visit(model)
    value_violations = []
    value_violation_paths = []
    for raw in forbidden_values or []:
        candidate = str(raw)
        matching_paths = [
            path for path, value in scalar_values
            if value == candidate
        ]
        if len(candidate) >= 4 and matching_paths:
            value_violations.append(candidate[:3] + "…")
            value_violation_paths.extend(matching_paths)
    for raw in forbidden_identifiers or []:
        candidate = str(raw)
        matching_paths = [
            path for path, value in scalar_values
            if value == candidate
        ]
        if len(candidate) >= 6 and matching_paths:
            value_violations.append(candidate[:3] + "…")
            value_violation_paths.extend(matching_paths)
    return {
        "passed": not key_violations and not value_violations,
        "forbidden_key_paths": sorted(set(key_violations)),
        "forbidden_values_detected": sorted(set(value_violations)),
        "forbidden_value_paths": sorted(set(value_violation_paths)),
    }

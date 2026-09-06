"""Escritura atómica de artefactos y estado local."""
import json, os, tempfile
from pathlib import Path
from typing import Any

def atomic_write_text(path: Path, text: str) -> None:
    """Sustituye un texto solo después de escribirlo completamente."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        prefix=f".{path.name}.",
        suffix=".tmp",
        dir=path.parent,
    )
    temporary_path = Path(temporary_name)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(text)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary_path, path)
    except Exception:
        try:
            temporary_path.unlink(missing_ok=True)
        finally:
            raise


def atomic_write_json(path: Path, data: Any) -> None:
    atomic_write_text(
        path,
        json.dumps(data, ensure_ascii=False, indent=2, default=str) + "\n",
    )

"""Regenera los hashes del lock desde wheels previamente descargados y revisados.

Uso: python scripts/Update-DependencyHashes.py CARPETA_WHEELS
El conjunto debe contener exactamente una distribución por versión del lock.
"""
import hashlib
import re
import sys
import zipfile
from email.parser import BytesParser
from pathlib import Path


def normalise(name):
    return re.sub(r"[-_.]+", "-", name).lower()


def main():
    root = Path(__file__).resolve().parents[1]
    lock = root / "requirements-windows-lock.txt"
    required = re.findall(r"^([A-Za-z0-9_.-]+)==([^\s\\]+)", lock.read_text(encoding="utf-8"), re.M)
    wheels = {}
    for wheel in Path(sys.argv[1]).glob("*.whl"):
        with zipfile.ZipFile(wheel) as archive:
            metadata_files = [name for name in archive.namelist() if name.endswith(".dist-info/METADATA")]
            if len(metadata_files) != 1:
                raise ValueError("Metadatos wheel ambiguos.")
            metadata = BytesParser().parsebytes(archive.read(metadata_files[0]))
        key = (normalise(metadata["Name"]), metadata["Version"])
        if key in wheels:
            raise ValueError("Hay más de un wheel para una dependencia.")
        wheels[key] = hashlib.sha256(wheel.read_bytes()).hexdigest()
    if set(wheels) != {(normalise(name), version) for name, version in required}:
        raise ValueError("Los wheels no coinciden exactamente con el lock.")
    lines = ["# Python 3.11 / Windows x64. Hashes de los wheels admitidos.",
             "# Regenerar con scripts/Update-DependencyHashes.py tras revisar las descargas."]
    lines.extend(f"{name}=={version} --hash=sha256:{wheels[(normalise(name), version)]}" for name, version in required)
    lock.write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()

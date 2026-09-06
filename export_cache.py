"""Caché de respuestas Garmin con integridad y escritura atómica."""
import json, logging, time
from datetime import timedelta
from pathlib import Path
from typing import Optional
from export_io import atomic_write_json
log = logging.getLogger("garmin_export")

_CACHE_ENVELOPE_KEY = "__garmin_export_cache__"


_CACHE_FORMAT_VERSION = 2


class ExportCache:
    """Caché JSON para respuestas diarias, actividades y secciones.

    Se guarda en {output_dir}/.cache/ y utiliza fechas o identificadores como
    claves. Los datos históricos permanecen entre ejecuciones.
    """

    def __init__(
        self,
        out_dir: Path,
        enabled: bool = True,
        cache_dir: Optional[Path] = None,
    ):
        self.enabled = enabled
        self.cache_dir = (
            Path(cache_dir)
            if cache_dir is not None
            else out_dir / ".cache"
        )
        self.daily_dir = self.cache_dir / "daily"
        self.activity_dir = self.cache_dir / "activities"
        self.section_dir = self.cache_dir / "sections"
        self.hits = 0
        self.misses = 0

        if not enabled:
            return

        self.daily_dir.mkdir(parents=True, exist_ok=True)
        self.activity_dir.mkdir(parents=True, exist_ok=True)
        self.section_dir.mkdir(parents=True, exist_ok=True)

        existing_files = list(self.daily_dir.glob("*.json"))
        daily_health = sum(1 for f in existing_files if f.name[0].isdigit())
        daily_hydration = sum(1 for f in existing_files if f.name.startswith("hydration_"))
        daily_nutrition = sum(1 for f in existing_files if f.name.startswith("nutrition_"))
        existing_acts = len(list(self.activity_dir.glob("*.json")))
        existing_sects = len(list(self.section_dir.glob("*.json")))
        total = len(existing_files) + existing_acts + existing_sects
        if total:
            parts = []
            if daily_health:
                parts.append(f"{daily_health} días de salud")
            if daily_hydration:
                parts.append(f"{daily_hydration} días de hidratación")
            if daily_nutrition:
                parts.append(f"{daily_nutrition} días de nutrición")
            if existing_acts:
                parts.append(f"{existing_acts} actividades")
            if existing_sects:
                parts.append(f"{existing_sects} secciones")
            log.info(f"Caché: {', '.join(parts)}")

    def _read_entry(
        self,
        path: Path,
        *,
        accept_legacy: bool,
    ) -> tuple[Optional[dict], set[str], bool]:
        """Lee una entrada y separa sus datos de los metadatos de integridad."""
        if not self.enabled:
            return None, set(), False
        if path.exists():
            try:
                raw = json.loads(path.read_text(encoding="utf-8"))
                if not isinstance(raw, dict):
                    raise ValueError("La entrada de caché no es un objeto.")
                envelope = raw.get(_CACHE_ENVELOPE_KEY)
                if isinstance(envelope, dict):
                    payload = raw.get("data")
                    if (
                        envelope.get("version") != _CACHE_FORMAT_VERSION
                        or not isinstance(payload, dict)
                    ):
                        self.misses += 1
                        return None, set(), False
                    complete_keys = {
                        str(key)
                        for key in envelope.get("complete_keys", [])
                        if isinstance(key, str)
                    }
                    if envelope.get("complete") is False:
                        self.misses += 1
                        return None, complete_keys, False
                    self.hits += 1
                    return payload, complete_keys, True
                if accept_legacy:
                    # Los datos se conservan, pero ninguna clave se considera
                    # verificada: la primera ejecución con v3 la actualizará.
                    self.hits += 1
                    return raw, set(), False
            except (json.JSONDecodeError, OSError, ValueError):
                pass
        self.misses += 1
        return None, set(), False

    @staticmethod
    def _write_entry(
        path: Path,
        data: dict,
        *,
        complete: bool,
        complete_keys: Optional[set[str]] = None,
    ) -> None:
        """Escribe una entrada con una marca explícita de integridad."""
        keys = complete_keys if complete_keys is not None else set(data)
        envelope = {
            _CACHE_ENVELOPE_KEY: {
                "version": _CACHE_FORMAT_VERSION,
                "complete": bool(complete),
                "complete_keys": sorted(str(key) for key in keys),
            },
            "data": data,
        }
        atomic_write_json(path, envelope)

    def get_day_entry(self, ds: str) -> tuple[Optional[dict], set[str]]:
        """Devuelve los datos diarios y las claves confirmadas por Garmin."""
        if not self.enabled:
            return None, set()
        path = self.daily_dir / f"{ds}.json"
        data, complete_keys, _ = self._read_entry(
            path,
            accept_legacy=True,
        )
        return data, complete_keys

    def get_day(self, ds: str) -> Optional[dict]:
        """Compatibilidad: devuelve únicamente los datos de una entrada diaria."""
        data, _ = self.get_day_entry(ds)
        return data

    def put_day(
        self,
        ds: str,
        data: dict,
        *,
        complete_keys: Optional[set[str]] = None,
    ):
        if not self.enabled:
            return
        path = self.daily_dir / f"{ds}.json"
        keys = complete_keys if complete_keys is not None else set(data)
        self._write_entry(
            path,
            data,
            complete=True,
            complete_keys=keys,
        )

    def get_activity(self, activity_id) -> Optional[dict]:
        if not self.enabled:
            return None
        path = self.activity_dir / f"{activity_id}.json"
        data, _, _ = self._read_entry(path, accept_legacy=False)
        return data

    def put_activity(self, activity_id, data: dict, *, complete: bool = True):
        if not self.enabled:
            return
        path = self.activity_dir / f"{activity_id}.json"
        self._write_entry(path, data, complete=complete)

    def get_section(self, name: str) -> Optional[dict]:
        """Obtiene de caché una sección completa."""
        if not self.enabled:
            return None
        path = self.section_dir / f"{name}.json"
        data, _, _ = self._read_entry(path, accept_legacy=False)
        return data

    def section_needs_refresh(self, name: str, max_age_days: int) -> bool:
        """Indica si una sección no existe o ha superado su vigencia."""
        if not self.enabled:
            return True
        path = self.section_dir / f"{name}.json"
        if path.with_suffix(".retry").exists():
            return True
        try:
            age_seconds = max(0.0, time.time() - path.stat().st_mtime)
        except OSError:
            return True
        return age_seconds >= timedelta(days=max_age_days).total_seconds()

    def put_section(self, name: str, data: dict, *, complete: bool = True):
        if not self.enabled:
            return
        path = self.section_dir / f"{name}.json"
        self._write_entry(path, data, complete=complete)

    def put_refreshable_section(self, name: str, data: dict, *, complete: bool) -> dict:
        """Conserva la última sección completa y reintenta tras un fallo de refresco."""
        if not self.enabled:
            return data
        retry_path = self.section_dir / f"{name}.retry"
        if not complete:
            previous = self.get_section(name)
            atomic_write_json(retry_path, {"retry_required": True})
            if previous is not None:
                return previous
        self.put_section(name, data, complete=complete)
        if complete:
            retry_path.unlink(missing_ok=True)
        return data

    def summary(self) -> str:
        total = self.hits + self.misses
        if total == 0:
            return "Caché: sin consultas"
        pct = (self.hits / total) * 100
        return f"Caché: {self.hits} reutilizados, {self.misses} nuevos ({pct:.0f}% reutilizado)"

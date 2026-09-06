"""Primitivas compartidas del modelo semántico y sus presentaciones."""
import math, re, unicodedata
from datetime import date, datetime, timedelta
from typing import Any, Iterable, Optional

def _normal_key(value: Any) -> str:
    plain = unicodedata.normalize("NFKD", str(value).casefold())
    plain = "".join(character for character in plain if not unicodedata.combining(character))
    return re.sub(r"[^a-z0-9]", "", plain)


def _lookup(data: Any, *names: str, default=None):
    """Busca una clave admitiendo snake_case, camelCase y PascalCase."""
    if not isinstance(data, dict):
        return default
    wanted = {_normal_key(name) for name in names}
    for key, value in data.items():
        if _normal_key(key) in wanted:
            return value
    return default


def _number(value: Any) -> Optional[float]:
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        if isinstance(value, float) and not math.isfinite(value):
            return None
        return float(value)
    return None


def _round(value: Any, digits: int = 1):
    numeric = _number(value)
    return round(numeric, digits) if numeric is not None else None


def _parse_date(value: Any) -> Optional[date]:
    if isinstance(value, datetime):
        return value.date()
    if isinstance(value, date):
        return value
    if not isinstance(value, str):
        return None
    match = re.search(r"\d{4}-\d{2}-\d{2}", value)
    if not match:
        return None
    try:
        return date.fromisoformat(match.group(0))
    except ValueError:
        return None


def _parse_duration_seconds(value: Any) -> Optional[int]:
    numeric = _number(value)
    if numeric is not None:
        return round(numeric) if numeric >= 0 else None
    if not isinstance(value, str) or not value.strip():
        return None
    parts = value.strip().split(":")
    try:
        numbers = [int(part) for part in parts]
    except ValueError:
        return None
    if len(numbers) == 2:
        hours, minutes, seconds = 0, numbers[0], numbers[1]
    elif len(numbers) == 3:
        hours, minutes, seconds = numbers
    else:
        return None
    if hours < 0 or not 0 <= minutes < 60 or not 0 <= seconds < 60:
        return None
    return hours * 3600 + minutes * 60 + seconds


def _date_range(start: date, end: date) -> list[date]:
    return [
        start + timedelta(days=index)
        for index in range((end - start).days + 1)
    ]


def _missing_ranges(expected: Iterable[date], available: set[date]) -> list[str]:
    missing = [day for day in expected if day not in available]
    if not missing:
        return []
    result: list[str] = []
    range_start = previous = missing[0]
    for current in missing[1:]:
        if current != previous + timedelta(days=1):
            result.append(
                range_start.isoformat()
                if range_start == previous
                else f"{range_start.isoformat()}/{previous.isoformat()}"
            )
            range_start = current
        previous = current
    result.append(
        range_start.isoformat()
        if range_start == previous
        else f"{range_start.isoformat()}/{previous.isoformat()}"
    )
    return result


def _remove_empty(value: Any, preserve_false_zero: bool = True):
    if isinstance(value, dict):
        cleaned = {
            key: _remove_empty(item, preserve_false_zero)
            for key, item in value.items()
        }
        return {
            key: item
            for key, item in cleaned.items()
            if item is not None and item != "" and item != [] and item != {}
        }
    if isinstance(value, list):
        return [
            cleaned
            for item in value
            if (cleaned := _remove_empty(item, preserve_false_zero))
            not in (None, "", [], {})
        ]
    return value


def _path_value(item: dict, path: tuple[str, ...]):
    value: Any = item
    for key in path:
        if not isinstance(value, dict):
            return None
        value = value.get(key)
    return _number(value)


def _sport_family(sport: Any) -> str:
    value = str(sport or "").casefold()
    if any(part in value for part in ("run", "jog", "carrera")):
        return "running"
    if any(part in value for part in ("cycl", "bike", "biking", "cicl")):
        return "cycling"
    if any(part in value for part in ("strength", "weight_training", "fuerza")):
        return "strength"
    return "other"

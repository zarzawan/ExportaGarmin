"""Eventos públicos del backend, sin valores personales ni rutas locales."""
import json
import re

PREFIX = "EXPORT_EVENT "
PROTOCOL_VERSION = 1


class EventEmitter:
    def __init__(self, enabled=False, stream=None):
        self.enabled = enabled
        self.stream = stream

    def emit(self, event, *, phase=None, completed=None, total=None, status=None):
        if not self.enabled:
            return
        if event not in {"phase", "progress", "result", "error"}:
            raise ValueError("Evento no válido")
        payload = {"protocol_version": PROTOCOL_VERSION, "event": event}
        if phase is not None:
            if not re.fullmatch(r"[A-Za-z '&_-]{1,80}", phase):
                raise ValueError("Fase no válida")
            payload["phase"] = phase
        for key, value in (("completed", completed), ("total", total)):
            if value is not None:
                if not isinstance(value, int) or isinstance(value, bool) or value < 0:
                    raise ValueError("Progreso no válido")
                payload[key] = value
        if status is not None:
            if status not in {"completed", "partial", "failed"}:
                raise ValueError("Estado no válido")
            payload["status"] = status
        print(PREFIX + json.dumps(payload, ensure_ascii=True), file=self.stream, flush=True)

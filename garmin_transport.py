"""Control de solicitudes HTTP y registros públicos del exportador."""
import logging
from functools import wraps
from requests.adapters import HTTPAdapter


class SafeLogFilter(logging.Filter):
    def filter(self, record):
        return record.name == "garmin_export" and record.exc_info is None


def configure_private_logging(verbose=False):
    """Solo los mensajes deliberadamente saneados llegan a stderr."""
    root = logging.getLogger()
    root.setLevel(logging.DEBUG if verbose else logging.INFO)
    if not root.handlers:
        logging.basicConfig(format="%(asctime)s [%(levelname)s] %(message)s", datefmt="%H:%M:%S")
    for handler in root.handlers:
        if not any(isinstance(f, SafeLogFilter) for f in handler.filters):
            handler.addFilter(SafeLogFilter())
    # También bloquear handlers propios y futuros hijos de las dependencias.
    for name in ("garminconnect", "garth", "requests", "urllib3", "oauthlib", "requests_oauthlib"):
        logger = logging.getLogger(name)
        logger.handlers.clear()
        logger.addHandler(logging.NullHandler())
        logger.propagate = False


class RegulatedHTTPAdapter(HTTPAdapter):
    """Adaptador que garth comparte con sus sesiones OAuth internas."""
    def __init__(self, limiter_provider):
        super().__init__(max_retries=0, pool_connections=10, pool_maxsize=10)
        self.limiter_provider = limiter_provider

    def send(self, request, **kwargs):
        limiter = self.limiter_provider()
        for attempt in range(2):
            limiter.wait()
            try:
                response = super().send(request, **kwargs)
            except Exception:
                limiter.on_error()
                raise
            if response.status_code == 429:
                limiter.on_rate_limit()
                if attempt == 0:
                    response.close()
                    continue
            elif response.status_code >= 400:
                limiter.on_error()
            else:
                limiter.on_success()
            return response


def regulate_transport(api, limiter_provider):
    """Regula cada petición a Garmin, incluidas páginas, redirecciones y OAuth."""
    from requests import Session
    client = getattr(api, "garth", None) or getattr(api, "client", None)
    session = getattr(client, "sess", None)
    if not isinstance(session, Session):
        raise RuntimeError("La biblioteca de Garmin no ofrece un transporte compatible.")
    if getattr(session, "_export_regulated", False):
        api._export_transport_regulated = True
        return api
    # garth remonta los adaptadores al cargar tokens; restaurar la regulación.
    client.retries = 0

    def mount_adapters():
        for prefix in ("https://", "http://"):
            previous = session.adapters.get(prefix)
            if isinstance(previous, RegulatedHTTPAdapter):
                continue
            session.mount(prefix, RegulatedHTTPAdapter(limiter_provider))
            if previous is not None:
                previous.close()

    configure = getattr(client, "configure", None)
    if callable(configure):
        @wraps(configure)
        def configure_regulated(*args, **kwargs):
            result = configure(*args, **kwargs)
            mount_adapters()
            return result
        client.configure = configure_regulated
    mount_adapters()
    session._export_regulated = True
    api._export_transport_regulated = True
    telemetry = getattr(client, "telemetry", None)
    if telemetry is not None:
        telemetry.configure(enabled=False)
    return api


import threading, time
log = logging.getLogger("garmin_export")

class RateLimiter:
    """Regula llamadas y solo reduce el ritmo cuando Garmin lo solicita."""

    def __init__(self, base_delay: float = 0.15):
        self.base_delay = base_delay
        self.current_delay = base_delay
        self.call_count = 0
        self.last_call = 0.0
        self.blocked_until = 0.0
        self.consecutive_ok = 0
        self._lock = threading.Lock()

    def wait(self):
        preventive_pause_pending = False
        while True:
            with self._lock:
                now = time.monotonic()
                if preventive_pause_pending:
                    wait_for = self.blocked_until - now
                    if wait_for <= 0:
                        self.last_call = now
                        return
                else:
                    wait_for = max(
                        self.blocked_until - now,
                        self.current_delay - (now - self.last_call),
                    )
                if wait_for <= 0:
                    self.last_call = now
                    self.call_count += 1
                    if self.call_count % 250 == 0:
                        log.info(
                            "  Pausa de seguridad después de "
                            f"{self.call_count} llamadas a la API..."
                        )
                        self.blocked_until = max(
                            self.blocked_until,
                            now + 2,
                        )
                        preventive_pause_pending = True
                    else:
                        return
            time.sleep(max(wait_for, 0.01))

    def on_success(self):
        with self._lock:
            self.consecutive_ok += 1
            if self.consecutive_ok > 10 and self.current_delay > self.base_delay:
                self.current_delay = max(self.base_delay, self.current_delay * 0.9)

    def on_rate_limit(self):
        with self._lock:
            self.consecutive_ok = 0
            self.current_delay = min(self.current_delay * 2, 10.0)
            self.blocked_until = max(
                self.blocked_until,
                time.monotonic() + 60,
            )
            log.warning(
                "  Límite de Garmin alcanzado: nueva espera "
                f"{self.current_delay:.1f}s; pausando 60s todos los hilos..."
            )

    def on_error(self):
        with self._lock:
            self.consecutive_ok = 0
            self.current_delay = min(self.current_delay * 1.2, 5.0)

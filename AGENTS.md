# AGENTS.md — Contexto para asistentes de IA

Este archivo está dirigido a asistentes que trabajen sobre el proyecto o
analicen sus exportaciones. Las instrucciones de instalación para personas
están en [README.md](README.md).

## Qué hace el proyecto

El script `garmin_export.py` descarga datos de salud y entrenamiento de una
cuenta de Garmin Connect y los guarda como texto con bloques JSON.

Hay dos formatos:

- El modo completo conserva las respuestas originales de Garmin.
- El modo compacto crea objetos semánticos, reduce duplicados y elimina datos
  privados para facilitar el análisis periódico con una IA.

Los archivos son `.txt`, sin bloques de código Markdown. Esta decisión mejora
la compatibilidad con NotebookLM y otras herramientas que fragmentan e
indexan documentos.

Garmin no ofrece una API oficial para uso personal. El proyecto utiliza
`python-garminconnect`, que reproduce las llamadas de Garmin Connect y delega
el inicio de sesión OAuth en `garth`.

## Estructura de la salida

La línea de comandos genera normalmente:

```text
export/garmin_export_AAAA-MM-DD_HHMMSS.txt
```

El lanzador de Windows propone nombres comprensibles:

```text
export/managed/garmin_datos_2026-01-01_a_2026-07-28.txt
```

Las actualizaciones creadas con `--update` añaden el sufijo `_update`.

El compacto semántico contiene estas secciones:

```text
Export Metadata       Metadatos, intervalo, zona horaria y versión
Profile               Edad, sexo, altura, unidades y reloj sin identidad
Daily Health          Un registro semántico por día
Blood Pressure        Solo mediciones reales
Activities            Resúmenes, vueltas, zonas, sensaciones y equipamiento
Body Composition      Peso y composición corporal con unidades explícitas
Training Metrics      Métricas clasificadas por su relación con el intervalo
Goals and Records     Récords y objetivos activos
Gear                  Equipamiento y asociaciones con actividades
Hydration             Solo registros reales
Nutrition             Solo registros reales
Weekly Summary        Totales calculados y distribución de pulso
Data Quality          Ausencias, advertencias, conversiones y privacidad
```

El modo completo añade todas las respuestas originales, incluidas tendencias,
Golf, planes, entrenamientos guardados y salud femenina.

Las secciones vacías indican que no hay datos disponibles.

## Arquitectura

### Un script y tres clases principales

| Clase | Responsabilidad |
|---|---|
| `RateLimiter` | Regula de forma adaptativa y segura entre hilos las llamadas a Garmin. |
| `ExportCache` | Guarda respuestas JSON originales por día, actividad o sección. |
| `GarminExporter` | Autentica, ejecuta cada sección, crea la salida y conserva resultados parciales al interrumpir. |

### Concurrencia

- Salud diaria: hasta 4 llamadas simultáneas.
- Hidratación: hasta 4 días simultáneos.
- Nutrición: hasta 4 días, con 3 llamadas por día.
- Actividades: procesamiento secuencial.
- Todas las llamadas pasan por un único `RateLimiter` protegido con
  `threading.Lock`.

### Caché

La caché se guarda bajo la carpeta de salida seleccionada:

| Tipo | Clave | Ubicación |
|---|---|---|
| Salud diaria | `AAAA-MM-DD` | `.cache/daily/AAAA-MM-DD.json` |
| Hidratación | `hydration_AAAA-MM-DD` | `.cache/daily/` |
| Nutrición | `nutrition_AAAA-MM-DD` | `.cache/daily/` |
| Actividad | identificador de actividad | `.cache/activities/` |
| Sección | nombre e intervalo cuando corresponde | `.cache/sections/` |

Los datos históricos permanecen en caché. En modo compacto se actualizan el
resumen, detalle y equipamiento de las actividades de los últimos 14 días para
recoger cambios o autoevaluaciones sincronizados con retraso.

Las secciones dependientes de fechas utilizan claves con inicio y fin. Así una
respuesta de un intervalo nunca se reutiliza por error en otro.

`--no-cache` fuerza una descarga nueva.

### Intervalos largos

Algunos endpoints rechazan periodos superiores a aproximadamente un año. La
función `_chunked_date_call()` divide el intervalo en bloques de 365 días y
combina las listas resultantes. Se usa para composición corporal, pesajes,
minutos de intensidad, puntuaciones de resistencia y colinas, tolerancia de
carrera y calendario menstrual.

### Regulación de llamadas

| Parámetro | Valor |
|---|---|
| Espera inicial | 0,15 s, configurable con `--delay` |
| Respuesta 429 | Duplica la espera, hasta 10 s; pausa 60 s y reintenta una vez |
| Racha correcta | Reduce gradualmente la espera hacia la base |
| Cada 250 llamadas | Pausa preventiva de 2 s |
| Error general | Aumenta la espera un 20 % |

## Autenticación

- `garth` gestiona el flujo SSO/OAuth de Garmin.
- Los tokens quedan en `~/.garminconnect/`, fuera del repositorio.
- Se admite `.env` o las variables `GARMIN_EMAIL` y `GARMIN_PASSWORD`.
- `--login` autentica y sale sin exportar.
- Nunca deben escribirse credenciales, MFA, cookies ni tokens en código,
  pruebas, registros o documentación.

## Modo compacto (`--compact`)

La transformación se realiza únicamente al escribir. La caché siempre conserva
la respuesta original completa.

El esquema actual es `2.1.1`.

Características:

- Nombres estables en `snake_case` y unidades explícitas.
- Perfil reducido sin nombre, fecha de nacimiento exacta, identificadores,
  números de serie, URL ni capacidades del dispositivo.
- Un registro diario con recuperación, sueño y VFC resumidos.
- Actividades unificadas desde `summary` y `detail.summaryDTO`.
- Vueltas desde `splits.lapDTOs`, con `typed_splits` solo como alternativa.
- Zonas de pulso y potencia, autoevaluación y equipamiento por actividad.
- Métricas posteriores al final solicitado separadas en `current_snapshot`.
- Secciones `Weekly Summary` y `Data Quality`.
- Presión arterial, hidratación y nutrición solo cuando existen datos reales.

### Series temporales

`--activity-details` conserva la máxima resolución disponible con columnas
deportivas aprobadas y sin coordenadas.

Los valores `null` de las matrices posicionales nunca se eliminan. Cada muestra
se valida contra el número de descriptores antes de escribirse. Una fila
malformada se omite y queda indicada en calidad de datos.

Los metadatos diferencian:

```text
activity_series_mode: none
activity_series_mode: full
```

Ambas siguen siendo exportaciones compactas semánticas.

Garmin puede usar grabación inteligente, por lo que la serie no garantiza un
punto por segundo.

### Unidades y normalizaciones

- `sleepNeed.actual` se convierte de minutos a segundos.
- Los epochs de inicio y fin del sueño se interpretan explícitamente como
  milisegundos UTC y se convierten a ISO 8601 con la zona IANA configurada.
- La temperatura meteorológica se convierte de Fahrenheit a Celsius.
- La temperatura directa del sensor se conserva como Celsius.
- RPE `10..100` se convierte a `1..10`; los valores originales se mantienen
  dentro de la autoevaluación.
- La velocidad del endpoint conocido de umbral de lactato conserva el valor
  original y documenta el factor aplicado.
- Los valores cuya unidad no está confirmada permanecen como datos originales
  y no generan conversiones engañosas.

### Zona 0 de pulso

La zona 0 representa únicamente tiempo con pulso válido por debajo de la zona
1:

```text
zona 0 = duración con pulso válido - suma de zonas 1 a 5
```

Los intervalos sin pulso se registran aparte. Nunca deben asignarse
automáticamente a la zona 0.

### Zonas horarias

`--timezone` acepta una zona IANA. En Windows, `Romance Standard Time` se
normaliza a `Europe/Madrid`. El offset se calcula para la fecha histórica
solicitada y respeta los cambios de horario.

## Modo dividido (`--split`)

Activa el compacto y divide la salida para mantenerse por debajo de 500.000
palabras por fuente de NotebookLM. El límite interno es 480.000.

Se divide por secciones. Si una sección es demasiado grande, se fragmenta por
claves o elementos JSON. Los nombres usan:

```text
_split_part1of6.txt
```

## Modo actualización (`--update`)

Busca la exportación más reciente, lee su fecha final y comienza un día antes
para recoger datos sincronizados con retraso. Si no encuentra una exportación,
utiliza el valor de `--days`.

Activa automáticamente el modo compacto.

## Intervalos explícitos y lanzador de Windows

- `--start-date AAAA-MM-DD --end-date AAAA-MM-DD` selecciona un intervalo
  inclusivo.
- `--end-date` requiere una fecha inicial.
- La fecha final no puede ser futura ni anterior a la inicial.
- `--filename` solo admite un nombre dentro de `--output`, nunca una ruta.
- El lanzador propone `garmin_datos_INICIO_a_FIN.txt`.
- Repetir el mismo nombre sustituye solo ese archivo.
- La presión arterial se consulta mediante
  `get_blood_pressure(inicio, fin)`.

## Privacidad

El compacto excluye:

- nombres y datos del propietario;
- identificadores personales y de dispositivos;
- números de serie;
- coordenadas, polilíneas y ubicaciones;
- URL e imágenes de perfil;
- hábitos íntimos;
- catálogos vacíos y estructuras duplicadas.

La caché y las exportaciones contienen datos personales originales, por lo que
`export/`, `.env`, `.garminconnect/` y `.venv/` deben permanecer en
`.gitignore`.

## Compatibilidad con herramientas de IA

- El texto plano evita problemas conocidos de indexación de Markdown.
- No se usan bloques de código JSON porque algunos indexadores los omiten.
- NotebookLM admite hasta 500.000 palabras y 200 MB por fuente.
- NotebookLM usa recuperación por fragmentos; no lee necesariamente el archivo
  completo en cada pregunta.
- El archivo compacto normal es el recomendado para revisiones semanales.
- Las series completas solo convienen para intervalos cortos o sesiones
  concretas.

## Lecciones importantes

1. Garmin no ofrece una API personal oficial; los endpoints pueden cambiar.
2. Los límites de llamadas existen, aunque no están documentados.
3. Algunos endpoints limitan el intervalo a aproximadamente un año.
4. La caché permite reanudar exportaciones largas sin repetir llamadas.
5. Las actividades recientes pueden cambiar después de sincronizarse.
6. La regulación debe ser segura entre hilos.
7. Cada familia de endpoints devuelve estructuras distintas.
8. No todas las cuentas tienen Golf, nutrición, hidratación o salud femenina.
9. Las exportaciones completas pueden ocupar cientos de megabytes.
10. El índice de contenidos ayuda a una IA a orientarse.
11. El texto plano suele funcionar mejor que Markdown para RAG.
12. Las series de Garmin pueden ser listas de objetos o matrices posicionales.
13. Una actualización necesita solapamiento para recoger datos tardíos.
14. Quitar un `null` posicional desplaza todas las columnas posteriores.
15. Zona 0 y ausencia de pulso son conceptos diferentes.
16. La ausencia de sueño o VFC debe permanecer explícita, sin sustituciones.
17. Los logs de depuración solo pueden mostrar método, fecha, forma del payload
    y motivo de ausencia; nunca valores personales ni tokens.

## Dependencias

- `garminconnect`: cliente de Garmin Connect.
- `garth`: autenticación OAuth/SSO.
- `tzdata` en Windows: zonas IANA y cambios horarios históricos.

La edición de Windows utiliza Python 3.11 y .NET 11, tal como se indica en
`README.md` y en el instalador.

## Archivos principales

| Archivo | Finalidad |
|---|---|
| `garmin_export.py` | Exportador y transformaciones |
| `requirements.txt` | Dependencias Python generales |
| `requirements-windows-lock.txt` | Versiones comprobadas para Windows |
| `GarminDataExport.csproj` | Capa .NET de consola |
| `GarminDataExport.Launcher/` | Lanzador gráfico para Windows |
| `Setup-Windows.ps1` | Instalación automatizada |
| `Instalar.bat` | Entrada de instalación por doble clic |
| `README.md` | Guía completa para personas |
| `LEEME_PRIMERO.txt` | Guía rápida |
| `tests/` | Pruebas automatizadas y fixtures anonimizadas |
| `.gitignore` | Exclusión de credenciales, caché y exportaciones |
| `LICENSE` | Licencia Apache 2.0 oficial |

# Exportador de datos de Garmin para Windows

Aplicación gratuita y de código abierto para descargar tus propios datos de
Garmin Connect, guardarlos de forma incremental y preparar un archivo fácil de
analizar con ChatGPT, NotebookLM, Claude u otra IA.

Está pensada especialmente para personas que quieren relacionar entrenamiento,
descanso, sensaciones y recuperación —por ejemplo durante la preparación de
una maratón— sin tener que entender Python, .NET, JSON ni la línea de comandos.

Esta edición permite descargar una copia local de tus datos de Garmin Connect
mediante una ventana sencilla. Puedes elegir las fechas inicial y final,
guardar el resultado con un nombre comprensible e incluir, si lo necesitas,
el máximo detalle temporal registrado durante las actividades.

La aplicación mantiene la capa .NET del proyecto original y utiliza Python
internamente para comunicarse con Garmin Connect.

El formato predeterminado es un **compacto semántico** pensado para analizar
entrenamientos con una IA: conserva las métricas deportivas importantes,
elimina duplicados y excluye nombres, coordenadas, identificadores personales
y números de serie. El modo completo continúa disponible mediante la línea de
comandos cuando se necesiten las respuestas originales sin transformar de
Garmin.

> [!IMPORTANT]
> Este es un proyecto personal y no oficial. No pertenece a Garmin ni está
> respaldado por Garmin. Utilízalo únicamente con tu propia cuenta.

Esta versión está basada en el proyecto original
[sirredbeard/garmin-data-export](https://github.com/sirredbeard/garmin-data-export)
y mantiene su licencia Apache 2.0 y la atribución correspondiente.

## Qué puedes hacer con él

- Elegir una fecha inicial y otra final desde una ventana.
- Descargar salud diaria, actividades, vueltas, zonas, sueño, VFC, peso,
  presión arterial, autoevaluaciones y equipamiento, cuando Garmin los tenga.
- Reutilizar una caché para no descargar repetidamente lo mismo.
- Crear un archivo compacto y privado para una revisión semanal con una IA.
- Añadir las muestras temporales de las actividades cuando necesites analizar
  una sesión concreta con más detalle.
- Mantener una copia local de tus datos sin subir credenciales ni actividades
  a este repositorio.

El programa no crea planes de entrenamiento ni toma decisiones médicas. Se
limita a descargar, ordenar y explicar los datos que Garmin proporciona.

## Instalación en dos minutos

Si no tienes conocimientos informáticos, este es el resumen:

1. Pulsa el botón verde **Code** de GitHub y después **Download ZIP**.
2. Extrae por completo el ZIP.
3. Haz doble clic en `Instalar.bat`.
4. Espera a que termine y sigue las preguntas de la ventana negra.
5. Haz doble clic en `GarminLauncher.exe`.

La explicación detallada, incluidos los posibles avisos de Windows, está justo
debajo.

## Instalación fácil

Estos pasos están pensados para una persona sin conocimientos informáticos.
Solo necesitas un PC con Windows 11, conexión a Internet, una cuenta de Garmin
Connect y acceso a este repositorio público de GitHub.

### 1. Descargar el programa

1. Abre `https://github.com/zarzawan/garmin-data-export`.
2. Pulsa el botón verde **Code**.
3. Pulsa **Download ZIP**. No necesitas una cuenta de GitHub.
4. Abre la carpeta **Descargas** de Windows.
5. Pulsa con el botón derecho sobre el archivo ZIP y elige **Extraer todo**.
6. Entra en la carpeta que Windows acaba de crear.

Es importante extraer el ZIP. El instalador no funcionará correctamente si se
ejecuta desde dentro del archivo comprimido.

### 2. Instalar

1. Haz doble clic en `Instalar.bat`.
2. Si Windows muestra una advertencia, revisa que el archivo procede de tu
   descarga de este repositorio público. Después pulsa **Más información** y
   **Ejecutar de todas formas**.
3. Deja abierta la ventana negra mientras termina la instalación.
4. Acepta las ventanas de Windows que soliciten permiso para instalar Python o
   .NET.

El instalador se encarga automáticamente de:

- instalar Python 3.11 si falta;
- instalar el SDK de .NET 11 si falta;
- crear el entorno privado `.venv`;
- instalar las dependencias comprobadas;
- compilar la aplicación;
- crear `GarminLauncher.exe`.

La primera instalación puede tardar varios minutos.

### 3. Iniciar sesión en Garmin

Si este PC todavía no tiene una sesión guardada, el instalador te preguntará
si quieres iniciar sesión.

1. Pulsa **Intro** para continuar.
2. Escribe tu correo de Garmin y pulsa **Intro**.
3. Escribe tu contraseña y pulsa **Intro**. La contraseña no aparecerá en
   pantalla mientras escribes; es normal.
4. Si utilizas verificación en dos pasos, escribe el código de tu aplicación
   de autenticación.

Las credenciales se envían directamente al inicio de sesión de Garmin. El
proyecto no las guarda. Los tokens de sesión quedan fuera del repositorio, en:

```text
C:\Users\TU_USUARIO\.garminconnect
```

No compartas nunca tu contraseña, código MFA, tokens ni la carpeta
`.garminconnect`.

### 4. Abrir el programa

Cuando el instalador indique que ha terminado:

1. Cierra la ventana del instalador.
2. Haz doble clic en `GarminLauncher.exe`.
3. Elige las fechas **Descargar desde** y **Hasta**. Las dos están incluidas.
4. Revisa el nombre propuesto para el archivo. Puedes cambiarlo.
5. Deja desmarcado el detalle temporal salvo que quieras analizar sesiones
   concretas con la máxima resolución registrada.
6. Pulsa **Crear exportación**.
7. No cierres la ventana hasta que aparezca **Exportación completada**.

## Uso habitual

En las siguientes ocasiones solo tienes que:

1. abrir `GarminLauncher.exe`;
2. elegir el intervalo;
3. revisar el nombre del archivo;
4. pulsar **Crear exportación**.

La aplicación reutiliza una caché local. Solo consulta lo que todavía no se ha
descargado. Las actividades de los últimos 14 días se actualizan para recoger
autoevaluaciones o cambios sincronizados después del entrenamiento. El nombre
predeterminado describe el intervalo de forma clara para una persona y para una
IA:

```text
export\managed\garmin_datos_2026-01-01_a_2026-07-28.txt
```

Si vuelves a usar el mismo nombre, el archivo se sustituye. Así puedes mantener
un único archivo actualizado. Si cambias el nombre, se conservarán ambos.

Los botones **Abrir carpeta** y **Abrir archivo** permiten encontrar el
resultado sin navegar manualmente por las carpetas.

## Qué datos incluye el archivo predeterminado

Según los datos disponibles en tu cuenta, el compacto semántico puede incluir:

- edad al final del periodo, sexo, altura, sistema de unidades, zona horaria y
  modelo del reloj principal, sin nombre ni identificadores;
- pasos, pulso en reposo, sueño, estrés, batería corporal, SpO2, VFC y
  respiración, resumidos en una fila por día;
- mediciones reales de presión arterial, omitiendo respuestas vacías;
- todas las actividades, vueltas, zonas de pulso y potencia, carga, ritmos,
  autoevaluación de Garmin y zapatillas o bicicletas asociadas;
- peso y composición corporal con unidades explícitas;
- métricas de entrenamiento separadas entre datos del periodo, valores
  anteriores, fotografías actuales y valores sin fecha;
- récords personales y objetivos activos, sin insignias ni objetivos antiguos;
- hidratación y nutrición únicamente cuando existe un registro real;
- un resumen semanal calculado y un bloque de calidad de datos.

Para reducir ruido no se incluyen por defecto la biblioteca de entrenamientos,
insignias, Golf, planes antiguos, tendencias de 52 semanas, salud femenina,
catálogos de hábitos sin registrar, hábitos íntimos ni secciones vacías. Todo
ello continúa disponible en el modo completo.

## Qué formato conviene para analizar una maratón con IA

El formato predeterminado es el recomendado:

- texto `.txt` con encabezados claros y bloques JSON normalizados;
- nombres de campo estables con la unidad indicada, como `distance_m`,
  `duration_s`, `heart_rate_bpm` o `weight_kg`;
- una fila diaria sin repetir las respuestas de todos los endpoints;
- actividades sin propietarios, coordenadas, polilíneas ni datos técnicos;
- resúmenes, vueltas, zonas, potencia, carga, recuperación, sueño, VFC, peso,
  presión arterial, autoevaluación y equipamiento;
- metadatos con intervalo, zona horaria y versión del esquema;
- avisos claros cuando Garmin no proporciona sueño, VFC u otros datos.

El esquema compacto `2.1.1` también documenta las conversiones que aplica:

- `sleepNeed.actual` llega en minutos y se guarda como `sleep_need_s`
  multiplicándolo por 60;
- los epochs de inicio y fin del sueño se interpretan explícitamente como
  milisegundos UTC y se presentan en ISO 8601 con la zona configurada, por
  ejemplo `2026-07-24T03:45:01+02:00`;
- la temperatura meteorológica de una actividad llega en Fahrenheit y se
  convierte a Celsius, mientras que la temperatura directa del sensor ya está
  en Celsius;
- la autoevaluación de esfuerzo de Garmin usa valores `10–100` y se presenta
  como escala `1–10`; la sensación se traduce a categorías estables;
- la distribución de pulso añade una zona 0 para el tiempo con pulso válido por
  debajo de la zona 1 y separa los huecos en los que no existe pulso;
- el valor de velocidad del umbral de lactato conserva el dato raw y documenta
  la conversión usada por ese endpoint.

Esto suele aportar más señal y menos ruido para revisar la preparación de una
maratón. Permite que la IA relacione carga, sensaciones, recuperación y
evolución sin recibir grandes catálogos, datos privados o estructuras
duplicadas.

La casilla **Incluir el máximo detalle temporal de las actividades** añade la
serie de métricas que Garmin conserva para cada sesión. En muchos dispositivos
la grabación es aproximadamente segundo a segundo, pero Garmin puede utilizar
grabación inteligente o intervalos variables: la aplicación no inventa puntos
que el reloj no haya registrado. Los valores ausentes se conservan como
`null`, de modo que cada muestra sigue alineada con sus descriptores.

Activa esa casilla para intervalos cortos o para estudiar entrenamientos
importantes, por ejemplo una tirada larga, una prueba de ritmo de maratón o una
sesión de series. Para varios meses, déjala desmarcada y utiliza vueltas,
ritmos, zonas y resúmenes.

## Dónde se guarda cada cosa

| Contenido | Ubicación |
|---|---|
| Archivos que debes utilizar | `export\managed\garmin_datos_FECHA_a_FECHA.txt` |
| Caché incremental | `export\managed\.cache\` |
| Sesión de Garmin | `%USERPROFILE%\.garminconnect\` |
| Preferencia de fecha del lanzador | `%LOCALAPPDATA%\GarminDataExportLauncher\settings.json` |
| Entorno de Python | `.venv\` |

Los datos personales, la caché, los tokens y `.venv` están excluidos de Git.

## Volver a instalar o reparar

Puedes ejecutar `Instalar.bat` otra vez si:

- borraste accidentalmente `GarminLauncher.exe`;
- se actualizó Windows;
- la aplicación dejó de abrirse;
- quieres volver a compilar la versión descargada.

Antes de hacerlo, cierra `GarminLauncher.exe`.

El instalador conserva la caché y las exportaciones existentes. No borra tus
datos.

## Problemas frecuentes

### La ventana se cierra demasiado rápido

Ejecuta `Instalar.bat`, no `Setup-Windows.ps1`. El archivo BAT mantiene la
ventana abierta para que puedas leer el error.

### Windows bloquea el ejecutable

El ejecutable es una compilación personal y no tiene una firma comercial.
Comprueba que descargaste el proyecto desde
`https://github.com/zarzawan/garmin-data-export` antes de utilizar
**Más información → Ejecutar de todas formas**.

### Dice que no existe una sesión guardada

Vuelve a ejecutar `Instalar.bat` y acepta iniciar sesión. También puedes abrir
PowerShell dentro de la carpeta y ejecutar:

```powershell
$env:PATH = "$PWD\.venv\Scripts;$env:PATH"
dotnet run -- --login
```

### Garmin rechaza el inicio de sesión

- Comprueba el correo y la contraseña entrando primero en
  `https://connect.garmin.com`.
- Espera unos minutos si has realizado varios intentos.
- Comprueba que el código MFA todavía no ha caducado.

### La descarga se interrumpe

Abre de nuevo el lanzador y pulsa **Crear exportación**. La caché permite
continuar sin repetir todo el trabajo.

## Uso avanzado

No necesitas estos comandos para utilizar la ventana gráfica.

Mostrar la ayuda:

```powershell
$env:PATH = "$PWD\.venv\Scripts;$env:PATH"
dotnet run -- --help
```

Exportar desde una fecha concreta:

```powershell
dotnet run -- --start-date 2025-01-01 --end-date 2025-03-31 --compact
```

Elegir el nombre e incluir la máxima resolución de las actividades:

```powershell
dotnet run -- --start-date 2025-01-01 --end-date 2025-03-31 --compact --activity-details --filename garmin_datos_2025-01-01_a_2025-03-31_detalle.txt
```

La salida compacta indica `activity_series_mode: none`; con
`--activity-details` indica `activity_series_mode: full`. Ambas siguen siendo
exportaciones compactas semánticas: la segunda añade principalmente las series
temporales.

Forzar una zona horaria IANA distinta de la detectada en Windows:

```powershell
dotnet run -- --start-date 2025-01-01 --end-date 2025-03-31 --compact --timezone Europe/Madrid
```

El programa calcula el desfase UTC según la fecha histórica seleccionada, incluyendo
los cambios entre horario de invierno y verano.

Exportar todo el historial:

```powershell
dotnet run -- --all --split
```

Crear un archivo completo con todas las respuestas originales de Garmin:

```powershell
dotnet run -- --start-date 2025-01-01 --end-date 2025-03-31
```

Ejecutar las pruebas:

```powershell
.\.venv\Scripts\python.exe -m unittest discover -s tests -v
```

Actualizar una exportación creada mediante la línea de comandos:

```powershell
dotnet run -- --update
```

## Seguridad

Nunca añadas a Git ni compartas:

- `export\`;
- `.env`;
- `.garminconnect\`;
- `.venv\`;
- contraseñas, códigos MFA, cookies o tokens.

El archivo `.gitignore` bloquea estas rutas como medida adicional.

## Idioma e identificadores técnicos

La documentación, el instalador, la ventana y los mensajes destinados a
personas están en español. Los argumentos de línea de comandos, nombres de
métodos de Garmin y claves JSON como `sleep_start_local` o `distance_m`
permanecen en inglés porque forman parte del formato técnico estable. Cambiarlos
rompería exportaciones anteriores, pruebas y herramientas que ya los utilizan.

El archivo `LICENSE` conserva el texto oficial en inglés de Apache 2.0 para no
alterar su significado legal.

## Colaborar

Las mejoras y correcciones son bienvenidas mediante una incidencia o una
solicitud de cambios en GitHub. Antes de adjuntar un ejemplo:

- elimina nombres, correos e identificadores;
- no incluyas nunca tokens, cookies, contraseñas ni códigos MFA;
- utiliza fixtures inventadas o anonimizadas;
- comprueba que `export\` no aparece en los archivos preparados para Git.

## Proyecto original y licencia

Esta edición está basada en
[`sirredbeard/garmin-data-export`](https://github.com/sirredbeard/garmin-data-export).

Se distribuye bajo la [licencia Apache 2.0](LICENSE). Garmin y Garmin Connect
son marcas de sus respectivos propietarios.

# Exportador de datos de Garmin para Windows

Esta edición permite descargar una copia local de tus datos de Garmin Connect
mediante una ventana sencilla. Puedes elegir una fecha inicial y actualizar
posteriormente la exportación sin volver a descargarlo todo.

La aplicación mantiene la capa .NET del proyecto original y utiliza Python
internamente para comunicarse con Garmin Connect.

> [!IMPORTANT]
> Este es un proyecto personal y no oficial. No pertenece a Garmin ni está
> respaldado por Garmin. Utilízalo únicamente con tu propia cuenta.

## Instalación fácil

Estos pasos están pensados para una persona sin conocimientos informáticos.
Solo necesitas un PC con Windows 11, conexión a Internet y acceso a este
repositorio privado de GitHub.

### 1. Descargar el programa

1. Inicia sesión en GitHub con la cuenta que tiene acceso a este repositorio.
2. Abre `https://github.com/zarzawan/garmin-data-export`.
3. Pulsa el botón verde **Code**.
4. Pulsa **Download ZIP**.
5. Abre la carpeta **Descargas** de Windows.
6. Pulsa con el botón derecho sobre el archivo ZIP y elige **Extraer todo**.
7. Entra en la carpeta que Windows acaba de crear.

Es importante extraer el ZIP. El instalador no funcionará correctamente si se
ejecuta desde dentro del archivo comprimido.

### 2. Instalar

1. Haz doble clic en `Instalar.bat`.
2. Si Windows muestra una advertencia, revisa que el archivo procede de tu
   repositorio privado. Después pulsa **Más información** y
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
3. Elige en **Descargar desde** la fecha inicial que quieres conservar.
4. Pulsa **Crear o actualizar**.
5. No cierres la ventana hasta que aparezca
   **Actualización completada**.

## Uso habitual

En las siguientes ocasiones solo tienes que:

1. abrir `GarminLauncher.exe`;
2. mantener o cambiar la fecha inicial;
3. pulsar **Crear o actualizar**.

La aplicación reutiliza una caché local. Solo consulta lo que todavía no se ha
descargado y reconstruye un único archivo actualizado:

```text
export\managed\garmin_actual.txt
```

Los botones **Abrir carpeta** y **Abrir archivo** permiten encontrar el
resultado sin navegar manualmente por las carpetas.

## Qué datos descarga

Según los datos disponibles en tu cuenta, puede incluir:

- perfil, configuración y dispositivos;
- pasos, frecuencia cardiaca, sueño, estrés, batería corporal, SpO2, HRV y
  respiración;
- actividades, vueltas, zonas, ejercicios, tiempo y series temporales;
- peso y composición corporal;
- métricas y preparación de entrenamiento;
- objetivos, récords y logros;
- tendencias semanales;
- golf, equipamiento, planes y entrenamientos;
- hidratación, nutrición y salud femenina.

Los bloques JSON conservan los nombres técnicos originales que devuelve la API
de Garmin. No se traducen ni se modifican para evitar perder información.

## Dónde se guarda cada cosa

| Contenido | Ubicación |
|---|---|
| Archivo que debes utilizar | `export\managed\garmin_actual.txt` |
| Caché incremental | `export\.cache\` |
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
Comprueba que lo descargaste de tu repositorio privado antes de utilizar
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

Abre de nuevo el lanzador y pulsa **Crear o actualizar**. La caché permite
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
dotnet run -- --start-date 2025-01-01 --compact
```

Exportar todo el historial:

```powershell
dotnet run -- --all --split
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

## Proyecto original y licencia

Esta edición está basada en
[`sirredbeard/garmin-data-export`](https://github.com/sirredbeard/garmin-data-export).

Se distribuye bajo la [licencia Apache 2.0](LICENSE). Garmin y Garmin Connect
son marcas de sus respectivos propietarios.

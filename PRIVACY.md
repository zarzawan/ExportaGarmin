# Política de privacidad

## Resumen

ExportaGarmin es una aplicación local, gratuita y sin telemetría. Descarga los
datos de la cuenta de Garmin Connect que la persona decide utilizar y crea
archivos en su propio ordenador.

> This program will not transfer any information to other networked systems
> unless specifically requested by the user or the person installing or
> operating it.

ExportaGarmin no envía automáticamente datos a ChatGPT, Claude, NotebookLM,
Google Drive ni ningún otro servicio de inteligencia artificial o
almacenamiento. La persona decide manualmente si comparte un archivo.

## Datos tratados

Según las opciones elegidas, el programa puede tratar localmente:

- sesión y tokens de Garmin Connect;
- perfil deportivo y métricas de salud;
- actividades, GPS, altitud, vueltas y series temporales;
- sueño, estrés, VFC, pulso, peso, presión arterial u otros datos disponibles;
- contexto de carrera, diario y comentarios introducidos por la persona;
- caché de respuestas de Garmin y registros técnicos saneados.

El lanzador no guarda el correo, la contraseña ni el código MFA dentro de sus
preferencias. Garmin y las bibliotecas de autenticación pueden tratarlos en
memoria durante el inicio de sesión. Los tokens resultantes se guardan
localmente para no pedir credenciales en cada uso.

## Conexiones de red

ExportaGarmin se conecta a Garmin Connect únicamente cuando la persona inicia
sesión, comprueba una sesión o solicita una exportación. Garmin no proporciona
una API personal oficial; `garminconnect` y `garth` reproducen las llamadas de
Garmin Connect. El uso de ese servicio también está sujeto a las condiciones y
la política de privacidad de Garmin.

La aplicación no incorpora analítica, publicidad, seguimiento, afiliados,
actualización automática ni un servidor propio. Descargar el programa o abrir
un enlace del README sí implica utilizar GitHub según las decisiones del
usuario y las políticas de GitHub.

SignPath, si acepta el proyecto, solo recibirá artefactos públicos de
compilación durante el proceso del mantenedor. La firma no transfiere a
SignPath sesiones, cachés, exportaciones ni información de usuarios de Garmin.

## Almacenamiento y conservación

Los datos se guardan en el PC:

| Contenido | Ubicación habitual |
|---|---|
| Exportaciones | `Documentos\Garmin para IA\NOMBRE_DEL_PERFIL\` |
| Perfiles, tokens, caché, carrera y diario | `%LOCALAPPDATA%\GarminDataExportLauncher\` |
| Sesión heredada, si existe | `%USERPROFILE%\.garminconnect\` |

La carpeta Documentos puede estar redirigida a OneDrive u otro sistema de
sincronización configurado por la persona. En ese caso, Windows o el proveedor
elegido pueden copiar las exportaciones fuera del PC sin intervención de
ExportaGarmin.

No existe un plazo de conservación impuesto por la aplicación. La persona
puede borrar exportaciones, cachés, perfiles y sesiones cuando quiera siguiendo
la [guía de desinstalación](UNINSTALL.md).

## Privacidad de las exportaciones

El formato compacto aplica automáticamente una política que retira identidad,
credenciales e identificadores internos, pero conserva los datos deportivos
necesarios para el análisis. Los títulos, las coordenadas exactas y los nombres
personalizados de equipamiento pueden revelar información personal. La persona
debe revisar cada archivo antes de compartirlo.

La caché conserva respuestas originales de Garmin y debe tratarse como
información privada. No debe publicarse en GitHub ni adjuntarse a una
incidencia.

## Control de la persona

La persona puede:

- decidir cuándo se conecta el programa a Garmin;
- elegir periodo, actividad, formato y contenido opcional;
- revisar el resultado antes de compartirlo;
- cerrar sesión eliminando los tokens locales;
- eliminar todos los datos locales y la aplicación.

## Contacto y seguridad

Las dudas generales pueden abrirse en
[GitHub Issues](https://github.com/zarzawan/ExportaGarmin/issues), sin adjuntar
datos de salud, credenciales, tokens, rutas personales ni exportaciones reales.
Los problemas de seguridad deben comunicarse mediante
[GitHub Security Advisories](https://github.com/zarzawan/ExportaGarmin/security/advisories/new).

## English summary

ExportaGarmin is a free local application with no telemetry, advertising or
automatic uploads. It connects to Garmin Connect only when the user logs in,
checks a session or requests an export. Tokens, cache, journal entries and
exports are stored on the user's Windows account. The application never sends
exports to ChatGPT, Claude, NotebookLM or other AI services; users choose
manually whether and where to upload a file. Exact GPS tracks and customized
gear names may remain in an export and must be reviewed before sharing.

# Desinstalar ExportaGarmin

ExportaGarmin es portable: no instala servicios, controladores, tareas
programadas ni extensiones del navegador y no necesita una desinstalación de
Windows.

## Quitar solamente el programa

1. Cierra `ExportaGarmin.exe`.
2. Elimina la carpeta que extrajiste del ZIP.
3. Elimina cualquier acceso directo que hubieras creado.

Esto no borra perfiles, sesiones, caché ni exportaciones, para que una futura
versión pueda volver a utilizarlos.

## Eliminar también perfiles y sesiones

Este paso cierra las sesiones locales y borra cachés, carrera, diario y
preferencias de todos los perfiles de la cuenta de Windows actual.

1. Pulsa `Windows + R`.
2. Escribe `%LOCALAPPDATA%` y pulsa **Aceptar**.
3. Elimina la carpeta `GarminDataExportLauncher`.
4. Si utilizaste una versión antigua, pulsa de nuevo `Windows + R`, escribe
   `%USERPROFILE%` y elimina `.garminconnect` si existe.

No compartas esas carpetas: pueden contener tokens y respuestas originales de
Garmin.

## Eliminar las exportaciones

1. Abre **Documentos**.
2. Revisa la carpeta `Garmin para IA`.
3. Conserva los informes que necesites y elimina el resto.
4. Vacía la papelera solo cuando hayas comprobado el contenido.

Si Documentos está sincronizado con OneDrive u otro proveedor, revisa también
su papelera y sus versiones en línea. Eliminar un archivo local puede
sincronizar la eliminación.

## Qué permanece fuera del control de ExportaGarmin

Desinstalar no elimina información de Garmin Connect, archivos que hayas
copiado a otra carpeta ni documentos que hayas subido manualmente a una IA u
otro servicio. Debes gestionarlos directamente en cada servicio.

## English summary

Close the application and delete the extracted program folder. To remove all
local profiles, tokens, cache, race context, journal and preferences, also
delete `%LOCALAPPDATA%\GarminDataExportLauncher`. Delete the legacy
`%USERPROFILE%\.garminconnect` folder only if you no longer need that session.
Exports are stored under `Documents\Garmin para IA` and must be reviewed and
removed separately, including any copies synchronized by OneDrive.

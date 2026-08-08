# Code signing policy

## Estado / Status

ExportaGarmin está preparando su solicitud para participar en el programa
gratuito para proyectos open source de SignPath Foundation. La versión pública
actual todavía no está firmada. Esta página no afirma que exista una solicitud
enviada ni que SignPath la haya aceptado.

ExportaGarmin is preparing its application for the free SignPath Foundation
open-source program. The current public release is not signed yet. This page
does not claim that an application has been submitted or accepted.

Cuando el proyecto sea aceptado, se aplicará esta declaración exigida por
SignPath:

> Free code signing provided by
> [SignPath.io](https://about.signpath.io/), certificate by
> [SignPath Foundation](https://signpath.org/).

Windows mostrará **SignPath Foundation** como editor de las versiones que
hayan sido firmadas mediante este programa.

## Responsables / Team roles

- **Committer and maintainer:**
  [zarzawan](https://github.com/zarzawan).
- **Reviewer:** [zarzawan](https://github.com/zarzawan). Las aportaciones de
  personas sin permiso directo se revisan mediante pull request.
- **Signing approver:** [zarzawan](https://github.com/zarzawan). Cada solicitud
  de firma deberá aprobarse manualmente.

El responsable debe mantener MFA tanto en GitHub como en SignPath. Nunca se
comparten cuentas, tokens ni aprobaciones.

## Qué se firma / Signing scope

Solo se solicitará la firma Authenticode de `ExportaGarmin.exe`, el lanzador
WinForms creado y mantenido en este repositorio a partir de
`GarminDataExport.Launcher/`.

No se firmarán como propios:

- `runtime/python/python.exe`, archivos `.pyd` o cualquier binario de Python;
- bibliotecas o ejecutables incluidos por .NET, Python u otras dependencias;
- archivos `.pyc` del backend;
- binarios publicados por `sirredbeard/garmin-data-export` u otros proyectos;
- ninguna descarga que no proceda del workflow oficial de este repositorio.

El runtime .NET autocontenido se incorpora como biblioteca de sistema dentro
del ejecutable de la aplicación y conserva sus avisos de licencia. La
configuración de artefacto restringe la firma al único ejecutable propio.

## Procedencia / Upstream relationship

ExportaGarmin es un trabajo derivado y un descendiente Git de
[sirredbeard/garmin-data-export](https://github.com/sirredbeard/garmin-data-export),
distribuido por su autor bajo Apache License 2.0. El historial conserva el
commit upstream común y el README atribuye expresamente el proyecto original.
GitHub no muestra actualmente `zarzawan/ExportaGarmin` como un fork formal.

El backend Python y la capa de consola .NET comenzaron en el proyecto original
y han sido modificados para preparar datos deportivos para IA. El lanzador
gráfico `ExportaGarmin.exe`, los perfiles, el diario, los informes, la
privacidad automática y el empaquetado portable fueron desarrollados y se
mantienen en este repositorio. La firma solicitada se limita al lanzador
propio; no se solicitarán firmas de ExportaGarmin para el código derivado ni
para las dependencias de terceros.

## Proceso de publicación / Release process

Tras la aprobación de SignPath, una publicación oficial deberá:

1. partir de una etiqueta protegida y del código público de este repositorio;
2. ejecutarse exclusivamente en un runner alojado por GitHub;
3. instalar únicamente las versiones fijadas y verificar las descargas;
4. ejecutar todas las pruebas y construir el paquete sin firmar;
5. subir el artefacto a GitHub Actions antes de solicitar la firma;
6. solicitar a SignPath una firma Authenticode SHA-256 con sellado de tiempo;
7. requerir la aprobación manual del aprobador;
8. verificar el editor, la validez y el sello de tiempo del ejecutable firmado;
9. calcular el SHA-256 del ZIP ya firmado;
10. publicar exclusivamente el ZIP firmado y su hash en GitHub Releases.

Si la firma, la aprobación o la verificación falla, el workflow deberá fallar
cerrado y no publicará una alternativa sin firmar.

Los identificadores de organización, proyecto y política se guardarán como
variables de GitHub. El token de API se guardará únicamente como secreto de
GitHub Actions. Ninguno de esos valores se incluirá en el repositorio ni en
los registros.

La propuesta técnica, todavía inactiva, está documentada en
[docs/SIGNPATH_INTEGRATION.md](docs/SIGNPATH_INTEGRATION.md).

## Incidentes y revocación / Incidents and revocation

Una firma se solicitará únicamente para una versión oficial revisada. Ante una
sospecha de compromiso, publicación incorrecta o incumplimiento:

- se detendrán nuevas firmas y publicaciones;
- se investigarán el commit, el workflow, el artefacto y la aprobación;
- se informará a SignPath cuando corresponda;
- se solicitará la revocación si la integridad de una firma no puede
  garantizarse;
- se publicará una corrección y un aviso para los usuarios.

Los problemas de seguridad pueden comunicarse de forma privada mediante
[GitHub Security Advisories](https://github.com/zarzawan/ExportaGarmin/security/advisories/new).

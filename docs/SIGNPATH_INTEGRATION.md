# Integración prevista con SignPath

Esta integración está **preparada pero no activada**. SignPath Foundation no
aprobó la primera solicitud por falta de adopción y visibilidad pública
suficientes. No existen identificadores ni secretos válidos. El workflow
actual continúa publicando versiones sin firma. Solo se activará si una futura
solicitud es aceptada.

## Alcance

La configuración propuesta en
[`.signpath/artifact-configuration.xml`](../.signpath/artifact-configuration.xml)
firma exclusivamente `ExportaGarmin.exe`. No utiliza comodines para ejecutables
o DLL y, por tanto, no firma Python, archivos `.pyd`, dependencias externas ni
binarios upstream.

La entrada de SignPath será el ZIP sin firmar creado por
`scripts/Build-PortableRelease.ps1`. SignPath abrirá ese ZIP, firmará el único
ejecutable declarado y devolverá un nuevo ZIP. Ese resultado firmado será el
ZIP final de GitHub Releases.

El servicio de firma basado en archivos de SignPath gestiona automáticamente
el sellado de tiempo. El workflow verificará además que Windows reconoce la
firma y que existe un certificado de sello de tiempo antes de publicar.

## Datos que debe proporcionar SignPath

Después de la aceptación deben configurarse, sin inventarlos:

| Tipo | Nombre sugerido en GitHub | Contenido |
|---|---|---|
| Secreto | `SIGNPATH_API_TOKEN` | Token del usuario con permiso de submitter |
| Variable | `SIGNPATH_ORGANIZATION_ID` | Identificador de la organización |
| Variable | `SIGNPATH_PROJECT_SLUG` | Slug del proyecto |
| Variable | `SIGNPATH_SIGNING_POLICY_SLUG` | Slug de la política con aprobación manual |
| Variable | `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG` | Slug de la configuración de artefacto |

El token nunca se pasará como argumento de PowerShell, se imprimirá ni se
guardará en un artefacto. GitHub debe mantener permisos predeterminados de solo
lectura y conceder `contents: write` únicamente al trabajo que crea la Release.

También será necesario:

1. instalar la aplicación oficial de SignPath para este repositorio;
2. asociar GitHub.com como Trusted Build System;
3. limitar el proyecto de SignPath a runners alojados por GitHub;
4. asignar a `zarzawan` como submitter y aprobador;
5. activar MFA en GitHub y SignPath;
6. cargar y revisar la configuración de artefacto propuesta;
7. configurar una política Authenticode SHA-256 con aprobación manual.

## Cambio pendiente en GitHub Actions

Tras la aprobación se sustituirá la publicación directa de
`.github/workflows/release.yml` por esta secuencia. Los identificadores se leen
de variables y el único secreto es el token:

```yaml
permissions:
  actions: read
  contents: write

steps:
  # checkout, Python, .NET, versión y Build-PortableRelease.ps1 permanecen

  - name: Subir ZIP sin firmar
    id: upload-unsigned-artifact
    uses: actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a # v7
    with:
      name: exportagarmin-unsigned-${{ steps.version.outputs.version }}
      path: artifacts/ExportaGarmin-${{ steps.version.outputs.version }}-Windows-x64.zip
      archive: false

  - name: Solicitar firma a SignPath
    uses: signpath/github-action-submit-signing-request@b9d91eadd323de506c0c81cf0c7fe7438f3360fd # v2
    with:
      api-token: ${{ secrets.SIGNPATH_API_TOKEN }}
      organization-id: ${{ vars.SIGNPATH_ORGANIZATION_ID }}
      project-slug: ${{ vars.SIGNPATH_PROJECT_SLUG }}
      signing-policy-slug: ${{ vars.SIGNPATH_SIGNING_POLICY_SLUG }}
      artifact-configuration-slug: ${{ vars.SIGNPATH_ARTIFACT_CONFIGURATION_SLUG }}
      github-artifact-id: ${{ steps.upload-unsigned-artifact.outputs.artifact-id }}
      wait-for-completion: true
      output-artifact-directory: artifacts/signed
      parameters: |
        version: ${{ toJSON(steps.version.outputs.version) }}

  - name: Verificar firma y crear SHA-256 final
    shell: pwsh
    run: |
      $version = '${{ steps.version.outputs.version }}'
      $zip = "artifacts/signed/ExportaGarmin-$version-Windows-x64.zip"
      $check = Join-Path $env:RUNNER_TEMP 'exportagarmin-signature-check'
      Expand-Archive -LiteralPath $zip -DestinationPath $check -Force
      $exe = Join-Path $check "ExportaGarmin-$version-Windows-x64/ExportaGarmin.exe"
      $signature = Get-AuthenticodeSignature -LiteralPath $exe
      if ($signature.Status -ne 'Valid') {
        throw "La firma Authenticode no es válida: $($signature.Status)"
      }
      if ($signature.SignerCertificate.Subject -notmatch 'SignPath Foundation') {
        throw 'El editor de la firma no es SignPath Foundation.'
      }
      if (-not $signature.TimeStamperCertificate) {
        throw 'La firma no contiene un sello de tiempo.'
      }
      $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
      "$hash  ExportaGarmin-$version-Windows-x64.zip" |
        Set-Content -LiteralPath "$zip.sha256" -Encoding ASCII

  # La Release utiliza exclusivamente artifacts/signed/*.zip y *.sha256.
```

Los identificadores de commit de las acciones deberán revisarse y actualizarse
de forma explícita cuando corresponda. No se publicará una ruta alternativa
sin firma cuando SignPath esté habilitado.
Un fallo, rechazo o tiempo de espera abortará la Release.

## Comprobaciones posteriores a la aprobación

- Confirmar que la configuración de SignPath acepta exactamente el ZIP
  generado por GitHub Actions.
- Comprobar que solo cambia `ExportaGarmin.exe` dentro del ZIP.
- Verificar `Status = Valid`, editor `SignPath Foundation` y sello de tiempo.
- Ejecutar `ExportaGarmin.exe --diagnose` después de la firma.
- Volver a ejecutar las 200 pruebas y la comprobación portable.
- Actualizar el README y las notas de la Release de “solicitud no aprobada” a
  “firma activa”.

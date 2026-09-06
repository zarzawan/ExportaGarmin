[CmdletBinding()]
param(
    [string]$Version,

    [string]$OutputDirectory,

    [string]$BuilderPython,

    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$versionProperties = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props')
$declaredVersion = [string]$versionProperties.Project.PropertyGroup.ExportaGarminVersion
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $declaredVersion }
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or $Version -cne $declaredVersion) {
    throw 'La versión debe coincidir con Directory.Build.props.'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot 'artifacts'
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$packageName = "ExportaGarmin-$Version-Windows-x64"
$packageRoot = Join-Path $outputRoot $packageName
$stagingRoot = Join-Path $outputRoot '.staging'
$publishRoot = Join-Path $stagingRoot 'dotnet'
$cacheRoot = Join-Path $outputRoot '.cache'
$zipPath = Join-Path $outputRoot "$packageName.zip"
$hashPath = "$zipPath.sha256"

$pythonVersion = '3.11.9'
$pythonArchiveName = "python-$pythonVersion-embeddable-amd64.zip"
$pythonArchiveUrl = "https://www.python.org/ftp/python/$pythonVersion/$pythonArchiveName"
$pythonArchiveSha256 =
    '33b448f95fecb7c6f802157dbd5e6b40a2ad9bfc8b95ca634a06ba4073ad1ac0'
$pythonArchive = Join-Path $cacheRoot $pythonArchiveName
$dotnetRuntimeVersion = '10.0.10'
$dotnetNotices = @(
    @{
        Name = 'DOTNET_LICENSE.txt'
        Url = "https://raw.githubusercontent.com/dotnet/runtime/v$dotnetRuntimeVersion/LICENSE.TXT"
        Sha256 = 'cfc21f5e8bd655ae997eec916138b707b1d290b83272c02a95c9f821b8c87310'
    },
    @{
        Name = 'DOTNET_THIRD_PARTY_NOTICES.txt'
        Url = "https://raw.githubusercontent.com/dotnet/runtime/v$dotnetRuntimeVersion/THIRD-PARTY-NOTICES.TXT"
        Sha256 = '66f1d4e44973185519bb4aa8a9718eb22fc7af2cc532e3ae9cfc4c127ee7fc54'
    }
)

function Remove-SafeDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }
    $resolved = [IO.Path]::GetFullPath($Path)
    $prefix = $outputRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith(
            $prefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Se rechazó borrar una carpeta fuera de artifacts: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$FailureMessage
    )

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage Código de error: $LASTEXITCODE."
    }
}

function Resolve-BuilderPython {
    if (-not [string]::IsNullOrWhiteSpace($BuilderPython)) {
        return [IO.Path]::GetFullPath($BuilderPython)
    }
    $localPython = Join-Path $projectRoot '.venv\Scripts\python.exe'
    if (Test-Path -LiteralPath $localPython -PathType Leaf) {
        return $localPython
    }
    $pythonCommand = Get-Command python -ErrorAction SilentlyContinue
    if ($pythonCommand) {
        return $pythonCommand.Source
    }
    throw 'No se encontró Python 3.11 para construir el paquete.'
}

function Assert-Python311 {
    param([Parameter(Mandatory = $true)][string]$Executable)

    $version = & $Executable -c (
        "import sys; print('{0}.{1}'.format(*sys.version_info[:2]))")
    if ($LASTEXITCODE -ne 0 -or
        -not [string]::Equals(
            ([string]$version).Trim(),
            '3.11',
            [StringComparison]::Ordinal)) {
        throw 'La construcción requiere Python 3.11.'
    }
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $cacheRoot -Force | Out-Null
Remove-SafeDirectory -Path $packageRoot
Remove-SafeDirectory -Path $stagingRoot
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $hashPath -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

$builderPythonExe = Resolve-BuilderPython
Assert-Python311 -Executable $builderPythonExe

if (-not $SkipTests) {
    & (Join-Path $PSScriptRoot 'Validate-Project.ps1') -PythonPath $builderPythonExe
}

Invoke-Checked `
    -Executable 'dotnet' `
    -Arguments @(
        'publish',
        (Join-Path $projectRoot `
            'GarminDataExport.Launcher\GarminDataExport.Launcher.csproj'),
        '-c', 'Release',
        '-f', 'net10.0-windows',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:PublishTrimmed=false',
        '-o', $publishRoot) `
    -FailureMessage 'No se pudo publicar el lanzador .NET.'

$launcher = Join-Path $publishRoot 'ExportaGarmin.exe'
if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
    throw 'La publicación no generó ExportaGarmin.exe.'
}
$launcherInfo = (Get-Item -LiteralPath $launcher).VersionInfo
$expectedMetadata = @{
    ProductName = 'ExportaGarmin'
    ProductVersion = $Version
    FileVersion = "$Version.0"
    CompanyName = 'zarzawan'
    FileDescription = 'ExportaGarmin'
    LegalCopyright =
        'Copyright © 2026 zarzawan and ExportaGarmin contributors'
}
foreach ($attribute in $expectedMetadata.GetEnumerator()) {
    $actualValue = [string]$launcherInfo.($attribute.Key)
    if (-not [string]::Equals(
            $actualValue,
            [string]$attribute.Value,
            [StringComparison]::Ordinal)) {
        throw (
            "Metadato incoherente en ExportaGarmin.exe: " +
            "$($attribute.Key)='$actualValue'; " +
            "se esperaba '$($attribute.Value)'."
        )
    }
}
Copy-Item -LiteralPath $launcher -Destination (
    Join-Path $packageRoot 'ExportaGarmin.exe')

if (-not (Test-Path -LiteralPath $pythonArchive -PathType Leaf) -or
    -not [string]::Equals(
        (Get-FileHash -LiteralPath $pythonArchive -Algorithm SHA256).Hash,
        $pythonArchiveSha256,
        [StringComparison]::OrdinalIgnoreCase)) {
    Remove-Item -LiteralPath $pythonArchive -Force -ErrorAction SilentlyContinue
    Write-Host "Descargando Python $pythonVersion portable..." -ForegroundColor Cyan
    Invoke-WebRequest `
        -Uri $pythonArchiveUrl `
        -OutFile $pythonArchive `
        -UseBasicParsing
}
$actualPythonHash =
    (Get-FileHash -LiteralPath $pythonArchive -Algorithm SHA256).Hash
if (-not [string]::Equals(
        $actualPythonHash,
        $pythonArchiveSha256,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'El SHA-256 del runtime oficial de Python no coincide.'
}

foreach ($notice in $dotnetNotices) {
    $cachedNotice = Join-Path (
        $cacheRoot) "dotnet-$dotnetRuntimeVersion-$($notice.Name)"
    if (-not (Test-Path -LiteralPath $cachedNotice -PathType Leaf) -or
        -not [string]::Equals(
            (Get-FileHash -LiteralPath $cachedNotice -Algorithm SHA256).Hash,
            $notice.Sha256,
            [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item `
            -LiteralPath $cachedNotice `
            -Force `
            -ErrorAction SilentlyContinue
        Invoke-WebRequest `
            -Uri $notice.Url `
            -OutFile $cachedNotice `
            -UseBasicParsing
    }
    $actualNoticeHash =
        (Get-FileHash -LiteralPath $cachedNotice -Algorithm SHA256).Hash
    if (-not [string]::Equals(
            $actualNoticeHash,
            $notice.Sha256,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "El SHA-256 de $($notice.Name) no coincide."
    }
    Copy-Item `
        -LiteralPath $cachedNotice `
        -Destination (Join-Path $packageRoot $notice.Name)
}

$portablePythonRoot = Join-Path $packageRoot 'runtime\python'
New-Item -ItemType Directory -Path $portablePythonRoot -Force | Out-Null
Expand-Archive `
    -LiteralPath $pythonArchive `
    -DestinationPath $portablePythonRoot `
    -Force

$pythonPathFile = Join-Path $portablePythonRoot 'python311._pth'
@(
    'python311.zip',
    '.',
    'Lib\site-packages',
    '..\..\app',
    'import site'
) | Set-Content -LiteralPath $pythonPathFile -Encoding ASCII

$sitePackages = Join-Path $portablePythonRoot 'Lib\site-packages'
New-Item -ItemType Directory -Path $sitePackages -Force | Out-Null
Invoke-Checked `
    -Executable $builderPythonExe `
    -Arguments @(
        '-m', 'pip', 'install',
        '--disable-pip-version-check',
        '--no-compile',
        '--no-deps',
        '--require-hashes',
        '--only-binary=:all:',
        '--requirement',
        (Join-Path $projectRoot 'requirements-windows-lock.txt'),
        '--target',
        $sitePackages) `
    -FailureMessage 'No se pudieron preparar las dependencias portables.'

$applicationRoot = Join-Path $packageRoot 'app'
New-Item -ItemType Directory -Path $applicationRoot -Force | Out-Null
Invoke-Checked `
    -Executable $builderPythonExe `
    -Arguments @((Join-Path $PSScriptRoot 'Compile-Backend.py'), $applicationRoot) `
    -FailureMessage 'No se pudo compilar y validar el backend portable.'
foreach ($file in @(
        'README.md',
        'LEEME_PRIMERO.txt',
        'LICENSE',
        'LICENCIAS_TERCEROS.txt',
        'CODE_SIGNING.md',
        'PRIVACY.md',
        'UNINSTALL.md',
        'requirements-windows-lock.txt')) {
    Copy-Item `
        -LiteralPath (Join-Path $projectRoot $file) `
        -Destination (Join-Path $packageRoot $file)
}
$documentationImages = Join-Path $projectRoot 'docs\images'
if (-not (Test-Path -LiteralPath $documentationImages -PathType Container)) {
    throw 'No se encontraron las capturas públicas de documentación.'
}
New-Item `
    -ItemType Directory `
    -Path (Join-Path $packageRoot 'docs') `
    -Force | Out-Null
Copy-Item `
    -LiteralPath $documentationImages `
    -Destination (Join-Path $packageRoot 'docs\images') `
    -Recurse
@(
    "ExportaGarmin $Version",
    '.NET 10 LTS autocontenido',
    "Python $pythonVersion portable",
    'Arquitectura: Windows x64'
) | Set-Content `
    -LiteralPath (Join-Path $packageRoot 'VERSION.txt') `
    -Encoding UTF8
$sourceCommit = 'no disponible'
try {
    $candidateCommit = (& git -C $projectRoot rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -eq 0 -and $candidateCommit) {
        $sourceCommit = ([string]$candidateCommit).Trim()
    }
} catch {
    # El enlace al repositorio sigue siendo suficiente.
}
@(
    'CÓDIGO FUENTE DE EXPORTAGARMIN',
    '===============================',
    '',
    'Esta descarga contiene la aplicación preparada para su uso normal.',
    'El código fuente se publica por separado en:',
    '',
    'https://github.com/zarzawan/ExportaGarmin',
    '',
    "Versión: $Version",
    "Commit de referencia: $sourceCommit",
    '',
    'Licencia: Apache License 2.0.'
) | Set-Content `
    -LiteralPath (Join-Path $packageRoot 'CODIGO_FUENTE.txt') `
    -Encoding UTF8

$portablePython = Join-Path $portablePythonRoot 'python.exe'
Invoke-Checked `
    -Executable $portablePython `
    -Arguments @(
        '-c',
        'import garminconnect,garth,openpyxl,pydantic,tzdata;print(1)') `
    -FailureMessage 'El backend portable no puede importar sus dependencias.'
Invoke-Checked `
    -Executable $portablePython `
    -Arguments @((Join-Path $applicationRoot 'garmin_export.pyc'), '--help') `
    -FailureMessage 'El backend portable no puede mostrar su ayuda.'
$diagnosticProcess = Start-Process `
    -FilePath (Join-Path $packageRoot 'ExportaGarmin.exe') `
    -ArgumentList '--diagnose' `
    -WindowStyle Hidden `
    -Wait `
    -PassThru
try {
    if ($diagnosticProcess.ExitCode -ne 0) {
        throw (
            'El diagnóstico del ejecutable portable ha fallado. ' +
            "Código de error: $($diagnosticProcess.ExitCode)."
        )
    }
} finally {
    $diagnosticProcess.Dispose()
}

$forbiddenEntries = Get-ChildItem -LiteralPath $packageRoot -Recurse -Force |
    Where-Object {
        $_.Name -in @(
            '.env',
            '.garminconnect',
            'oauth1_token.json',
            'oauth2_token.json') -or
        $_.FullName -match '[\\/](export|cache|sesion)[\\/]'
    }
if ($forbiddenEntries) {
    throw 'El paquete contiene una ruta reservada para datos privados.'
}

$compressionSucceeded = $false
for ($attempt = 1; $attempt -le 3 -and -not $compressionSucceeded; $attempt++) {
    try {
        Remove-Item `
            -LiteralPath $zipPath `
            -Force `
            -ErrorAction SilentlyContinue
        Compress-Archive `
            -LiteralPath $packageRoot `
            -DestinationPath $zipPath `
            -CompressionLevel Optimal
        $compressionSucceeded = $true
    } catch {
        if ($attempt -eq 3) {
            throw
        }
        Start-Sleep -Milliseconds 750
    }
}
$releaseHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
"$releaseHash  $packageName.zip" |
    Set-Content -LiteralPath $hashPath -Encoding ASCII

Write-Host ''
Write-Host 'Paquete portable validado:' -ForegroundColor Green
Write-Host $zipPath
Write-Host 'SHA-256:' -ForegroundColor Green
Write-Host $releaseHash

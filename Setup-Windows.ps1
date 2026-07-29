[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $projectRoot

function Refresh-ProcessPath {
    $userPath = [Environment]::GetEnvironmentVariable(
        'PATH',
        [EnvironmentVariableTarget]::User)
    $machinePath = [Environment]::GetEnvironmentVariable(
        'PATH',
        [EnvironmentVariableTarget]::Machine)
    $env:PATH = "$userPath;$machinePath"
}

function Find-Python311 {
    $pyLauncher = Get-Command py -ErrorAction SilentlyContinue
    if ($pyLauncher) {
        $resolved = & $pyLauncher.Source -3.11 -c "import sys; print(sys.executable)" 2>$null
        if ($LASTEXITCODE -eq 0 -and $resolved -and
            (Test-Path -LiteralPath $resolved.Trim())) {
            return $resolved.Trim()
        }
    }

    $pythonCommand = Get-Command python -ErrorAction SilentlyContinue
    if ($pythonCommand) {
        $version = & $pythonCommand.Source -c "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}')" 2>$null
        if ($LASTEXITCODE -eq 0 -and $version.Trim() -eq '3.11') {
            return $pythonCommand.Source
        }
    }

    return $null
}

function Test-Python311Executable {
    param([Parameter(Mandatory = $true)][string]$Executable)

    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) {
        return $false
    }
    try {
        $versionOutput = & $Executable -c "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}')" 2>$null
        return $LASTEXITCODE -eq 0 -and
            [string]::Equals(
                ([string]$versionOutput).Trim(),
                '3.11',
                [StringComparison]::Ordinal)
    } catch {
        return $false
    }
}

function Find-DotNet {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetCommand) {
        return $dotnetCommand.Source
    }

    $standardPath = 'C:\Program Files\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $standardPath) {
        return $standardPath
    }

    return $null
}

function Require-Winget {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw 'Windows no tiene disponible winget, necesario para instalar los programas que faltan.'
    }
}

Write-Host ''
Write-Host 'EntrenaIA - Exportador de Garmin para IA - Instalación' -ForegroundColor Cyan
Write-Host '=========================================================' -ForegroundColor Cyan
Write-Host ''
Write-Host 'No cierres esta ventana. Se comprobarán todos los requisitos.' -ForegroundColor Gray
Write-Host ''

$runningLauncher = Get-Process GarminLauncher -ErrorAction SilentlyContinue
if ($runningLauncher) {
    throw 'GarminLauncher.exe está abierto. Cierra su ventana y ejecuta de nuevo Instalar.bat.'
}

$pythonExe = Find-Python311
if (-not $pythonExe) {
    Require-Winget
    Write-Host 'Instalando Python 3.11...' -ForegroundColor Yellow
    winget install `
        --id Python.Python.3.11 `
        --exact `
        --source winget `
        --accept-source-agreements `
        --accept-package-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo instalar Python. Código de error: $LASTEXITCODE."
    }
    Refresh-ProcessPath
    $pythonExe = Find-Python311
    if (-not $pythonExe) {
        throw 'Python 3.11 se instaló, pero todavía no aparece en Windows. Reinicia el PC y ejecuta de nuevo Instalar.bat.'
    }
}
Write-Host "Python encontrado: $pythonExe" -ForegroundColor Green

$dotnetExe = Find-DotNet
$hasDotNet11 = $false
if ($dotnetExe) {
    $installedSdks = & $dotnetExe --list-sdks
    $hasDotNet11 = [bool]($installedSdks -match '^11\.')
}

if (-not $hasDotNet11) {
    Require-Winget
    Write-Host 'Instalando el SDK de .NET 11...' -ForegroundColor Yellow

    winget show `
        --id Microsoft.DotNet.SDK.11 `
        --exact `
        --source winget `
        --accept-source-agreements *> $null
    if ($LASTEXITCODE -eq 0) {
        $dotnetPackage = 'Microsoft.DotNet.SDK.11'
    } else {
        # Microsoft publica la versión preliminar vigente bajo este ID
        # genérico. La comprobación posterior impide aceptar otra versión.
        winget show `
            --id Microsoft.DotNet.SDK.Preview `
            --exact `
            --source winget `
            --accept-source-agreements *> $null
        if ($LASTEXITCODE -ne 0) {
            throw (
                'winget no ofrece todavía el SDK 11. Instálalo desde ' +
                'https://dotnet.microsoft.com/download/dotnet/11.0 ' +
                'y ejecuta de nuevo Instalar.bat.'
            )
        }
        $dotnetPackage = 'Microsoft.DotNet.SDK.Preview'
    }

    winget install `
        --id $dotnetPackage `
        --exact `
        --source winget `
        --accept-source-agreements `
        --accept-package-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo instalar .NET. Código de error: $LASTEXITCODE."
    }
    Refresh-ProcessPath
    $dotnetExe = Find-DotNet
    if (-not $dotnetExe) {
        throw '.NET se instaló, pero todavía no aparece en Windows. Reinicia el PC y ejecuta de nuevo Instalar.bat.'
    }
}
$installedSdks = & $dotnetExe --list-sdks
if ($LASTEXITCODE -ne 0 -or -not [bool]($installedSdks -match '^11\.')) {
    throw 'El SDK de .NET 11 todavía no está disponible. Reinicia el PC y ejecuta de nuevo Instalar.bat.'
}
Write-Host ".NET encontrado: $dotnetExe" -ForegroundColor Green

$venvRoot = Join-Path $projectRoot '.venv'
$venvPython = Join-Path $venvRoot 'Scripts\python.exe'
if ((Test-Path -LiteralPath $venvRoot) -and
    -not (Test-Python311Executable -Executable $venvPython)) {
    $projectPrefix = [IO.Path]::GetFullPath($projectRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolvedVenv = [IO.Path]::GetFullPath($venvRoot)
    $backupName = '.venv.incompatible-{0}-{1}' -f `
        (Get-Date -Format 'yyyyMMdd-HHmmss'), `
        ([Guid]::NewGuid().ToString('N').Substring(0, 6))
    $venvBackup = Join-Path $projectRoot $backupName
    $resolvedBackup = [IO.Path]::GetFullPath($venvBackup)
    if (-not $resolvedVenv.StartsWith(
            $projectPrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not $resolvedBackup.StartsWith(
            $projectPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'No se puede reparar .venv porque su ruta no está dentro del proyecto.'
    }
    Move-Item -LiteralPath $resolvedVenv -Destination $resolvedBackup
    Write-Host "El entorno anterior no era Python 3.11 y se ha conservado en: $resolvedBackup" -ForegroundColor Yellow
}

if (-not (Test-Python311Executable -Executable $venvPython)) {
    Write-Host 'Creando el entorno privado de Python...' -ForegroundColor Yellow
    & $pythonExe -m venv $venvRoot
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo crear el entorno de Python. Código de error: $LASTEXITCODE."
    }
}
if (-not (Test-Python311Executable -Executable $venvPython)) {
    throw 'El entorno .venv no funciona con Python 3.11. Revisa el antivirus y ejecuta de nuevo Instalar.bat.'
}

Write-Host 'Instalando las dependencias comprobadas de Python...' -ForegroundColor Yellow
& $venvPython -m pip install -r (Join-Path $projectRoot 'requirements-windows-lock.txt')
if ($LASTEXITCODE -ne 0) {
    throw "No se pudieron instalar las dependencias de Python. Código de error: $LASTEXITCODE."
}
& $venvPython -m pip check
if ($LASTEXITCODE -ne 0) {
    throw 'La comprobación de las dependencias de Python ha fallado.'
}

Write-Host 'Compilando la aplicación .NET...' -ForegroundColor Yellow
& $dotnetExe restore (Join-Path $projectRoot 'GarminDataExport.slnx')
if ($LASTEXITCODE -ne 0) {
    throw "No se pudieron restaurar las dependencias de .NET. Código de error: $LASTEXITCODE."
}
# La versión preliminar de .NET 11 puede terminar con código 1 y sin errores
# al compilar esta solución en paralelo. La compilación secuencial es estable.
& $dotnetExe build (Join-Path $projectRoot 'GarminDataExport.slnx') --no-restore -m:1
if ($LASTEXITCODE -ne 0) {
    throw "La compilación de .NET ha fallado. Código de error: $LASTEXITCODE."
}

$publishDirectory = Join-Path $projectRoot 'GarminDataExport.Launcher\bin\publish'
Write-Host 'Creando GarminLauncher.exe...' -ForegroundColor Yellow
& $dotnetExe publish `
    (Join-Path $projectRoot 'GarminDataExport.Launcher\GarminDataExport.Launcher.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo crear GarminLauncher.exe. Código de error: $LASTEXITCODE."
}

$publishedExe = Join-Path $publishDirectory 'GarminLauncher.exe'
$rootExe = Join-Path $projectRoot 'GarminLauncher.exe'
Copy-Item -LiteralPath $publishedExe -Destination $rootExe -Force

Write-Host ''
Write-Host 'La instalación de la aplicación se ha completado correctamente.' -ForegroundColor Green
Write-Host "Lanzador: $rootExe"
Write-Host ''
Write-Host 'Siguiente paso:' -ForegroundColor Cyan
Write-Host '1. Cierra esta ventana.'
Write-Host '2. Haz doble clic en GarminLauncher.exe.'
Write-Host '3. Sigue el asistente Primeros pasos para elegir perfil e iniciar sesión.'
Write-Host ''
Write-Host 'Escribe tus credenciales y el MFA únicamente en la ventana de inicio de sesión.' -ForegroundColor Yellow

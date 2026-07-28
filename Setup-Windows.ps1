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
Write-Host 'Exportador de datos de Garmin - Instalación para Windows' -ForegroundColor Cyan
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
Write-Host ".NET encontrado: $dotnetExe" -ForegroundColor Green

$venvPython = Join-Path $projectRoot '.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $venvPython)) {
    Write-Host 'Creando el entorno privado de Python...' -ForegroundColor Yellow
    & $pythonExe -m venv (Join-Path $projectRoot '.venv')
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo crear el entorno de Python. Código de error: $LASTEXITCODE."
    }
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
& $dotnetExe build (Join-Path $projectRoot 'GarminDataExport.slnx') --no-restore
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

$tokenDirectory = Join-Path $env:USERPROFILE '.garminconnect'
$hasSavedSession = (Test-Path -LiteralPath $tokenDirectory) -and
    [bool](Get-ChildItem -LiteralPath $tokenDirectory -File -ErrorAction SilentlyContinue)

if (-not $hasSavedSession) {
    Write-Host ''
    Write-Host 'Este PC todavía no tiene una sesión guardada de Garmin.' -ForegroundColor Yellow
    Write-Host 'Tus credenciales se enviarán directamente a Garmin y no se guardarán en el proyecto.'
    Write-Host 'No compartas con nadie tu contraseña, código MFA ni tokens.'
    Write-Host ''
    $loginAnswer = Read-Host 'Pulsa Intro para iniciar sesión ahora o escribe N para hacerlo más tarde'
    if ($loginAnswer -notmatch '^(?i:n|no)$') {
        Write-Host ''
        Write-Host 'Iniciando el acceso seguro a Garmin...' -ForegroundColor Yellow
        $previousPath = $env:PATH
        try {
            $env:PATH = "$(Join-Path $projectRoot '.venv\Scripts');$env:PATH"
            & $dotnetExe run `
                --project (Join-Path $projectRoot 'GarminDataExport.csproj') `
                -- `
                --login
            if ($LASTEXITCODE -ne 0) {
                Write-Warning 'No se pudo completar el inicio de sesión. Puedes ejecutar Instalar.bat otra vez para reintentarlo.'
            } else {
                Write-Host 'La sesión de Garmin se ha guardado correctamente.' -ForegroundColor Green
            }
        } finally {
            $env:PATH = $previousPath
        }
    } else {
        Write-Host 'Inicio de sesión aplazado. Ejecuta Instalar.bat otra vez cuando quieras completarlo.' -ForegroundColor Yellow
    }
} else {
    Write-Host 'Sesión de Garmin encontrada.' -ForegroundColor Green
}

Write-Host ''
Write-Host 'Para utilizar el programa, haz doble clic en GarminLauncher.exe.' -ForegroundColor Cyan

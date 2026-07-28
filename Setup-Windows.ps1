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
        throw 'winget is required to install missing Windows prerequisites.'
    }
}

Write-Host ''
Write-Host 'Garmin Data Export - Windows setup' -ForegroundColor Cyan
Write-Host '==================================' -ForegroundColor Cyan
Write-Host ''

$runningLauncher = Get-Process GarminLauncher -ErrorAction SilentlyContinue
if ($runningLauncher) {
    throw 'GarminLauncher.exe is running. Close its window and rerun Setup-Windows.ps1.'
}

$pythonExe = Find-Python311
if (-not $pythonExe) {
    Require-Winget
    Write-Host 'Installing Python 3.11...' -ForegroundColor Yellow
    winget install `
        --id Python.Python.3.11 `
        --exact `
        --source winget `
        --accept-source-agreements `
        --accept-package-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "Python installation failed with exit code $LASTEXITCODE."
    }
    Refresh-ProcessPath
    $pythonExe = Find-Python311
    if (-not $pythonExe) {
        throw 'Python 3.11 was installed but could not be located. Restart PowerShell and rerun setup.'
    }
}
Write-Host "Python: $pythonExe" -ForegroundColor Green

$dotnetExe = Find-DotNet
$hasDotNet11 = $false
if ($dotnetExe) {
    $installedSdks = & $dotnetExe --list-sdks
    $hasDotNet11 = [bool]($installedSdks -match '^11\.')
}

if (-not $hasDotNet11) {
    Require-Winget
    Write-Host 'Installing .NET 11 SDK...' -ForegroundColor Yellow

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
        throw ".NET installation failed with exit code $LASTEXITCODE."
    }
    Refresh-ProcessPath
    $dotnetExe = Find-DotNet
    if (-not $dotnetExe) {
        throw '.NET was installed but could not be located. Restart PowerShell and rerun setup.'
    }
}
Write-Host ".NET: $dotnetExe" -ForegroundColor Green

$venvPython = Join-Path $projectRoot '.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $venvPython)) {
    Write-Host 'Creating .venv...' -ForegroundColor Yellow
    & $pythonExe -m venv (Join-Path $projectRoot '.venv')
    if ($LASTEXITCODE -ne 0) {
        throw "Virtual environment creation failed with exit code $LASTEXITCODE."
    }
}

Write-Host 'Installing tested Python dependencies...' -ForegroundColor Yellow
& $venvPython -m pip install -r (Join-Path $projectRoot 'requirements-windows-lock.txt')
if ($LASTEXITCODE -ne 0) {
    throw "Python dependency installation failed with exit code $LASTEXITCODE."
}
& $venvPython -m pip check
if ($LASTEXITCODE -ne 0) {
    throw 'Python dependency validation failed.'
}

Write-Host 'Building .NET projects...' -ForegroundColor Yellow
& $dotnetExe restore (Join-Path $projectRoot 'GarminDataExport.slnx')
if ($LASTEXITCODE -ne 0) {
    throw ".NET restore failed with exit code $LASTEXITCODE."
}
& $dotnetExe build (Join-Path $projectRoot 'GarminDataExport.slnx') --no-restore
if ($LASTEXITCODE -ne 0) {
    throw ".NET build failed with exit code $LASTEXITCODE."
}

$publishDirectory = Join-Path $projectRoot 'GarminDataExport.Launcher\bin\publish'
Write-Host 'Publishing GarminLauncher.exe...' -ForegroundColor Yellow
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
    throw ".NET publish failed with exit code $LASTEXITCODE."
}

$publishedExe = Join-Path $publishDirectory 'GarminLauncher.exe'
$rootExe = Join-Path $projectRoot 'GarminLauncher.exe'
Copy-Item -LiteralPath $publishedExe -Destination $rootExe -Force

Write-Host ''
Write-Host 'Setup completed successfully.' -ForegroundColor Green
Write-Host "Launcher: $rootExe"

$tokenDirectory = Join-Path $env:USERPROFILE '.garminconnect'
if (-not (Test-Path -LiteralPath $tokenDirectory)) {
    Write-Host ''
    Write-Host 'Before the first export, log in once:' -ForegroundColor Yellow
    Write-Host '  $env:PATH = "$PWD\.venv\Scripts;$env:PATH"'
    Write-Host '  dotnet run -- --login'
}

Write-Host ''
Write-Host 'Double-click GarminLauncher.exe to export or update your data.'

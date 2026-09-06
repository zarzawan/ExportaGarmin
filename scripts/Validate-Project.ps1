[CmdletBinding()]
param([string]$PythonPath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($PythonPath)) {
    $PythonPath = Join-Path $projectRoot '.venv\Scripts\python.exe'
    if (-not (Test-Path -LiteralPath $PythonPath)) {
        $PythonPath = (Get-Command python -ErrorAction Stop).Source
    }
}
function Invoke-Validation {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Ha fallado la validación: $Executable $($Arguments -join ' ')"
    }
}
Push-Location -LiteralPath $projectRoot
try {
    $modules = @(Get-Content -LiteralPath 'backend-modules.txt' -Encoding UTF8)
    Invoke-Validation $PythonPath (@('-m', 'py_compile') + $modules)
    Invoke-Validation 'dotnet' @('restore', 'GarminDataExport.slnx')
    Invoke-Validation 'dotnet' @('build', 'GarminDataExport.slnx', '--no-restore')
    Invoke-Validation 'dotnet' @('run', '--project', 'GarminDataExport.Tests', '--no-build')
    Invoke-Validation $PythonPath @('-m', 'unittest', 'discover', '-s', 'tests', '-v')
    Invoke-Validation 'dotnet' @('run', '--project', 'GarminDataExport.csproj', '--no-build', '--', '--help')
    Invoke-Validation $PythonPath @('-m', 'pip', 'check')
    Invoke-Validation 'git' @('diff', '--check')
} finally {
    Pop-Location
}

[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot 'docs\images'
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$project = Join-Path $projectRoot `
    'GarminDataExport.Launcher\GarminDataExport.Launcher.csproj'

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class ReadmeWindowCapture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(
        IntPtr window,
        IntPtr deviceContext,
        uint flags);
}
'@

function Save-Preview {
    param(
        [Parameter(Mandatory = $true)][string]$Mode,
        [Parameter(Mandatory = $true)][string]$FileName
    )

    $executable = Join-Path $projectRoot (
        'GarminDataExport.Launcher\bin\Release\net10.0-windows\win-x64\' +
        'ExportaGarmin.exe')
    $process = Start-Process `
        -FilePath $executable `
        -ArgumentList @('--readme-preview', $Mode) `
        -PassThru
    try {
        $null = $process.WaitForInputIdle(10000)
        for ($attempt = 0; $attempt -lt 100; $attempt++) {
            $process.Refresh()
            if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
                break
            }
            Start-Sleep -Milliseconds 100
        }
        if ($process.MainWindowHandle -eq [IntPtr]::Zero) {
            throw "No se encontró la ventana de demostración «$Mode»."
        }
        Start-Sleep -Milliseconds 500

        $rect = New-Object ReadmeWindowCapture+Rect
        if (-not [ReadmeWindowCapture]::GetWindowRect(
                $process.MainWindowHandle,
                [ref]$rect)) {
            throw "No se pudo medir la ventana de demostración «$Mode»."
        }
        $width = $rect.Right - $rect.Left
        $height = $rect.Bottom - $rect.Top
        if ($width -lt 900 -or $height -lt 700) {
            throw "La ventana de demostración tiene un tamaño inesperado."
        }

        $bitmap = New-Object System.Drawing.Bitmap $width, $height
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $deviceContext = $graphics.GetHdc()
            try {
                if (-not [ReadmeWindowCapture]::PrintWindow(
                        $process.MainWindowHandle,
                        $deviceContext,
                        2)) {
                    throw "Windows no pudo capturar la vista «$Mode»."
                }
            } finally {
                $graphics.ReleaseHdc($deviceContext)
            }
            $destination = Join-Path $outputRoot $FileName
            $bitmap.Save(
                $destination,
                [System.Drawing.Imaging.ImageFormat]::Png)
        } finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    } finally {
        if (-not $process.HasExited) {
            $null = $process.CloseMainWindow()
            if (-not $process.WaitForExit(3000)) {
                $process.Kill()
                $process.WaitForExit()
            }
        }
        $process.Dispose()
    }
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

& dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar ExportaGarmin. Código de error: $LASTEXITCODE."
}

Save-Preview `
    -Mode 'principal' `
    -FileName 'exportagarmin-principal.png'
Save-Preview `
    -Mode 'informe' `
    -FileName 'exportagarmin-informe.png'
Save-Preview `
    -Mode 'diario' `
    -FileName 'exportagarmin-diario.png'

Write-Host 'Capturas anónimas creadas:' -ForegroundColor Green
Write-Host (Join-Path $outputRoot 'exportagarmin-principal.png')
Write-Host (Join-Path $outputRoot 'exportagarmin-informe.png')
Write-Host (Join-Path $outputRoot 'exportagarmin-diario.png')

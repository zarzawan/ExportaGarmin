@echo off
chcp 65001 >nul
setlocal
title Instalación de EntrenaIA
cd /d "%~dp0"

echo.
echo ============================================================
echo   EntrenaIA - Exportador de Garmin para IA
echo ============================================================
echo.
echo No cierres esta ventana. La primera instalación puede tardar
echo varios minutos y Windows puede solicitar permisos.
echo.

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-Windows.ps1"
set "RESULTADO=%ERRORLEVEL%"

echo.
if not "%RESULTADO%"=="0" (
    echo ============================================================
    echo   La instalación no se ha completado
    echo ============================================================
    echo.
    echo Lee el mensaje de error que aparece más arriba.
    echo Puedes corregir el problema y ejecutar Instalar.bat otra vez.
) else (
    echo ============================================================
    echo   Instalación terminada
    echo ============================================================
    echo.
    echo Ya puedes cerrar esta ventana y hacer doble clic en:
    echo.
    echo   GarminLauncher.exe
)

echo.
pause
exit /b %RESULTADO%

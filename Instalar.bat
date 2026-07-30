@echo off
chcp 65001 >nul
setlocal
title Instalación de ExportaGarmin
cd /d "%~dp0"

echo.
echo ============================================================
echo   ExportaGarmin - Preparación desde el código fuente
echo ============================================================
echo.
echo Esta herramienta es para desarrolladores que han descargado el
echo código fuente. La versión normal de GitHub Releases ya está
echo compilada y no necesita este instalador.
echo.
echo No cierres esta ventana. Puede tardar varios minutos y Windows
echo puede solicitar permisos.
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
    echo   ExportaGarmin.exe
)

echo.
pause
exit /b %RESULTADO%

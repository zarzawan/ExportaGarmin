using System.Diagnostics;
using System.Text;
using GarminDataExport.Launcher.Models;

namespace GarminDataExport.Launcher.Services;

internal static class SessionLoginLauncher
{
    private static readonly object Sync = new();
    private static Process? _activeProcess;
    private static bool _starting;

    public static bool IsRunning
    {
        get
        {
            lock (Sync)
            {
                if (_starting)
                    return true;
                if (_activeProcess is null)
                    return false;
                try
                {
                    return !_activeProcess.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }
    }

    public static async Task<int> RunAsync(
        UserProfile profile,
        string projectRoot)
    {
        lock (Sync)
        {
            if (_starting || _activeProcess is not null)
            {
                throw new InvalidOperationException(
                    "Ya hay un inicio de sesión abierto. Termínalo o cierra su ventana antes de iniciar otro.");
            }
            _starting = true;
        }

        Process? process = null;
        try
        {
            var pythonPath = Path.Combine(
                projectRoot,
                ".venv",
                "Scripts",
                "python.exe");
            var scriptPath = Path.Combine(projectRoot, "garmin_export.py");
            if (!File.Exists(pythonPath) || !File.Exists(scriptPath))
                throw new FileNotFoundException(
                    "No se encontró la instalación de Python del programa.");

            var tokenStore = AppPaths.TokenStore(profile);
            if (profile.Id != UserProfile.LegacyId)
                Directory.CreateDirectory(tokenStore);

            var commandFile = AppPaths.LoginScriptFile(profile);
            Directory.CreateDirectory(Path.GetDirectoryName(commandFile)!);
            var lines = new[]
            {
                "@echo off",
                "chcp 65001 >nul",
                "title Inicio de sesión de Garmin",
                "echo.",
                "echo INICIO DE SESION DE GARMIN",
                "echo Escribe tus credenciales y el codigo MFA solo en esta ventana.",
                "echo El programa no guarda tu contrasena.",
                "echo.",
                "set \"GARMIN_EMAIL=\"",
                "set \"GARMIN_PASSWORD=\"",
                "set \"EMAIL=\"",
                "set \"PASSWORD=\"",
                "set \"GARMINTOKENS=\"",
                $"{Quote(pythonPath)} {Quote(scriptPath)} --login --force-login --ignore-credential-env --tokenstore {Quote(tokenStore)}",
                "set \"GARMIN_LOGIN_EXIT_CODE=%ERRORLEVEL%\"",
                "echo.",
                "if \"%GARMIN_LOGIN_EXIT_CODE%\"==\"0\" (echo Sesion preparada.) else (echo No se pudo iniciar la sesion. Revisa el mensaje anterior.)",
                "echo Esta ventana se cerrara automaticamente.",
                "timeout /t 4 /nobreak >nul",
                "exit /b %GARMIN_LOGIN_EXIT_CODE%",
            };
            File.WriteAllLines(
                commandFile,
                lines,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            process = Process.Start(new ProcessStartInfo
            {
                FileName = commandFile,
                WorkingDirectory = projectRoot,
                UseShellExecute = true,
            }) ?? throw new InvalidOperationException(
                "Windows no pudo abrir la ventana de inicio de sesión.");
            lock (Sync)
            {
                _activeProcess = process;
                _starting = false;
            }
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        finally
        {
            lock (Sync)
            {
                _starting = false;
                if (ReferenceEquals(_activeProcess, process))
                    _activeProcess = null;
            }
            process?.Dispose();
        }
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("%", "%%", StringComparison.Ordinal)
            .Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}

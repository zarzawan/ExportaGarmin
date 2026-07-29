using System.Diagnostics;
using GarminDataExport.Launcher.Models;

namespace GarminDataExport.Launcher.Services;

internal static class SessionValidator
{
    public static async Task<bool> ValidateAsync(
        UserProfile profile,
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        if (!AppPaths.HasSession(profile))
            return false;

        var pythonPath = Path.Combine(
            projectRoot,
            ".venv",
            "Scripts",
            "python.exe");
        var scriptPath = Path.Combine(projectRoot, "garmin_export.py");
        if (!File.Exists(pythonPath) || !File.Exists(scriptPath))
            return false;

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            WorkingDirectory = projectRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var variable in new[]
                 {
                     "GARMIN_EMAIL",
                     "GARMIN_PASSWORD",
                     "EMAIL",
                     "PASSWORD",
                     "GARMINTOKENS",
                 })
        {
            startInfo.Environment.Remove(variable);
        }
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("--check-session");
        startInfo.ArgumentList.Add("--ignore-credential-env");
        startInfo.ArgumentList.Add("--non-interactive-auth");
        startInfo.ArgumentList.Add("--tokenstore");
        startInfo.ArgumentList.Add(AppPaths.TokenStore(profile));

        Process? process = null;
        try
        {
            process = new Process { StartInfo = startInfo };
            if (!process.Start())
                return false;
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdout, stderr);
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        finally
        {
            TryStop(process);
            process?.Dispose();
        }
    }

    private static void TryStop(Process? process)
    {
        if (process is null)
            return;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Ya había terminado o no llegó a iniciarse.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // La comprobación ya devolverá false; no se muestran datos técnicos.
        }
    }
}

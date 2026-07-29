using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GarminDataExport.Launcher.Services;

internal sealed class BackendCapabilities
{
    private readonly HashSet<string> _options;

    private BackendCapabilities(IEnumerable<string> options, bool isReady)
    {
        _options = new HashSet<string>(options, StringComparer.Ordinal);
        IsReady = isReady;
    }

    public bool IsReady { get; }
    public bool Supports(string option) => _options.Contains(option);

    public static async Task<BackendCapabilities> DetectAsync(
        string pythonPath,
        string scriptPath,
        CancellationToken cancellationToken = default)
    {
        var required = new[]
        {
            "--start-date", "--end-date", "--output", "--filename", "--tokenstore",
            "--compact", "--activity-details", "--days", "--login", "--force-login",
            "--check-session",
            "--report", "--format", "--cache-dir", "--manifest", "--run-id",
            "--ignore-credential-env", "--non-interactive-auth", "--list-activities",
            "--activity-id", "--race-context", "--journal", "--review-weeks",
        };

        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
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
            startInfo.ArgumentList.Add("--help");

            process = new Process { StartInfo = startInfo };
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var help = (await stdout) + Environment.NewLine + (await stderr);
            if (process.ExitCode != 0)
                return new BackendCapabilities([], false);
            var options = Regex.Matches(help, @"--[a-z][a-z0-9-]*")
                .Select(match => match.Value)
                .ToHashSet(StringComparer.Ordinal);
            return new BackendCapabilities(
                options,
                required.All(options.Contains));
        }
        catch
        {
            return new BackendCapabilities([], false);
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
        catch
        {
            // La detección simplemente se marcará como no disponible.
        }
    }
}

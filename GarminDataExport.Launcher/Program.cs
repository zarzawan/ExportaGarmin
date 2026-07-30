using System.Diagnostics;
using System.Text;
using GarminDataExport.Launcher.Services;

namespace GarminDataExport.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--diagnose", StringComparer.Ordinal))
            return DiagnosePortableInstallation();

        ApplicationConfiguration.Initialize();
        var previewIndex = Array.IndexOf(args, "--readme-preview");
        if (previewIndex >= 0)
        {
            var mode = previewIndex + 1 < args.Length
                ? args[previewIndex + 1]
                : "principal";
            if (mode is not ("principal" or "informe"))
                return 4;
            Application.Run(new MainForm(mode));
            return 0;
        }

        using var singleInstance = new Mutex(
            initiallyOwned: true,
            name: @"Local\GarminDataExportLauncher-v3",
            createdNew: out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "ExportaGarmin ya está abierto.\n\n" +
                "Utiliza la ventana existente para evitar que dos exportaciones se pisen.",
                "Programa ya abierto",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }
        Application.SetUnhandledExceptionMode(
            UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) =>
            ShowProtectedStorageError(args.Exception);
        try
        {
            Application.Run(new MainForm());
        }
        catch (InvalidDataException error)
        {
            ShowProtectedStorageError(error);
        }
        finally
        {
            singleInstance.ReleaseMutex();
        }
        return 0;
    }

    private static int DiagnosePortableInstallation()
    {
        var backend = BackendPaths.Find();
        if (backend is null)
            return 2;
        var startInfo = new ProcessStartInfo
        {
            FileName = backend.PythonPath,
            WorkingDirectory = backend.ApplicationRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };
        backend.ApplySafePythonEnvironment(startInfo.Environment);
        startInfo.ArgumentList.Add(backend.ScriptPath);
        startInfo.ArgumentList.Add("--help");
        using var process = Process.Start(startInfo);
        if (process is null)
            return 3;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);
        if (process.ExitCode != 0)
            return process.ExitCode;
        var output = stdout.GetAwaiter().GetResult();
        return output.Contains("Días de datos diarios", StringComparison.Ordinal) &&
               !output.Contains("DÃ", StringComparison.Ordinal)
            ? 0
            : 5;
    }

    private static void ShowProtectedStorageError(Exception error)
    {
        var message = error is InvalidDataException
            ? error.Message
            : "Se ha producido un error local y la operación se ha detenido para no perder datos.";
        MessageBox.Show(
            message + "\n\nNo borres los archivos .bak o .damaged. " +
            "Puedes pedir ayuda sin compartir su contenido.",
            "Protección de datos locales",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}

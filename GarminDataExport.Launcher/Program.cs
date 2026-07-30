using System.Diagnostics;
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
        using var singleInstance = new Mutex(
            initiallyOwned: true,
            name: @"Local\GarminDataExportLauncher-v3",
            createdNew: out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "EntrenaIA ya está abierto.\n\n" +
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
        return process.ExitCode;
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

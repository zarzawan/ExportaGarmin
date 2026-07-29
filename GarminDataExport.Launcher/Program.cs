namespace GarminDataExport.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
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
            return;
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

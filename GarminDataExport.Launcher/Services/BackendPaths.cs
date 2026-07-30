namespace GarminDataExport.Launcher.Services;

internal sealed class BackendPaths
{
    private BackendPaths(
        string applicationRoot,
        string pythonPath,
        string scriptPath,
        bool isPortable)
    {
        ApplicationRoot = applicationRoot;
        PythonPath = pythonPath;
        ScriptPath = scriptPath;
        IsPortable = isPortable;
    }

    public string ApplicationRoot { get; }
    public string PythonPath { get; }
    public string ScriptPath { get; }
    public bool IsPortable { get; }

    public static BackendPaths? TryResolve(string applicationRoot)
    {
        var root = Path.GetFullPath(applicationRoot);
        var portable = new BackendPaths(
            root,
            Path.Combine(root, "runtime", "python", "python.exe"),
            Path.Combine(root, "app", "garmin_export.pyc"),
            isPortable: true);
        if (portable.Exists())
            return portable;

        var development = new BackendPaths(
            root,
            Path.Combine(root, ".venv", "Scripts", "python.exe"),
            Path.Combine(root, "garmin_export.py"),
            isPortable: false);
        return development.Exists() ? development : null;
    }

    public static BackendPaths? Find()
    {
        var startingDirectories = new[]
        {
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
        };
        foreach (var startingDirectory in startingDirectories.Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            var directory = new DirectoryInfo(startingDirectory);
            for (var level = 0;
                 directory is not null && level < 8;
                 level++, directory = directory.Parent)
            {
                var resolved = TryResolve(directory.FullName);
                if (resolved is not null)
                    return resolved;
            }
        }
        return null;
    }

    public void ApplySafePythonEnvironment(
        IDictionary<string, string?> environment)
    {
        environment["PYTHONDONTWRITEBYTECODE"] = "1";
        environment["PYTHONNOUSERSITE"] = "1";
        environment["PYTHONUTF8"] = "1";
        foreach (var variable in new[]
                 {
                     "GARMIN_EMAIL",
                     "GARMIN_PASSWORD",
                     "EMAIL",
                     "PASSWORD",
                     "GARMINTOKENS",
                     "PYTHONHOME",
                     "PYTHONPATH",
                 })
        {
            environment.Remove(variable);
        }
    }

    private bool Exists() =>
        File.Exists(PythonPath) && File.Exists(ScriptPath);
}

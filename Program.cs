using System.Diagnostics;

var backend = FindBundledOrDevelopmentBackend();
var scriptPath = backend?.ScriptPath ??
                 Path.Combine(Directory.GetCurrentDirectory(), "garmin_export.py");
var pythonCommand = backend?.PythonPath;
if (pythonCommand is null)
    pythonCommand = await FindPythonAsync();
if (pythonCommand is null)
{
    Console.Error.WriteLine("Python 3.11 no está instalado o no funciona.");
    if (!await TryInstallPythonAsync())
    {
        Console.Error.WriteLine("No se pudo instalar Python automáticamente. Ejecuta Instalar.bat para preparar la aplicación.");
        Environment.Exit(1);
    }
    pythonCommand = await FindPythonAsync();
    if (pythonCommand is null)
    {
        Console.Error.WriteLine("Python se instaló, pero Windows todavía no lo encuentra. Reinicia el PC y ejecuta Instalar.bat.");
        Environment.Exit(1);
    }
}

var psi = new ProcessStartInfo
{
    FileName = pythonCommand,
    ArgumentList = { scriptPath },
    WorkingDirectory = backend?.ApplicationRoot ??
                       Directory.GetCurrentDirectory(),
    UseShellExecute = false
};
psi.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
psi.Environment["PYTHONNOUSERSITE"] = "1";
psi.Environment["PYTHONUTF8"] = "1";
psi.Environment.Remove("PYTHONHOME");
psi.Environment.Remove("PYTHONPATH");

foreach (var arg in args)
    psi.ArgumentList.Add(arg);

using var process = Process.Start(psi);
if (process is null)
{
    Console.Error.WriteLine("No se pudo iniciar Python.");
    Environment.Exit(1);
}

await process.WaitForExitAsync();
Environment.Exit(process.ExitCode);

static BackendLocation? FindBundledOrDevelopmentBackend()
{
    foreach (var startingDirectory in new[]
             {
                 AppContext.BaseDirectory,
                 Environment.CurrentDirectory,
             }.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        var directory = new DirectoryInfo(startingDirectory);
        for (var level = 0;
             directory is not null && level < 8;
             level++, directory = directory.Parent)
        {
            var portable = new BackendLocation(
                directory.FullName,
                Path.Combine(
                    directory.FullName,
                    "runtime",
                    "python",
                    "python.exe"),
                Path.Combine(
                    directory.FullName,
                    "app",
                    "garmin_export.pyc"));
            if (portable.Exists())
                return portable;

            var development = new BackendLocation(
                directory.FullName,
                Path.Combine(
                    directory.FullName,
                    ".venv",
                    "Scripts",
                    "python.exe"),
                Path.Combine(directory.FullName, "garmin_export.py"));
            if (development.Exists())
                return development;
        }
    }
    return null;
}

static async Task<string?> FindPythonAsync()
{
    // Probar primero "python" y después "python3".
    foreach (var candidate in new[] { "python", "python3" })
    {
        if (await IsPython311Async(candidate))
            return candidate;
    }

    // Comprobar ubicaciones habituales de Windows cuando PATH no está configurado.
    if (OperatingSystem.IsWindows())
    {
        var localApps = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates = new List<string>();

        foreach (var baseDir in new[] { localApps, programFiles })
        {
            var pythonDir = Path.Combine(baseDir, "Programs", "Python");
            if (Directory.Exists(pythonDir))
            {
                foreach (var dir in Directory.GetDirectories(pythonDir).OrderDescending())
                {
                    var exe = Path.Combine(dir, "python.exe");
                    if (File.Exists(exe)) candidates.Add(exe);
                }
            }

            // winget puede instalar Python bajo Program Files\Python3xx.
            foreach (var dir in Directory.GetDirectories(baseDir, "Python3*").OrderDescending())
            {
                var exe = Path.Combine(dir, "python.exe");
                if (File.Exists(exe)) candidates.Add(exe);
            }
        }

        foreach (var exe in candidates)
        {
            if (await IsPython311Async(exe))
                return exe;
        }
    }

    return null;
}

static async Task<bool> IsPython311Async(string candidate)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = candidate,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(
            "import sys; raise SystemExit(0 if sys.version_info[:2] == (3, 11) else 1)");
        using var process = Process.Start(psi);
        if (process is null)
            return false;
        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}

static async Task<bool> TryInstallPythonAsync()
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("La instalación automática solo está disponible en Windows mediante winget.");
        return false;
    }

    // Comprobar si winget está disponible.
    try
    {
        var check = new ProcessStartInfo
        {
            FileName = "winget",
            ArgumentList = { "--version" },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var checkProc = Process.Start(check);
        if (checkProc is null) return false;
        await checkProc.WaitForExitAsync();
        if (checkProc.ExitCode != 0) return false;
    }
    catch
    {
        Console.Error.WriteLine("winget no está disponible. Ejecuta Instalar.bat o instala Python 3.11 manualmente.");
        return false;
    }

    Console.Write("¿Quieres instalar Python 3.11 mediante winget? [S/n] ");
    var response = Console.ReadLine()?.Trim();
    if (response is not null && response.Length > 0
        && !response.Equals("s", StringComparison.OrdinalIgnoreCase)
        && !response.Equals("si", StringComparison.OrdinalIgnoreCase)
        && !response.Equals("sí", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    Console.WriteLine("Instalando Python mediante winget...");
    var psi = new ProcessStartInfo
    {
        FileName = "winget",
        ArgumentList = { "install", "Python.Python.3.11", "--accept-source-agreements", "--accept-package-agreements" },
        UseShellExecute = false
    };
    using var proc = Process.Start(psi);
    if (proc is null) return false;
    await proc.WaitForExitAsync();

    if (proc.ExitCode != 0)
    {
        Console.Error.WriteLine("La instalación mediante winget ha fallado.");
        return false;
    }

    Console.WriteLine("Python se ha instalado correctamente.");

    // Actualizar PATH desde el registro para encontrar Python sin reiniciar.
    RefreshPath();
    return true;
}

static void RefreshPath()
{
    if (!OperatingSystem.IsWindows()) return;

    var machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "";
    var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
    var combined = $"{userPath};{machinePath}";
    Environment.SetEnvironmentVariable("PATH", combined);
}

internal sealed record BackendLocation(
    string ApplicationRoot,
    string PythonPath,
    string ScriptPath)
{
    public bool Exists() =>
        File.Exists(PythonPath) && File.Exists(ScriptPath);
}

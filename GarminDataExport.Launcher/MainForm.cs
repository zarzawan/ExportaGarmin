using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace GarminDataExport.Launcher;

internal sealed class MainForm : Form
{
    private static readonly (string English, string Spanish)[] SectionTranslations =
    [
        ("Profile", "Perfil"),
        ("Daily Health", "Salud diaria"),
        ("Activities", "Actividades"),
        ("Body Composition", "Composición corporal"),
        ("Training Metrics", "Métricas de entrenamiento"),
        ("Goals and Records", "Objetivos y récords"),
        ("Trends", "Tendencias"),
        ("Gear", "Equipamiento"),
        ("Training Plans", "Planes de entrenamiento"),
        ("Workouts", "Entrenamientos"),
        ("Hydration", "Hidratación"),
        ("Nutrition", "Nutrición"),
        ("Women's Health", "Salud femenina"),
    ];

    private readonly DateTimePicker _startDatePicker = new();
    private readonly Button _runButton = new();
    private readonly Button _openFolderButton = new();
    private readonly Button _openFileButton = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Label _statusLabel = new();
    private readonly Label _outputLabel = new();
    private readonly TextBox _logBox = new();

    private string? _projectRoot;
    private string? _outputDirectory;
    private string? _currentExportPath;

    public MainForm()
    {
        Text = "Exportador de datos de Garmin";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 540);
        Size = new Size(840, 620);
        Font = new Font("Segoe UI", 10F);

        BuildInterface();
        LoadLauncherSettings();
        LocateProject();
    }

    private void BuildInterface()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 7,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        Controls.Add(layout);

        var title = new Label
        {
            AutoSize = true,
            Text = "Garmin Connect — exportación incremental",
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8),
        };
        layout.Controls.Add(title);

        var explanation = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Text = "Selecciona la primera fecha que quieres conservar. La primera ejecución puede tardar; " +
                   "las siguientes reutilizan la caché y actualizan un único archivo.",
            Margin = new Padding(0, 0, 0, 16),
        };
        layout.Controls.Add(explanation);

        var datePanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 12),
        };
        datePanel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Descargar desde:",
            Margin = new Padding(0, 7, 10, 0),
        });
        _startDatePicker.Format = DateTimePickerFormat.Short;
        _startDatePicker.MinDate = new DateTime(2000, 1, 1);
        _startDatePicker.MaxDate = DateTime.Today;
        _startDatePicker.Width = 140;
        datePanel.Controls.Add(_startDatePicker);
        layout.Controls.Add(datePanel);

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 12),
        };

        _runButton.Text = "Crear o actualizar";
        _runButton.AutoSize = true;
        _runButton.Padding = new Padding(12, 5, 12, 5);
        _runButton.Click += RunButton_Click;
        buttonPanel.Controls.Add(_runButton);

        _openFolderButton.Text = "Abrir carpeta";
        _openFolderButton.AutoSize = true;
        _openFolderButton.Padding = new Padding(8, 5, 8, 5);
        _openFolderButton.Click += (_, _) => OpenOutputDirectory();
        buttonPanel.Controls.Add(_openFolderButton);

        _openFileButton.Text = "Abrir archivo";
        _openFileButton.AutoSize = true;
        _openFileButton.Padding = new Padding(8, 5, 8, 5);
        _openFileButton.Click += (_, _) => OpenCurrentExport();
        buttonPanel.Controls.Add(_openFileButton);

        layout.Controls.Add(buttonPanel);

        _progressBar.Dock = DockStyle.Top;
        _progressBar.Height = 8;
        _progressBar.Style = ProgressBarStyle.Blocks;
        _progressBar.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(_progressBar);

        var statusPanel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 8),
        };
        _statusLabel.AutoSize = true;
        _statusLabel.Text = "Preparando…";
        _statusLabel.Font = new Font(Font, FontStyle.Bold);
        statusPanel.Controls.Add(_statusLabel);

        _outputLabel.AutoSize = true;
        _outputLabel.ForeColor = SystemColors.GrayText;
        _outputLabel.MaximumSize = new Size(760, 0);
        statusPanel.Controls.Add(_outputLabel);
        layout.Controls.Add(statusPanel);

        _logBox.Dock = DockStyle.Fill;
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.Font = new Font("Consolas", 9F);
        _logBox.BackColor = Color.FromArgb(248, 249, 250);
        layout.Controls.Add(_logBox);
    }

    private async void RunButton_Click(object? sender, EventArgs e)
    {
        if (_projectRoot is null || _outputDirectory is null)
        {
            ShowError("No se encontró la instalación completa junto al lanzador. Ejecuta Instalar.bat para repararla.");
            return;
        }

        var tokenDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".garminconnect");
        if (!Directory.Exists(tokenDirectory) ||
            !Directory.EnumerateFiles(tokenDirectory).Any())
        {
            ShowError("No hay una sesión de Garmin guardada. Cierra esta ventana y ejecuta Instalar.bat para iniciar sesión.");
            return;
        }

        SaveLauncherSettings();
        SetRunningState(true);
        _logBox.Clear();
        AppendLog($"Fecha inicial: {_startDatePicker.Value:yyyy-MM-dd}");
        AppendLog("La caché interna se reutilizará para evitar descargas repetidas.");

        try
        {
            Directory.CreateDirectory(_outputDirectory);

            var pythonPath = Path.Combine(_projectRoot, ".venv", "Scripts", "python.exe");
            var scriptPath = Path.Combine(_projectRoot, "garmin_export.py");
            var startedAtUtc = DateTime.UtcNow;

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                WorkingDirectory = _projectRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.Environment["PYTHONUNBUFFERED"] = "1";
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add("--start-date");
            startInfo.ArgumentList.Add(_startDatePicker.Value.ToString(
                "yyyy-MM-dd", CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--compact");
            startInfo.ArgumentList.Add("--output");
            startInfo.ArgumentList.Add(_outputDirectory);

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                throw new InvalidOperationException("No se pudo iniciar el exportador.");

            var stdoutTask = PumpOutputAsync(process.StandardOutput);
            var stderrTask = PumpOutputAsync(process.StandardError);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdoutTask, stderrTask);

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"El exportador terminó con el código {process.ExitCode}. Revisa el registro.");

            var newExport = Directory
                .EnumerateFiles(_outputDirectory, "garmin_export_*.txt")
                .Where(path => File.GetLastWriteTimeUtc(path) >= startedAtUtc.AddSeconds(-5))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (newExport is null)
                throw new FileNotFoundException("La exportación terminó, pero no se encontró el archivo generado.");

            ReplaceCurrentExport(newExport);
            CleanupTimestampedExports();
            RefreshOutputState();

            _statusLabel.Text = "Actualización completada";
            AppendLog($"Archivo actual: {_currentExportPath}");
            MessageBox.Show(
                this,
                "La exportación se actualizó correctamente.",
                "Exportador de datos de Garmin",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "La actualización no se completó";
            AppendLog($"ERROR: {ex.Message}");
            ShowError(ex.Message);
        }
        finally
        {
            SetRunningState(false);
        }
    }

    private async Task PumpOutputAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
            AppendLog(TranslateVisibleLogLine(line));
    }

    private static string TranslateVisibleLogLine(string line)
    {
        foreach (var (english, spanish) in SectionTranslations)
            line = line.Replace(english, spanish, StringComparison.Ordinal);
        return line;
    }

    private void AppendLog(string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(line));
            return;
        }

        _logBox.AppendText(line + Environment.NewLine);
    }

    private void ReplaceCurrentExport(string generatedExport)
    {
        if (_outputDirectory is null)
            throw new InvalidOperationException("No se encontró la carpeta de salida.");

        var replacementPath = Path.Combine(_outputDirectory, "garmin_actual.new");
        var currentPath = Path.Combine(_outputDirectory, "garmin_actual.txt");

        File.Move(generatedExport, replacementPath, true);
        File.Move(replacementPath, currentPath, true);
    }

    private void CleanupTimestampedExports()
    {
        if (_outputDirectory is null)
            return;

        foreach (var path in Directory.EnumerateFiles(_outputDirectory, "garmin_export_*.txt"))
            File.Delete(path);
    }

    private void LocateProject()
    {
        _projectRoot = FindProjectRoot();
        if (_projectRoot is null)
        {
            _statusLabel.Text = "No se encontró la instalación";
            _outputLabel.Text = "Coloca GarminLauncher.exe dentro de la carpeta del proyecto.";
            _runButton.Enabled = false;
            _openFolderButton.Enabled = false;
            _openFileButton.Enabled = false;
            return;
        }

        _outputDirectory = Path.Combine(_projectRoot, "export", "managed");
        RefreshOutputState();
    }

    private void RefreshOutputState()
    {
        if (_outputDirectory is null)
            return;

        _currentExportPath = Path.Combine(_outputDirectory, "garmin_actual.txt");
        _outputLabel.Text = $"Archivo único: {_currentExportPath}";
        _openFolderButton.Enabled = true;
        _openFileButton.Enabled = File.Exists(_currentExportPath);
        if (_statusLabel.Text is "Preparando…")
        {
            _statusLabel.Text = File.Exists(_currentExportPath)
                ? "Listo para actualizar"
                : "Listo para la primera exportación";
        }
    }

    private static string? FindProjectRoot()
    {
        var startingDirectories = new[]
        {
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
        };

        foreach (var startingDirectory in startingDirectories.Distinct())
        {
            var directory = new DirectoryInfo(startingDirectory);
            for (var level = 0; directory is not null && level < 8; level++, directory = directory.Parent)
            {
                var scriptPath = Path.Combine(directory.FullName, "garmin_export.py");
                var pythonPath = Path.Combine(directory.FullName, ".venv", "Scripts", "python.exe");
                if (File.Exists(scriptPath) && File.Exists(pythonPath))
                    return directory.FullName;
            }
        }

        return null;
    }

    private void OpenOutputDirectory()
    {
        if (_outputDirectory is null)
            return;

        Directory.CreateDirectory(_outputDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { _outputDirectory },
            UseShellExecute = true,
        });
    }

    private void OpenCurrentExport()
    {
        if (_currentExportPath is null || !File.Exists(_currentExportPath))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = _currentExportPath,
            UseShellExecute = true,
        });
    }

    private void SetRunningState(bool isRunning)
    {
        _runButton.Enabled = !isRunning;
        _startDatePicker.Enabled = !isRunning;
        _openFolderButton.Enabled = !isRunning && _outputDirectory is not null;
        _openFileButton.Enabled = !isRunning &&
                                  _currentExportPath is not null &&
                                  File.Exists(_currentExportPath);
        _progressBar.Style = isRunning ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
        _progressBar.MarqueeAnimationSpeed = isRunning ? 30 : 0;
        if (isRunning)
            _statusLabel.Text = "Exportando… no cierres esta ventana";
    }

    private static string LauncherSettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GarminDataExportLauncher",
            "settings.json");

    private void LoadLauncherSettings()
    {
        var defaultDate = DateTime.Today.AddDays(-30);
        try
        {
            if (File.Exists(LauncherSettingsPath))
            {
                var settings = JsonSerializer.Deserialize<LauncherSettings>(
                    File.ReadAllText(LauncherSettingsPath));
                if (settings is not null)
                    defaultDate = settings.StartDate.Date;
            }
        }
        catch
        {
            // Un archivo de preferencias dañado no debe impedir que se abra el lanzador.
        }

        if (defaultDate < _startDatePicker.MinDate)
            defaultDate = _startDatePicker.MinDate;
        if (defaultDate > _startDatePicker.MaxDate)
            defaultDate = _startDatePicker.MaxDate;
        _startDatePicker.Value = defaultDate;
    }

    private void SaveLauncherSettings()
    {
        var settingsDirectory = Path.GetDirectoryName(LauncherSettingsPath)!;
        Directory.CreateDirectory(settingsDirectory);
        var settings = new LauncherSettings(_startDatePicker.Value.Date);
        File.WriteAllText(
            LauncherSettingsPath,
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ShowError(string message)
    {
        MessageBox.Show(
            this,
            message,
            "Exportador de datos de Garmin",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private sealed record LauncherSettings(DateTime StartDate);
}

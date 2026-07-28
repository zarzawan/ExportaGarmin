using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace GarminDataExport.Launcher;

internal sealed class MainForm : Form
{
    private static readonly (string Original, string Traduccion)[] SectionTranslations =
    [
        ("Export Metadata", "Metadatos de la exportación"),
        ("Profile", "Perfil"),
        ("Daily Health", "Salud diaria"),
        ("Blood Pressure", "Presión arterial"),
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
        ("Weekly Summary", "Resumen semanal"),
        ("Data Quality", "Calidad de los datos"),
        ("Women's Health", "Salud femenina"),
    ];

    private readonly DateTimePicker _startDatePicker = new();
    private readonly DateTimePicker _endDatePicker = new();
    private readonly TextBox _fileNameTextBox = new();
    private readonly CheckBox _includeActivityDetailsCheckBox = new();
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
        MinimumSize = new Size(800, 620);
        Size = new Size(900, 700);
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
            RowCount = 9,
        };
        for (var row = 0; row < 8; row++)
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
            MaximumSize = new Size(820, 0),
            Text = "Selecciona un intervalo. La caché evita repetir descargas. El archivo compacto " +
                   "está preparado para analizar el entrenamiento con una IA.",
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
        _startDatePicker.ValueChanged += DatePicker_ValueChanged;
        datePanel.Controls.Add(_startDatePicker);
        datePanel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Hasta:",
            Margin = new Padding(24, 7, 10, 0),
        });
        _endDatePicker.Format = DateTimePickerFormat.Short;
        _endDatePicker.MinDate = new DateTime(2000, 1, 1);
        _endDatePicker.MaxDate = DateTime.Today;
        _endDatePicker.Width = 140;
        _endDatePicker.ValueChanged += DatePicker_ValueChanged;
        datePanel.Controls.Add(_endDatePicker);
        layout.Controls.Add(datePanel);

        var filePanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8),
        };
        filePanel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Nombre del archivo:",
            Margin = new Padding(0, 7, 10, 0),
        });
        _fileNameTextBox.Width = 470;
        _fileNameTextBox.TextChanged += (_, _) => RefreshOutputState();
        filePanel.Controls.Add(_fileNameTextBox);
        layout.Controls.Add(filePanel);

        _includeActivityDetailsCheckBox.AutoSize = true;
        _includeActivityDetailsCheckBox.Text =
            "Incluir el máximo detalle temporal de las actividades (archivos mucho mayores)";
        _includeActivityDetailsCheckBox.Margin = new Padding(0, 0, 0, 12);
        layout.Controls.Add(_includeActivityDetailsCheckBox);

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 12),
        };

        _runButton.Text = "Crear exportación";
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

        if (_startDatePicker.Value.Date > _endDatePicker.Value.Date)
        {
            ShowError("La fecha inicial no puede ser posterior a la fecha final.");
            return;
        }

        string outputFileName;
        try
        {
            outputFileName = NormaliseFileName(_fileNameTextBox.Text);
            _fileNameTextBox.Text = outputFileName;
        }
        catch (ArgumentException ex)
        {
            ShowError(ex.Message);
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
        AppendLog($"Fecha final: {_endDatePicker.Value:yyyy-MM-dd}");
        AppendLog($"Archivo: {outputFileName}");
        AppendLog(_includeActivityDetailsCheckBox.Checked
            ? "Detalle temporal de actividades: incluido."
            : "Detalle temporal de actividades: omitido para reducir el tamaño.");
        AppendLog("La caché interna se reutilizará para evitar descargas repetidas.");

        try
        {
            Directory.CreateDirectory(_outputDirectory);

            var pythonPath = Path.Combine(_projectRoot, ".venv", "Scripts", "python.exe");
            var scriptPath = Path.Combine(_projectRoot, "garmin_export.py");
            _currentExportPath = Path.Combine(_outputDirectory, outputFileName);

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
            startInfo.ArgumentList.Add("--end-date");
            startInfo.ArgumentList.Add(_endDatePicker.Value.ToString(
                "yyyy-MM-dd", CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--compact");
            if (_includeActivityDetailsCheckBox.Checked)
                startInfo.ArgumentList.Add("--activity-details");
            startInfo.ArgumentList.Add("--output");
            startInfo.ArgumentList.Add(_outputDirectory);
            startInfo.ArgumentList.Add("--filename");
            startInfo.ArgumentList.Add(outputFileName);

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

            if (!File.Exists(_currentExportPath))
                throw new FileNotFoundException("La exportación terminó, pero no se encontró el archivo generado.");

            RefreshOutputState();

            _statusLabel.Text = "Exportación completada";
            AppendLog($"Archivo creado: {_currentExportPath}");
            MessageBox.Show(
                this,
                $"La exportación se guardó correctamente como:\n{outputFileName}",
                "Exportador de datos de Garmin",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "La exportación no se completó";
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
        foreach (var (original, traduccion) in SectionTranslations)
            line = line.Replace(original, traduccion, StringComparison.Ordinal);
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

        var fileName = string.IsNullOrWhiteSpace(_fileNameTextBox.Text)
            ? BuildDefaultFileName()
            : _fileNameTextBox.Text.Trim();
        _currentExportPath = Path.Combine(_outputDirectory, fileName);
        _outputLabel.Text = $"Se guardará en: {_currentExportPath}";
        _openFolderButton.Enabled = true;
        _openFileButton.Enabled = File.Exists(_currentExportPath);
        if (_statusLabel.Text is "Preparando…")
        {
            _statusLabel.Text = File.Exists(_currentExportPath)
                ? "Listo para sustituir ese archivo"
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
        _endDatePicker.Enabled = !isRunning;
        _fileNameTextBox.Enabled = !isRunning;
        _includeActivityDetailsCheckBox.Enabled = !isRunning;
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
        var defaultStartDate = DateTime.Today.AddDays(-30);
        var defaultEndDate = DateTime.Today;
        var includeActivityDetails = false;
        string? savedFileName = null;
        try
        {
            if (File.Exists(LauncherSettingsPath))
            {
                var settings = JsonSerializer.Deserialize<LauncherSettings>(
                    File.ReadAllText(LauncherSettingsPath));
                if (settings is not null)
                {
                    defaultStartDate = settings.StartDate.Date;
                    if (settings.EndDate.Year >= 2000)
                        defaultEndDate = settings.EndDate.Date;
                    includeActivityDetails = settings.IncludeActivityDetails;
                    savedFileName = settings.FileName;
                }
            }
        }
        catch
        {
            // Un archivo de preferencias dañado no debe impedir que se abra el lanzador.
        }

        defaultStartDate = ClampDate(defaultStartDate, _startDatePicker);
        defaultEndDate = ClampDate(defaultEndDate, _endDatePicker);
        if (defaultStartDate > defaultEndDate)
            defaultStartDate = defaultEndDate;

        _startDatePicker.Value = defaultStartDate;
        _endDatePicker.Value = defaultEndDate;
        _includeActivityDetailsCheckBox.Checked = includeActivityDetails;
        _fileNameTextBox.Text = string.IsNullOrWhiteSpace(savedFileName)
            ? BuildDefaultFileName()
            : savedFileName;
    }

    private void SaveLauncherSettings()
    {
        var settingsDirectory = Path.GetDirectoryName(LauncherSettingsPath)!;
        Directory.CreateDirectory(settingsDirectory);
        var settings = new LauncherSettings(
            _startDatePicker.Value.Date,
            _endDatePicker.Value.Date,
            _includeActivityDetailsCheckBox.Checked,
            NormaliseFileName(_fileNameTextBox.Text));
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

    private void DatePicker_ValueChanged(object? sender, EventArgs e)
    {
        if (_startDatePicker.Value.Date > _endDatePicker.Value.Date)
        {
            if (ReferenceEquals(sender, _startDatePicker))
                _endDatePicker.Value = _startDatePicker.Value.Date;
            else
                _startDatePicker.Value = _endDatePicker.Value.Date;
        }

        _fileNameTextBox.Text = BuildDefaultFileName();
    }

    private string BuildDefaultFileName() =>
        $"garmin_datos_{_startDatePicker.Value:yyyy-MM-dd}_a_{_endDatePicker.Value:yyyy-MM-dd}.txt";

    private static DateTime ClampDate(DateTime value, DateTimePicker picker)
    {
        if (value < picker.MinDate)
            return picker.MinDate;
        if (value > picker.MaxDate)
            return picker.MaxDate;
        return value;
    }

    private static string NormaliseFileName(string value)
    {
        var name = value.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Escribe un nombre para el archivo.");
        if (!string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                "El nombre no puede incluir carpetas ni caracteres no permitidos por Windows.");
        }
        if (!name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            name += ".txt";
        return name;
    }

    private sealed record LauncherSettings(
        DateTime StartDate,
        DateTime EndDate,
        bool IncludeActivityDetails,
        string? FileName);
}

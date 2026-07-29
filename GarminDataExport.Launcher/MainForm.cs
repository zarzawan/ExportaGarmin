using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GarminDataExport.Launcher.Models;
using GarminDataExport.Launcher.Services;
using GarminDataExport.Launcher.Views;

namespace GarminDataExport.Launcher;

internal sealed class MainForm : Form
{
    private static readonly (string Original, string Translation)[] SectionTranslations =
    [
        ("Export Metadata", "Metadatos de la exportación"),
        ("Race Context", "Contexto de carrera"),
        ("Profile", "Perfil"),
        ("Daily Health", "Salud diaria"),
        ("Blood Pressure", "Presión arterial"),
        ("Activities", "Actividades"),
        ("Body Composition", "Composición corporal"),
        ("Training Metrics", "Métricas de entrenamiento"),
        ("Goals and Records", "Objetivos y récords"),
        ("Gear", "Equipamiento"),
        ("Hydration", "Hidratación"),
        ("Nutrition", "Nutrición"),
        ("Period Summary", "Resumen del intervalo"),
        ("Weekly Timeline", "Evolución semanal"),
        ("Weekly Summary", "Resumen semanal"),
        ("Race Preparation", "Preparación de carrera"),
        ("Journal", "Diario"),
        ("Race Analysis", "Análisis de carrera"),
        ("Suggested Prompts", "Preguntas sugeridas"),
        ("Data Quality", "Calidad de los datos"),
    ];

    private readonly ProfileStore _profileStore = new();
    private LauncherSettings _settings = new();
    private BackendCapabilities? _capabilities;
    private UserProfile? _activeProfile;

    private readonly ComboBox _profileCombo = new();
    private readonly Label _sessionStatus = new();
    private readonly Label _raceSummary = new();
    private Control? _contextBar;
    private readonly TabControl _flowTabs = new();
    private readonly NumericUpDown _reviewWeeks = new();
    private readonly Label _reviewPeriod = new();
    private readonly TextBox _activityId = new();
    private readonly Label _selectedActivity = new();
    private string? _selectedActivityId;
    private DateTime? _selectedActivityDate;
    private readonly DateTimePicker _startDate = new();
    private readonly DateTimePicker _endDate = new();
    private readonly CheckBox _historyActivityDetails = new();
    private readonly ComboBox _formatCombo = new();
    private readonly Label _formatNotice = new();
    private readonly Label _outputPreview = new();
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _runButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _openFolderButton = new();
    private readonly Button _openFileButton = new();
    private readonly CheckBox _showLog = new();
    private readonly GroupBox _logGroup = new();
    private readonly TextBox _logBox = new();
    private RowStyle? _logRowStyle;

    private string? _projectRoot;
    private Process? _currentProcess;
    private bool _cancelRequested;
    private string? _verifiedSessionProfileId;
    private List<string> _lastOutputFiles = [];

    public MainForm()
    {
        Text = "EntrenaIA — Exportador de Garmin para IA";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(920, 720);
        Size = new Size(1030, 810);
        Font = new Font("Segoe UI", 10F);

        LoadSettings();
        BuildInterface();
        LocateProject();
        ApplySettingsToControls();
        PopulateProfiles();
        FormClosing += MainForm_FormClosing;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ShowStorageRecoveryNotices();
        await DetectBackendAsync();
        if (_capabilities?.IsReady != true)
            return;

        if (!_settings.FirstRunCompleted)
        {
            using var wizard = new FirstRunWizardForm(
                _profileStore,
                _projectRoot,
                _activeProfile);
            if (wizard.ShowDialog(this) == DialogResult.OK)
            {
                _settings.FirstRunCompleted = true;
                if (wizard.SelectedProfile is not null)
                    SelectProfile(wizard.SelectedProfile);
                ApplyRecommendedReviewWindow();
                SaveSettings();
            }
            else
            {
                _status.Text = "Puedes consultar la ayuda cuando quieras desde «Guía sencilla».";
            }
        }
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 8,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _logRowStyle = new RowStyle(SizeType.Absolute, 0);
        root.RowStyles.Add(_logRowStyle);
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);
        _contextBar = BuildContextBar();
        root.Controls.Add(_contextBar, 0, 1);
        root.Controls.Add(BuildFlowTabs(), 0, 2);
        root.Controls.Add(BuildFormatBar(), 0, 3);
        root.Controls.Add(BuildOutputStatus(), 0, 4);
        root.Controls.Add(BuildActionBar(), 0, 5);

        _progress.Dock = DockStyle.Top;
        _progress.Height = 7;
        _progress.Style = ProgressBarStyle.Blocks;
        _progress.Margin = new Padding(0, 5, 0, 6);
        root.Controls.Add(_progress, 0, 6);

        _logGroup.Text = "Detalles técnicos (revísalos antes de compartirlos)";
        _logGroup.Dock = DockStyle.Fill;
        _logGroup.Padding = new Padding(10);
        _logGroup.Visible = false;
        _logBox.Dock = DockStyle.Fill;
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.Font = new Font("Consolas", 9F);
        _logBox.BackColor = Color.FromArgb(248, 249, 250);
        _logGroup.Controls.Add(_logBox);
        root.Controls.Add(_logGroup, 0, 7);
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 8),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var titles = new Panel { Dock = DockStyle.Fill, Height = 70 };
        titles.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "EntrenaIA",
            Font = new Font("Segoe UI", 19F, FontStyle.Bold),
            Location = new Point(0, 0),
        });
        titles.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Exportador no oficial y no afiliado a Garmin: prepara tus datos para una IA.",
            ForeColor = SystemColors.GrayText,
            Location = new Point(2, 42),
        });
        panel.Controls.Add(titles, 0, 0);

        var help = MakeButton("Guía sencilla");
        help.Margin = new Padding(8, 8, 0, 0);
        help.Click += (_, _) =>
        {
            using var form = new HelpForm();
            form.ShowDialog(this);
        };
        panel.Controls.Add(help, 1, 0);
        return panel;
    }

    private Control BuildContextBar()
    {
        var outer = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Color.FromArgb(243, 247, 250),
            Padding = new Padding(12, 9, 12, 9),
            Margin = new Padding(0, 0, 0, 10),
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var profileLine = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
        };
        profileLine.Controls.Add(new Label
        {
            Text = "Perfil:",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 7, 8, 0),
        });
        _profileCombo.Width = 260;
        _profileCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _profileCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_profileCombo.SelectedItem is UserProfile profile)
                SelectProfile(profile);
        };
        profileLine.Controls.Add(_profileCombo);

        var manage = MakeButton("Personas");
        manage.Click += (_, _) => ManageProfiles();
        profileLine.Controls.Add(manage);

        var login = MakeButton("Iniciar sesión");
        login.Click += async (_, _) => await StartLoginAsync();
        profileLine.Controls.Add(login);
        var checkSession = MakeButton("Comprobar");
        checkSession.Click += async (_, _) => await CheckSessionAsync();
        profileLine.Controls.Add(checkSession);

        _sessionStatus.AutoSize = true;
        _sessionStatus.Margin = new Padding(10, 7, 0, 0);
        profileLine.Controls.Add(_sessionStatus);
        outer.Controls.Add(profileLine, 0, 0);

        var contextActions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true,
        };
        var race = MakeButton("Mi carrera");
        race.Click += (_, _) => OpenRaceContext();
        var journal = MakeButton("Mi diario");
        journal.Click += (_, _) => OpenJournal();
        contextActions.Controls.AddRange([journal, race]);
        outer.Controls.Add(contextActions, 1, 0);

        _raceSummary.AutoSize = true;
        _raceSummary.ForeColor = Color.FromArgb(55, 75, 90);
        _raceSummary.Margin = new Padding(0, 7, 0, 0);
        outer.Controls.Add(_raceSummary, 0, 1);
        outer.SetColumnSpan(_raceSummary, 2);
        return outer;
    }

    private Control BuildFlowTabs()
    {
        _flowTabs.Dock = DockStyle.Fill;
        _flowTabs.Padding = new Point(15, 6);
        _flowTabs.SelectedIndexChanged += (_, _) => RefreshOutputPreview();
        _flowTabs.TabPages.Add(BuildReviewTab());
        _flowTabs.TabPages.Add(BuildActivityTab());
        _flowTabs.TabPages.Add(BuildHistoryTab());
        return _flowTabs;
    }

    private TabPage BuildReviewTab()
    {
        var page = new TabPage("1. Revisión recomendada") { Padding = new Padding(20) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
        };
        layout.Controls.Add(new Label
        {
            Text = "Seguimiento semanal de tu preparación",
            AutoSize = true,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8),
        });
        layout.Controls.Add(new Label
        {
            Text = "Crea un único archivo autocontenido. Cada semana puedes sustituir el anterior en ChatGPT. " +
                   "Incluye evolución semanal, calidad de datos y contexto de carrera.",
            AutoSize = true,
            MaximumSize = new Size(850, 0),
            Margin = new Padding(0, 0, 0, 18),
        });
        var weeksLine = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        weeksLine.Controls.Add(new Label
        {
            Text = "Semanas que debe abarcar:",
            AutoSize = true,
            Margin = new Padding(0, 7, 10, 0),
        });
        _reviewWeeks.Minimum = 4;
        _reviewWeeks.Maximum = 52;
        _reviewWeeks.Value = 16;
        _reviewWeeks.Width = 70;
        _reviewWeeks.ValueChanged += (_, _) =>
        {
            RefreshReviewPeriod();
            RefreshOutputPreview();
        };
        weeksLine.Controls.Add(_reviewWeeks);
        weeksLine.Controls.Add(new Label
        {
            Text = "16 para maratón · 12 para media maratón",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(12, 7, 0, 0),
        });
        layout.Controls.Add(weeksLine);
        _reviewPeriod.AutoSize = true;
        _reviewPeriod.Font = new Font(Font, FontStyle.Bold);
        _reviewPeriod.Margin = new Padding(0, 12, 0, 0);
        layout.Controls.Add(_reviewPeriod);
        layout.Controls.Add(new Label
        {
            Text = "Consejo: sincroniza antes el reloj y completa en Garmin la autoevaluación de las sesiones recientes.",
            AutoSize = true,
            ForeColor = Color.FromArgb(45, 90, 110),
            Margin = new Padding(0, 18, 0, 0),
        });
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildActivityTab()
    {
        var page = new TabPage("2. Analizar una actividad") { Padding = new Padding(20) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
        };
        layout.Controls.Add(new Label
        {
            Text = "Estudia una sesión con el máximo detalle",
            AutoSize = true,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8),
        });
        layout.Controls.Add(new Label
        {
            Text = "Elige una actividad reciente. Se conservará la máxima resolución temporal disponible " +
                   "sin coordenadas. Garmin puede usar grabación inteligente, por lo que no siempre hay un punto por segundo.",
            AutoSize = true,
            MaximumSize = new Size(850, 0),
            Margin = new Padding(0, 0, 0, 18),
        });

        var chooser = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _activityId.Width = 310;
        _activityId.ReadOnly = true;
        _activityId.BackColor = SystemColors.Window;
        _activityId.PlaceholderText = "Referencia privada de la actividad";
        _activityId.TextChanged += (_, _) =>
        {
            if (!string.Equals(
                    _activityId.Text.Trim(),
                    _selectedActivityId,
                    StringComparison.Ordinal))
            {
                _selectedActivityId = null;
                _selectedActivityDate = null;
                _selectedActivity.Text = string.IsNullOrWhiteSpace(_activityId.Text)
                    ? "Aún no has elegido una actividad."
                    : "Referencia escrita manualmente; se buscará en los últimos 90 días.";
            }
            RefreshOutputPreview();
        };
        chooser.Controls.Add(_activityId);
        var choose = MakeButton("Buscar actividades recientes");
        choose.Click += async (_, _) => await ChooseActivityAsync();
        chooser.Controls.Add(choose);
        var diary = MakeButton("Añadir nota a esta sesión");
        diary.Click += (_, _) => OpenJournal(_activityId.Text.Trim());
        chooser.Controls.Add(diary);
        layout.Controls.Add(chooser);
        _selectedActivity.AutoSize = true;
        _selectedActivity.ForeColor = Color.FromArgb(45, 90, 110);
        _selectedActivity.Margin = new Padding(0, 12, 0, 0);
        _selectedActivity.Text = "Aún no has elegido una actividad.";
        layout.Controls.Add(_selectedActivity);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildHistoryTab()
    {
        var page = new TabPage("3. Archivo histórico") { Padding = new Padding(20) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
        };
        layout.Controls.Add(new Label
        {
            Text = "Elige un intervalo concreto",
            AutoSize = true,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8),
        });
        layout.Controls.Add(new Label
        {
            Text = "Úsalo para archivar una temporada o estudiar un periodo distinto. " +
                   "La revisión semanal recomendada suele ser más cómoda para el uso habitual.",
            AutoSize = true,
            MaximumSize = new Size(850, 0),
            Margin = new Padding(0, 0, 0, 18),
        });
        var dates = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        dates.Controls.Add(new Label
        {
            Text = "Desde:",
            AutoSize = true,
            Margin = new Padding(0, 7, 8, 0),
        });
        ConfigureDatePicker(_startDate);
        _startDate.ValueChanged += DatePickerChanged;
        dates.Controls.Add(_startDate);
        dates.Controls.Add(new Label
        {
            Text = "Hasta:",
            AutoSize = true,
            Margin = new Padding(22, 7, 8, 0),
        });
        ConfigureDatePicker(_endDate);
        _endDate.ValueChanged += DatePickerChanged;
        dates.Controls.Add(_endDate);
        layout.Controls.Add(dates);
        _historyActivityDetails.AutoSize = true;
        _historyActivityDetails.Text = "Incluir series temporales de todas las actividades (archivo mucho mayor)";
        _historyActivityDetails.Margin = new Padding(0, 16, 0, 0);
        layout.Controls.Add(_historyActivityDetails);
        page.Controls.Add(layout);
        return page;
    }

    private Control BuildFormatBar()
    {
        var bar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            Margin = new Padding(0, 10, 0, 4),
        };
        bar.Controls.Add(new Label
        {
            Text = "Formato:",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 7, 8, 0),
        });
        _formatCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _formatCombo.Width = 315;
        _formatCombo.Items.AddRange(
        [
            new FormatChoice("txt", "Texto con JSON (.txt) — recomendado para IA"),
            new FormatChoice("xlsx", "Excel (.xlsx) — opcional"),
            new FormatChoice("both", "Excel y texto — más lento"),
        ]);
        _formatCombo.SelectedIndexChanged += (_, _) =>
        {
            RefreshFormatNotice();
            RefreshOutputPreview();
            SaveSettings();
        };
        bar.Controls.Add(_formatCombo);
        _formatNotice.AutoSize = true;
        _formatNotice.ForeColor = SystemColors.GrayText;
        _formatNotice.Margin = new Padding(10, 7, 0, 0);
        bar.Controls.Add(_formatNotice);
        return bar;
    }

    private Control BuildOutputStatus()
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Margin = new Padding(0, 5, 0, 5),
        };
        _outputPreview.AutoSize = true;
        _outputPreview.MaximumSize = new Size(940, 0);
        _outputPreview.ForeColor = SystemColors.GrayText;
        panel.Controls.Add(_outputPreview);
        _status.AutoSize = true;
        _status.Font = new Font(Font, FontStyle.Bold);
        _status.Text = "Preparando el programa…";
        _status.Margin = new Padding(0, 7, 0, 0);
        panel.Controls.Add(_status);
        return panel;
    }

    private Control BuildActionBar()
    {
        var bar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            Margin = new Padding(0, 5, 0, 0),
        };
        _runButton.Text = "Crear archivo para la IA";
        _runButton.AutoSize = true;
        _runButton.Enabled = false;
        _runButton.Padding = new Padding(14, 7, 14, 7);
        _runButton.Font = new Font(_runButton.Font, FontStyle.Bold);
        _runButton.Click += async (_, _) => await RunExportAsync();
        bar.Controls.Add(_runButton);

        _cancelButton.Text = "Cancelar";
        _cancelButton.AutoSize = true;
        _cancelButton.Padding = new Padding(10, 7, 10, 7);
        _cancelButton.Enabled = false;
        _cancelButton.Click += (_, _) => CancelCurrentOperation();
        bar.Controls.Add(_cancelButton);

        var copyPrompt = MakeButton("Copiar pregunta para la IA");
        copyPrompt.Padding = new Padding(10, 7, 10, 7);
        copyPrompt.Click += (_, _) => CopyPrompt();
        bar.Controls.Add(copyPrompt);

        _openFolderButton.Text = "Abrir carpeta";
        _openFolderButton.AutoSize = true;
        _openFolderButton.Padding = new Padding(8, 7, 8, 7);
        _openFolderButton.Click += (_, _) => OpenOutputDirectory();
        bar.Controls.Add(_openFolderButton);

        _openFileButton.Text = "Abrir último archivo";
        _openFileButton.AutoSize = true;
        _openFileButton.Padding = new Padding(8, 7, 8, 7);
        _openFileButton.Enabled = false;
        _openFileButton.Click += (_, _) => OpenLastOutput();
        bar.Controls.Add(_openFileButton);

        _showLog.Text = "Detalles técnicos";
        _showLog.AutoSize = true;
        _showLog.Margin = new Padding(12, 11, 0, 0);
        _showLog.CheckedChanged += (_, _) => ToggleTechnicalLog();
        bar.Controls.Add(_showLog);
        return bar;
    }

    private static Button MakeButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Padding = new Padding(8, 4, 8, 4),
        Margin = new Padding(5, 1, 0, 1),
    };

    private static void ConfigureDatePicker(DateTimePicker picker)
    {
        picker.Format = DateTimePickerFormat.Short;
        picker.MinDate = new DateTime(2000, 1, 1);
        picker.MaxDate = DateTime.Today;
        picker.Width = 140;
    }

    private async Task DetectBackendAsync()
    {
        if (_projectRoot is null)
            return;
        _runButton.Enabled = false;
        _status.Text = "Comprobando la instalación…";
        var python = Path.Combine(_projectRoot, ".venv", "Scripts", "python.exe");
        var script = Path.Combine(_projectRoot, "garmin_export.py");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        _capabilities = await BackendCapabilities.DetectAsync(
            python,
            script,
            timeout.Token);
        ApplyCapabilities();
        if (_capabilities.IsReady)
        {
            _runButton.Enabled = true;
        }
        else
        {
            _status.Text =
                "La instalación está incompleta o no coincide con esta versión. Ejecuta Instalar.bat.";
            return;
        }
        _status.Text = AppPaths.HasSession(_activeProfile ?? _profileStore.Profiles[0])
            ? "Hay una sesión guardada. Pulsa «Comprobar» antes de la primera exportación."
            : "Antes de exportar, inicia sesión en Garmin.";
    }

    private void ApplyCapabilities()
    {
        var advancedFormats = _capabilities?.IsReady == true &&
                              _capabilities.Supports("--format");
        _formatCombo.Enabled = advancedFormats;
        if (!advancedFormats)
        {
            SelectFormat("txt");
            _formatNotice.Text = "Esta versión del exportador solo ofrece texto.";
        }
        else
        {
            SelectFormat(_settings.OutputFormat);
            RefreshFormatNotice();
        }
        RefreshOutputPreview();
    }

    private void RefreshFormatNotice()
    {
        if (_formatCombo.SelectedItem is not FormatChoice format)
            return;

        _formatNotice.Text = format.Code switch
        {
            "xlsx" =>
                "Opcional para revisar tablas. Tarda más y puede omitir series demasiado grandes; usa TXT para conservarlas.",
            "both" =>
                "Crea dos archivos y tarda más. El TXT conserva el detalle completo para la IA.",
            _ =>
                "Recomendado para IA: conserva el JSON completo y se crea más rápido.",
        };
    }

    private void PopulateProfiles()
    {
        var selectedId = _activeProfile?.Id ?? _settings.ActiveProfileId;
        _profileCombo.BeginUpdate();
        _profileCombo.Items.Clear();
        foreach (var profile in _profileStore.Profiles)
            _profileCombo.Items.Add(profile);
        _profileCombo.EndUpdate();
        var selected = _profileStore.Find(selectedId) ?? _profileStore.Profiles.First();
        _profileCombo.SelectedItem = selected;
        SelectProfile(selected);
    }

    private void SelectProfile(UserProfile profile)
    {
        if (_currentProcess is { HasExited: false } &&
            _activeProfile is not null &&
            !string.Equals(_activeProfile.Id, profile.Id, StringComparison.Ordinal))
        {
            _profileCombo.SelectedItem = _activeProfile;
            return;
        }
        var changedProfile = _activeProfile is not null &&
                             !string.Equals(
                                 _activeProfile.Id,
                                 profile.Id,
                                 StringComparison.Ordinal);
        _activeProfile = profile;
        _settings.ActiveProfileId = profile.Id;
        if (!ReferenceEquals(_profileCombo.SelectedItem, profile))
            _profileCombo.SelectedItem = profile;
        if (changedProfile)
        {
            _verifiedSessionProfileId = null;
            _lastOutputFiles = [];
            _selectedActivityId = null;
            _selectedActivityDate = null;
            _activityId.Clear();
            _selectedActivity.Text = "Aún no has elegido una actividad.";
            _logBox.Clear();
            _openFileButton.Enabled = false;
            _status.Text = "Perfil cambiado. Sus datos y archivos permanecen separados.";
        }
        ApplyRecommendedReviewWindow();
        RefreshProfileState();
        SaveSettings();
    }

    private void RefreshProfileState()
    {
        if (_activeProfile is null)
            return;
        var hasSession = AppPaths.HasSession(_activeProfile);
        var verified = hasSession &&
                       string.Equals(
                           _verifiedSessionProfileId,
                           _activeProfile.Id,
                           StringComparison.Ordinal);
        _sessionStatus.Text = verified
            ? "✓ sesión comprobada"
            : hasSession
                ? "sesión guardada"
                : "sesión pendiente";
        _sessionStatus.ForeColor = verified
            ? Color.DarkGreen
            : hasSession
                ? Color.DarkGoldenrod
                : Color.DarkOrange;

        var context = AtomicJsonStore.Read<RaceContext>(AppPaths.RaceContextFile(_activeProfile));
        if (context is null)
        {
            _raceSummary.Text = "Carrera: aún no configurada. Pulsa «Mi carrera» para mejorar el análisis.";
        }
        else
        {
            var race = context.RaceType switch
            {
                "marathon" => "Maratón",
                "half_marathon" => "Media maratón",
                "ten_k" => "10 km",
                "five_k" => "5 km",
                _ => context.DistanceKm is { } km ? $"{km:0.###} km" : "Carrera",
            };
            var date = context.RaceDate is { } raceDate
                ? $" · {raceDate:dd/MM/yyyy}"
                : "";
            var name = string.IsNullOrWhiteSpace(context.RaceName)
                ? ""
                : $" · {context.RaceName}";
            _raceSummary.Text = $"Carrera: {race}{name}{date}";
        }
        RefreshOutputPreview();
        if (Visible)
            ShowStorageRecoveryNotices();
    }

    private void ManageProfiles()
    {
        using var form = new ProfileManagerForm(_profileStore, _activeProfile);
        if (form.ShowDialog(this) != DialogResult.OK || form.SelectedProfile is null)
            return;
        PopulateProfiles();
        SelectProfile(form.SelectedProfile);
    }

    private async Task StartLoginAsync()
    {
        if (_activeProfile is null || _projectRoot is null)
        {
            ShowError("No se encontró la instalación completa. Ejecuta Instalar.bat para repararla.");
            return;
        }
        if (_capabilities?.IsReady != true)
        {
            ShowError("La instalación no está lista. Ejecuta Instalar.bat para repararla.");
            return;
        }
        if (SessionLoginLauncher.IsRunning)
        {
            ShowError(
                "Ya hay un inicio de sesión abierto. Termínalo o cierra su ventana antes de iniciar otro.");
            return;
        }
        var profile = _activeProfile;
        var projectRoot = _projectRoot;
        try
        {
            if (AppPaths.HasSession(profile) &&
                MessageBox.Show(
                    this,
                    "Este perfil ya tiene una sesión preparada.\n\n" +
                    "¿Quieres volver a identificarte para renovar la sesión o cambiar la cuenta de Garmin asociada?",
                    "Volver a iniciar sesión",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            MessageBox.Show(
                this,
                "Se abrirá una ventana negra.\n\n" +
                "Escribe allí tu usuario, contraseña y MFA. No los escribas en este programa ni en un chat.\n\n" +
                "La ventana se cerrará sola al terminar.",
                "Inicio de sesión",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            _verifiedSessionProfileId = null;
            SetRunningState(
                true,
                "Completa el inicio de sesión en la ventana negra…");
            var exitCode = await SessionLoginLauncher.RunAsync(
                profile,
                projectRoot);
            _status.Text = exitCode == 0
                ? "Inicio de sesión terminado. Pulsa «Comprobar» para validarlo."
                : "No se pudo completar el inicio de sesión.";
            if (exitCode != 0)
            {
                ShowError(
                    "Garmin no completó el inicio de sesión. Revisa el mensaje de la ventana negra y vuelve a intentarlo.");
            }
            RefreshProfileState();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetRunningState(false);
        }
    }

    private async Task CheckSessionAsync()
    {
        if (_activeProfile is null || _projectRoot is null)
            return;
        if (_capabilities?.IsReady != true)
        {
            ShowError("La instalación no está lista. Ejecuta Instalar.bat para repararla.");
            return;
        }
        if (SessionLoginLauncher.IsRunning)
        {
            ShowError(
                "El inicio de sesión sigue abierto. Termínalo o cierra su ventana y después pulsa «Comprobar».");
            return;
        }
        if (!AppPaths.HasSession(_activeProfile))
        {
            _verifiedSessionProfileId = null;
            RefreshProfileState();
            ShowError("Este perfil todavía no tiene una sesión guardada. Pulsa «Iniciar sesión».");
            return;
        }

        var profile = _activeProfile;
        SetRunningState(true, "Comprobando la sesión con Garmin…");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var valid = await SessionValidator.ValidateAsync(
                profile,
                _projectRoot,
                timeout.Token);
            if (valid)
            {
                _verifiedSessionProfileId = profile.Id;
                _status.Text = "✓ Sesión comprobada. Ya puedes crear un archivo.";
            }
            else
            {
                _verifiedSessionProfileId = null;
                _status.Text = "La sesión ha caducado o no se pudo comprobar.";
                MessageBox.Show(
                    this,
                    "No se ha podido validar la sesión de este perfil.\n\n" +
                    "Comprueba Internet y, si continúa, pulsa «Iniciar sesión».",
                    "Sesión no válida",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            RefreshProfileState();
        }
        finally
        {
            SetRunningState(false);
        }
    }

    private void OpenRaceContext()
    {
        if (_activeProfile is null)
            return;
        using var form = new RaceContextForm(_activeProfile);
        ShowStorageRecoveryNotices();
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            ApplyRecommendedReviewWindow();
            RefreshProfileState();
        }
    }

    private void OpenJournal(string? activityId = null)
    {
        if (_activeProfile is null)
            return;
        using var form = new JournalForm(_activeProfile, activityId);
        ShowStorageRecoveryNotices();
        form.ShowDialog(this);
    }

    private async Task ChooseActivityAsync()
    {
        if (_activeProfile is null || _projectRoot is null)
            return;
        if (_capabilities?.IsReady != true)
        {
            ShowError("La instalación no está lista. Ejecuta Instalar.bat para repararla.");
            return;
        }
        if (SessionLoginLauncher.IsRunning)
        {
            ShowError(
                "El inicio de sesión sigue abierto. Termínalo o cierra su ventana antes de buscar actividades.");
            return;
        }
        if (!AppPaths.HasSession(_activeProfile))
        {
            ShowError("Primero inicia sesión en Garmin.");
            return;
        }
        if (_capabilities?.Supports("--list-activities") != true)
        {
            MessageBox.Show(
                this,
                "Esta versión no puede mostrar la lista automáticamente. " +
                "Ejecuta Instalar.bat para actualizar o reparar el programa.",
                "Lista no disponible",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _cancelRequested = false;
        SetRunningState(
            true,
            "Buscando actividades recientes…",
            cancelable: true);
        _logBox.Clear();
        try
        {
            var listPath = AppPaths.ActivityListFile(_activeProfile);
            Directory.CreateDirectory(Path.GetDirectoryName(listPath)!);
            TryDeleteGeneratedFile(listPath);
            var listRequestedAt = DateTime.UtcNow;
            var startInfo = CreateBaseStartInfo();
            AddOption(startInfo, "--list-activities", listPath);
            AddOption(startInfo, "--days", "90");
            AddOption(startInfo, "--tokenstore", AppPaths.TokenStore(_activeProfile));
            AddOption(startInfo, "--ignore-credential-env");
            AddOption(startInfo, "--non-interactive-auth");
            if (_capabilities.Supports("--cache-dir"))
                AddOption(startInfo, "--cache-dir", AppPaths.CacheDirectory(_activeProfile));

            var exitCode = await RunProcessAsync(startInfo);
            if (_cancelRequested)
            {
                _status.Text = "Búsqueda de actividades cancelada.";
                return;
            }
            if (exitCode != 0)
                throw new InvalidOperationException("No se pudo obtener la lista de actividades.");
            _verifiedSessionProfileId = _activeProfile.Id;
            if (!File.Exists(listPath) ||
                File.GetLastWriteTimeUtc(listPath) < listRequestedAt.AddSeconds(-2))
            {
                throw new InvalidOperationException("No se recibió una lista nueva de Garmin.");
            }
            var activities = RecentActivityReader.Read(listPath);
            if (activities.Count == 0)
                throw new InvalidOperationException("Garmin no devolvió actividades recientes.");

            using var picker = new ActivityPickerForm(activities);
            if (picker.ShowDialog(this) == DialogResult.OK &&
                picker.SelectedActivity is { } selected)
            {
                _selectedActivityId = selected.Id;
                _activityId.Text = selected.Id;
                _selectedActivity.Text = $"Elegida: {selected}";
                _selectedActivityDate = DateTime.TryParse(
                    selected.Date,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out var parsed)
                    ? parsed.Date
                    : null;
                RefreshOutputPreview();
            }
            _status.Text = "Actividad preparada.";
        }
        catch (Exception ex)
        {
            if (!_cancelRequested)
            {
                _status.Text = "No se pudo cargar la lista.";
                AppendLog($"ERROR: {ex.Message}");
                ShowError(ex.Message);
            }
        }
        finally
        {
            SetRunningState(false);
        }
    }

    private async Task RunExportAsync()
    {
        if (_activeProfile is null || _projectRoot is null)
        {
            ShowError("No se encontró la instalación completa. Ejecuta Instalar.bat para repararla.");
            return;
        }
        if (_capabilities?.IsReady != true)
        {
            ShowError("La instalación no está lista. Ejecuta Instalar.bat para repararla.");
            return;
        }
        if (SessionLoginLauncher.IsRunning)
        {
            ShowError(
                "El inicio de sesión sigue abierto. Termínalo o cierra su ventana antes de exportar.");
            return;
        }
        if (!AppPaths.HasSession(_activeProfile))
        {
            ShowError("Primero pulsa «Iniciar sesión» y completa el acceso en la ventana negra.");
            return;
        }

        var mode = CurrentMode;
        if (mode == ReportMode.Activity &&
            string.IsNullOrWhiteSpace(_activityId.Text))
        {
            ShowError("Elige una actividad antes de continuar.");
            return;
        }
        if (mode == ReportMode.Activity &&
            !IsPrivateActivityReference(_activityId.Text))
        {
            ShowError(
                "La referencia privada no es válida. " +
                "Vuelve a elegir la actividad con «Buscar actividades recientes».");
            return;
        }
        if (mode == ReportMode.Activity &&
            _capabilities?.Supports("--activity-id") != true)
        {
            ShowError("La versión instalada del exportador todavía no admite el análisis de una sola actividad.");
            return;
        }
        if (_startDate.Value.Date > _endDate.Value.Date)
        {
            ShowError("La fecha inicial no puede ser posterior a la final.");
            return;
        }

        SaveSettings();
        _logBox.Clear();
        _lastOutputFiles = [];
        _openFileButton.Enabled = false;
        _cancelRequested = false;
        SetRunningState(
            true,
            "Descargando y preparando los datos…",
            cancelable: true);
        var startedAt = DateTime.UtcNow;

        try
        {
            var outputDirectory = AppPaths.OutputDirectory(_activeProfile);
            Directory.CreateDirectory(outputDirectory);
            Directory.CreateDirectory(AppPaths.ProfileDataRoot(_activeProfile));
            var (start, end) = CurrentDateRange();
            var fileStem = BuildFileStem();
            var format = SelectedFormat;
            var runId = Guid.NewGuid().ToString("N");

            AppendLog($"Tipo: {ModeDisplayName(mode)}");
            AppendLog($"Intervalo: {start:yyyy-MM-dd} a {end:yyyy-MM-dd}");
            AppendLog($"Formato: {format}");
            AppendLog("Destino: carpeta del perfil dentro de Documentos.");
            AppendLog("Las credenciales no se incluyen en el archivo.");

            var startInfo = CreateBaseStartInfo();
            AddOption(startInfo, "--start-date", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            AddOption(startInfo, "--end-date", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            AddOption(startInfo, "--compact");
            AddOption(startInfo, "--output", outputDirectory);
            AddOption(startInfo, "--filename", fileStem);
            AddOption(startInfo, "--tokenstore", AppPaths.TokenStore(_activeProfile));
            AddOption(startInfo, "--ignore-credential-env");
            AddOption(startInfo, "--non-interactive-auth");

            if (_capabilities?.Supports("--report") == true)
                AddOption(startInfo, "--report", ModeCode(mode));
            if (_capabilities?.Supports("--format") == true)
                AddOption(startInfo, "--format", format);
            if (_capabilities?.Supports("--cache-dir") == true)
                AddOption(startInfo, "--cache-dir", AppPaths.CacheDirectory(_activeProfile));
            if (_capabilities?.Supports("--manifest") == true)
                AddOption(startInfo, "--manifest", AppPaths.ManifestFile(_activeProfile));
            AddOption(startInfo, "--run-id", runId);

            var contextPath = AppPaths.RaceContextFile(_activeProfile);
            if (_capabilities?.Supports("--race-context") == true &&
                File.Exists(contextPath))
            {
                AddOption(startInfo, "--race-context", contextPath);
            }
            var journalPath = AppPaths.JournalFile(_activeProfile);
            if (_capabilities?.Supports("--journal") == true &&
                File.Exists(journalPath))
            {
                AddOption(startInfo, "--journal", journalPath);
            }

            if (mode == ReportMode.Preparation &&
                _capabilities?.Supports("--review-weeks") == true)
            {
                AddOption(startInfo, "--review-weeks", ((int)_reviewWeeks.Value).ToString(CultureInfo.InvariantCulture));
            }
            if (mode == ReportMode.Activity)
            {
                AddOption(startInfo, "--activity-id", _activityId.Text.Trim());
                AddOption(startInfo, "--activity-details");
            }
            if (mode == ReportMode.History && _historyActivityDetails.Checked)
                AddOption(startInfo, "--activity-details");

            var exitCode = await RunProcessAsync(startInfo);
            if (_cancelRequested)
            {
                _status.Text =
                    "Operación cancelada. No se dará por válido ningún archivo nuevo. " +
                    "La próxima ejecución podrá reutilizar la caché ya guardada.";
                return;
            }
            if (exitCode != 0)
                throw new InvalidOperationException(
                    $"El exportador terminó con el código {exitCode}. Abre los detalles técnicos.");
            _verifiedSessionProfileId = _activeProfile.Id;

            var discovered = FindGeneratedFiles(
                outputDirectory,
                fileStem,
                format,
                startedAt,
                runId,
                mode,
                start,
                end);
            _lastOutputFiles = discovered.Files;
            if (_lastOutputFiles.Count == 0)
                throw new FileNotFoundException(
                    "La descarga terminó, pero no se encontró el archivo generado.");

            _openFileButton.Enabled = true;
            foreach (var path in _lastOutputFiles)
                AppendLog($"Archivo creado: {Path.GetFileName(path)}");
            foreach (var section in discovered.ErrorSections)
                AppendLog($"Sección incompleta: {section}");

            if (discovered.IsPartial)
            {
                _status.Text =
                    "⚠ Archivo creado con datos parciales. Revisa los detalles antes de usarlo.";
                MessageBox.Show(
                    this,
                    "Se ha creado un archivo utilizable, pero Garmin no devolvió correctamente " +
                    "todas las secciones.\n\nAbre «Detalles técnicos», revisa qué ha faltado y " +
                    "vuelve a intentarlo más tarde si esos datos son importantes.",
                    "Exportación parcial",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            else
            {
                _status.Text = _lastOutputFiles.Count == 1
                    ? "✓ Archivo creado. Ya puedes subirlo manualmente a tu IA."
                    : $"✓ {_lastOutputFiles.Count} archivos creados. Ya puedes subirlos manualmente a tu IA.";
                MessageBox.Show(
                    this,
                    "La exportación ha terminado correctamente.\n\n" +
                    "Siguiente paso: abre el archivo, súbelo manualmente a tu IA y usa «Copiar pregunta para la IA».",
                    "Archivo preparado",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            if (!_cancelRequested)
            {
                _status.Text = "La exportación no se completó.";
                AppendLog($"ERROR: {ex.Message}");
                ShowError(ex.Message);
            }
        }
        finally
        {
            SetRunningState(false);
        }
    }

    private ProcessStartInfo CreateBaseStartInfo()
    {
        if (_projectRoot is null)
            throw new InvalidOperationException("No se encontró el proyecto.");
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(_projectRoot, ".venv", "Scripts", "python.exe"),
            WorkingDirectory = _projectRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";
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
        startInfo.ArgumentList.Add(Path.Combine(_projectRoot, "garmin_export.py"));
        return startInfo;
    }

    private static void AddOption(ProcessStartInfo startInfo, string option, string? value = null)
    {
        startInfo.ArgumentList.Add(option);
        if (value is not null)
            startInfo.ArgumentList.Add(value);
    }

    private async Task<int> RunProcessAsync(ProcessStartInfo startInfo)
    {
        using var process = new Process { StartInfo = startInfo };
        _currentProcess = process;
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("No se pudo iniciar el exportador.");
            var stdout = PumpOutputAsync(process.StandardOutput);
            var stderr = PumpOutputAsync(process.StandardError);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            return process.ExitCode;
        }
        finally
        {
            _currentProcess = null;
        }
    }

    private async Task PumpOutputAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
            AppendLog(RedactLocalPaths(TranslateVisibleLogLine(line)));
    }

    private static string TranslateVisibleLogLine(string line)
    {
        foreach (var (original, translation) in SectionTranslations)
            line = line.Replace(original, translation, StringComparison.Ordinal);
        return line;
    }

    private string RedactLocalPaths(string line)
    {
        if (_activeProfile is not null)
        {
            line = line.Replace(
                AppPaths.TokenStore(_activeProfile),
                "<sesión local>",
                StringComparison.OrdinalIgnoreCase);
            line = line.Replace(
                AppPaths.CacheDirectory(_activeProfile),
                "<caché local>",
                StringComparison.OrdinalIgnoreCase);
            line = line.Replace(
                AppPaths.OutputDirectory(_activeProfile),
                "<carpeta de salida>",
                StringComparison.OrdinalIgnoreCase);
        }
        var userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userFolder))
        {
            line = line.Replace(
                userFolder,
                "<carpeta personal>",
                StringComparison.OrdinalIgnoreCase);
        }
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

    private void CancelCurrentOperation()
    {
        if (_currentProcess is null || _currentProcess.HasExited)
            return;
        _cancelRequested = true;
        _status.Text = "Cancelando la operación…";
        try
        {
            _currentProcess.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // El proceso ya ha terminado.
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            AppendLog($"No se pudo detener inmediatamente: {ex.Message}");
        }
    }

    private void SetRunningState(
        bool running,
        string? status = null,
        bool cancelable = false)
    {
        _runButton.Enabled = !running && _capabilities?.IsReady == true;
        _cancelButton.Enabled = running && cancelable;
        _profileCombo.Enabled = !running;
        if (_contextBar is not null)
            _contextBar.Enabled = !running;
        _flowTabs.Enabled = !running;
        _formatCombo.Enabled = !running && _capabilities?.Supports("--format") == true;
        _openFolderButton.Enabled = !running && _activeProfile is not null;
        _openFileButton.Enabled = !running && _lastOutputFiles.Any(File.Exists);
        _progress.Style = running ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
        _progress.MarqueeAnimationSpeed = running ? 30 : 0;
        if (status is not null)
            _status.Text = status;
    }

    private ExportDiscovery FindGeneratedFiles(
        string outputDirectory,
        string fileStem,
        string format,
        DateTime startedAtUtc,
        string runId,
        ReportMode mode,
        DateTime start,
        DateTime end)
    {
        if (_activeProfile is not null &&
            _capabilities?.Supports("--manifest") == true)
        {
            var fromManifest = ReadManifest(
                AppPaths.ManifestFile(_activeProfile),
                outputDirectory,
                startedAtUtc,
                runId,
                ModeCode(mode),
                start,
                end,
                fileStem,
                format);
            if (fromManifest is not null)
                return fromManifest;
            return new ExportDiscovery([], false, []);
        }

        var extensions = format switch
        {
            "xlsx" => new[] { ".xlsx" },
            "both" => new[] { ".xlsx", ".txt" },
            _ => new[] { ".txt" },
        };
        var exact = extensions
            .Select(extension => Path.Combine(outputDirectory, fileStem + extension))
            .Where(path =>
                File.Exists(path) &&
                File.GetLastWriteTimeUtc(path) >= startedAtUtc.AddSeconds(-2))
            .ToList();
        if (exact.Count == extensions.Length)
            return new ExportDiscovery(exact, false, []);
        return new ExportDiscovery([], false, []);
    }

    private static ExportDiscovery? ReadManifest(
        string manifestPath,
        string outputDirectory,
        DateTime startedAtUtc,
        string expectedRunId,
        string expectedReportType,
        DateTime expectedStart,
        DateTime expectedEnd,
        string expectedFileStem,
        string expectedFormat)
    {
        try
        {
            if (!File.Exists(manifestPath) ||
                File.GetLastWriteTimeUtc(manifestPath) < startedAtUtc.AddSeconds(-2))
                return null;
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("run_id", out var runIdValue) ||
                !string.Equals(
                    runIdValue.GetString(),
                    expectedRunId,
                    StringComparison.Ordinal) ||
                !MatchesManifestValue(root, "report_type", expectedReportType) ||
                !MatchesManifestValue(root, "output_format", expectedFormat) ||
                !MatchesManifestValue(root, "file_stem", expectedFileStem) ||
                !MatchesManifestValue(
                    root,
                    "start_date",
                    expectedStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) ||
                !MatchesManifestValue(
                    root,
                    "end_date",
                    expectedEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            {
                return null;
            }
            JsonElement files;
            if (!root.TryGetProperty("files", out files) &&
                     !root.TryGetProperty("output_files", out files))
                return null;
            if (files.ValueKind != JsonValueKind.Array)
                return null;

            var result = new List<string>();
            foreach (var item in files.EnumerateArray())
            {
                var value = item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : item.ValueKind == JsonValueKind.Object &&
                      item.TryGetProperty("path", out var pathValue)
                        ? pathValue.GetString()
                        : null;
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                var fullPath = Path.IsPathRooted(value)
                    ? Path.GetFullPath(value)
                    : Path.GetFullPath(Path.Combine(outputDirectory, value));
                if (IsWithinDirectory(fullPath, outputDirectory) && File.Exists(fullPath))
                    result.Add(fullPath);
            }
            var expectedExtensions = expectedFormat switch
            {
                "xlsx" => new[] { ".xlsx" },
                "both" => new[] { ".txt", ".xlsx" },
                _ => new[] { ".txt" },
            };
            var uniqueResult = result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (uniqueResult.Count != expectedExtensions.Length ||
                expectedExtensions.Any(extension =>
                    !uniqueResult.Any(path =>
                        string.Equals(
                            Path.GetFileName(path),
                            expectedFileStem + extension,
                            StringComparison.OrdinalIgnoreCase))))
            {
                return null;
            }
            var status = root.ValueKind == JsonValueKind.Object &&
                         root.TryGetProperty("status", out var statusValue)
                ? statusValue.GetString()
                : "completed";
            if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(status, "partial", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            var errorSections = new List<string>();
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array)
            {
                foreach (var error in errors.EnumerateArray())
                {
                    var value = error.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        errorSections.Add(value[..Math.Min(value.Length, 120)]);
                }
            }
            var isPartial =
                string.Equals(status, "partial", StringComparison.OrdinalIgnoreCase) ||
                errorSections.Count > 0;
            return new ExportDiscovery(
                uniqueResult,
                isPartial,
                errorSections);
        }
        catch
        {
            return null;
        }
    }

    private static bool MatchesManifestValue(
        JsonElement root,
        string property,
        string expected) =>
        root.TryGetProperty(property, out var value) &&
        string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static bool IsWithinDirectory(string path, string directory)
    {
        var root = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteGeneratedFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // La marca de tiempo evita confundirlo después con un resultado nuevo.
        }
    }

    private void CopyPrompt()
    {
        var mode = CurrentMode;
        var contextText = "";
        RaceContext? context = null;
        if (_activeProfile is not null)
        {
            context = AtomicJsonStore.Read<RaceContext>(
                AppPaths.RaceContextFile(_activeProfile));
            if (context is not null)
            {
                var race = context.RaceType switch
                {
                    "marathon" => "maratón",
                    "half_marathon" => "media maratón",
                    "ten_k" => "10 km",
                    "five_k" => "5 km",
                    _ => $"{context.DistanceKm:0.###} km",
                };
                contextText = $" Mi objetivo es {race}" +
                              (context.RaceDate is { } date ? $" el {date:yyyy-MM-dd}" : "") +
                              ".";
            }
        }

        var common =
            "Primero evalúa la calidad, cobertura y ausencias de los datos. " +
            "No conviertas valores ausentes en cero. Separa hechos, cálculos e inferencias; " +
            "cita fechas, semanas y referencias de actividad. Si falta información importante, pregúntamela. " +
            "No diagnostiques lesiones ni enfermedades y señala cuándo conviene consultar a un profesional.";
        var prompt = mode switch
        {
            ReportMode.Preparation =>
                AiPromptBuilder.BuildPreparationReview(
                    context,
                    CurrentDateRange().End),
            ReportMode.Activity =>
                $"Analiza únicamente la actividad seleccionada como apoyo a mi preparación.{contextText} " +
                $"{common} Examina ritmo o potencia, pulso, vueltas, zonas, deriva solo si los datos permiten " +
                "calcularla, autoevaluación y nutrición anotada. Explica qué salió bien, qué limitó la sesión " +
                "y una recomendación práctica para la próxima sesión similar.",
            _ =>
                $"Analiza este archivo histórico de Garmin para entender tendencias de entrenamiento.{contextText} " +
                $"{common} Resume cambios sostenidos, periodos de mayor carga, constancia, tiradas largas, " +
                "recuperación y posibles lagunas del registro. Evita recomendaciones basadas en una sola cifra.",
        };

        try
        {
            Clipboard.SetText(prompt);
            _status.Text = "Pregunta copiada. Pégala en la conversación junto con el archivo.";
        }
        catch (Exception ex)
        {
            ShowError($"No se pudo copiar la pregunta: {ex.Message}");
        }
    }

    private void OpenOutputDirectory()
    {
        if (_activeProfile is null)
            return;
        var directory = AppPaths.OutputDirectory(_activeProfile);
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { directory },
            UseShellExecute = true,
        });
    }

    private void OpenLastOutput()
    {
        var file = _lastOutputFiles
            .Where(File.Exists)
            .OrderBy(path =>
                string.Equals(
                    Path.GetExtension(path),
                    ".xlsx",
                    StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : 1)
            .FirstOrDefault();
        if (file is null)
            return;
        Process.Start(new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = true,
        });
    }

    private void ToggleTechnicalLog()
    {
        var visible = _showLog.Checked;
        _logGroup.Visible = visible;
        if (_logRowStyle is not null)
            _logRowStyle.Height = visible ? 190 : 0;
        _settings.ShowTechnicalLog = visible;
        if (visible && Height < 880)
            Height = Math.Min(Screen.FromControl(this).WorkingArea.Height, 940);
        SaveSettings();
    }

    private void DatePickerChanged(object? sender, EventArgs e)
    {
        if (_startDate.Value.Date > _endDate.Value.Date)
        {
            if (ReferenceEquals(sender, _startDate))
                _endDate.Value = _startDate.Value.Date;
            else
                _startDate.Value = _endDate.Value.Date;
        }
        RefreshOutputPreview();
    }

    private void RefreshReviewPeriod()
    {
        var (start, end) = ReviewDateRange();
        _reviewPeriod.Text =
            $"Se incluirán datos desde {start:dd/MM/yyyy} hasta {end:dd/MM/yyyy}.";
    }

    private void ApplyRecommendedReviewWindow()
    {
        if (_activeProfile is null)
            return;
        var context = AtomicJsonStore.Read<RaceContext>(
            AppPaths.RaceContextFile(_activeProfile));
        var recommended = context?.RaceType switch
        {
            "marathon" => 16,
            "half_marathon" => 12,
            _ => (int?)null,
        };
        if (recommended is null)
            return;
        _reviewWeeks.Value = Math.Clamp(
            recommended.Value,
            (int)_reviewWeeks.Minimum,
            (int)_reviewWeeks.Maximum);
        SaveSettings();
    }

    private void RefreshOutputPreview()
    {
        RefreshReviewPeriod();
        if (_activeProfile is null)
            return;
        var extensionDescription = SelectedFormat switch
        {
            "xlsx" => ".xlsx",
            "both" => ".xlsx y .txt",
            _ => ".txt",
        };
        _outputPreview.Text =
            $"Se guardará en «Documentos\\Garmin para IA\\{_activeProfile.OutputFolderName}» " +
            $"como «{BuildFileStem()}{extensionDescription}».";
    }

    private (DateTime Start, DateTime End) CurrentDateRange() => CurrentMode switch
    {
        ReportMode.Preparation => ReviewDateRange(),
        ReportMode.Activity => ActivityDateRange(),
        _ => (_startDate.Value.Date, _endDate.Value.Date),
    };

    private (DateTime Start, DateTime End) ReviewDateRange()
    {
        var end = DateTime.Today;
        var inclusiveDays = checked((int)_reviewWeeks.Value * 7);
        var start = end.AddDays(-(inclusiveDays - 1));
        return (start, end);
    }

    private (DateTime Start, DateTime End) ActivityDateRange()
    {
        if (_selectedActivityDate is { } date)
            return (date.AddDays(-1), date.AddDays(1) > DateTime.Today ? DateTime.Today : date.AddDays(1));
        return (DateTime.Today.AddDays(-90), DateTime.Today);
    }

    private string BuildFileStem()
    {
        var mode = CurrentMode;
        if (mode == ReportMode.Preparation)
        {
            var context = _activeProfile is null
                ? null
                : AtomicJsonStore.Read<RaceContext>(AppPaths.RaceContextFile(_activeProfile));
            var race = context?.RaceName;
            if (string.IsNullOrWhiteSpace(race))
            {
                race = context?.RaceType switch
                {
                    "marathon" => "maraton",
                    "half_marathon" => "media_maraton",
                    "ten_k" => "10_km",
                    "five_k" => "5_km",
                    _ => "preparacion",
                };
            }
            return $"revision_{Slug(race)}_actual";
        }
        if (mode == ReportMode.Activity)
        {
            var date = _selectedActivityDate?.ToString("yyyy-MM-dd") ?? "actual";
            var reference = Slug(
                _selectedActivityId
                ?? _activityId.Text.Trim());
            var suffix = reference.Length > 10
                ? reference[^10..]
                : reference;
            return $"analisis_actividad_{date}_{suffix}";
        }
        return $"garmin_historico_{_startDate.Value:yyyy-MM-dd}_a_{_endDate.Value:yyyy-MM-dd}";
    }

    private static string Slug(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        var previousSeparator = false;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) ==
                UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousSeparator = false;
            }
            else if (!previousSeparator && builder.Length > 0)
            {
                builder.Append('_');
                previousSeparator = true;
            }
            if (builder.Length >= 40)
                break;
        }
        return builder.ToString().Trim('_') is { Length: > 0 } result
            ? result
            : "preparacion";
    }

    private static bool IsPrivateActivityReference(string value) =>
        Regex.IsMatch(value.Trim(), @"\Aactivity_[0-9a-f]{12}\z");

    private ReportMode CurrentMode => (ReportMode)_flowTabs.SelectedIndex;

    private string SelectedFormat =>
        (_formatCombo.SelectedItem as FormatChoice)?.Code ?? "txt";

    private static string ModeCode(ReportMode mode) => mode switch
    {
        ReportMode.Preparation => "preparation",
        ReportMode.Activity => "activity",
        _ => "history",
    };

    private static string ModeDisplayName(ReportMode mode) => mode switch
    {
        ReportMode.Preparation => "revisión de preparación",
        ReportMode.Activity => "análisis de una actividad",
        _ => "archivo histórico",
    };

    private void SelectFormat(string? code)
    {
        _formatCombo.SelectedItem = _formatCombo.Items
            .OfType<FormatChoice>()
            .FirstOrDefault(item => item.Code == code)
            ?? _formatCombo.Items.OfType<FormatChoice>().First();
    }

    private void LocateProject()
    {
        _projectRoot = FindProjectRoot();
        if (_projectRoot is not null)
            return;
        _status.Text = "No se encontró la instalación completa.";
        _runButton.Enabled = false;
        _sessionStatus.Text = "Ejecuta Instalar.bat";
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
                var script = Path.Combine(directory.FullName, "garmin_export.py");
                var python = Path.Combine(directory.FullName, ".venv", "Scripts", "python.exe");
                if (File.Exists(script) && File.Exists(python))
                    return directory.FullName;
            }
        }
        return null;
    }

    private void LoadSettings()
    {
        var storedSettings = AtomicJsonStore.Read<LauncherSettings>(AppPaths.SettingsFile);
        if (storedSettings is null)
        {
            _settings = new LauncherSettings
            {
                SchemaVersion = LauncherSettings.CurrentSchemaVersion,
            };
            return;
        }

        _settings = storedSettings;
        // Migración única: las instalaciones anteriores recomendaban Excel.
        // Después de guardar la versión actual, las elecciones del usuario se respetan.
        if (!_settings.ApplyMigrations())
            return;

        try
        {
            AtomicJsonStore.Write(AppPaths.SettingsFile, _settings);
        }
        catch (IOException)
        {
            // Si no se puede guardar, se volverá a intentar en el siguiente inicio.
        }
        catch (UnauthorizedAccessException)
        {
            // Si no se puede guardar, se volverá a intentar en el siguiente inicio.
        }
    }

    private void ApplySettingsToControls()
    {
        _reviewWeeks.Value = Math.Clamp(
            _settings.ReviewWeeks,
            (int)_reviewWeeks.Minimum,
            (int)_reviewWeeks.Maximum);
        _startDate.Value = ClampDate(_settings.StartDate, _startDate);
        _endDate.Value = ClampDate(_settings.EndDate, _endDate);
        if (_startDate.Value > _endDate.Value)
            _startDate.Value = _endDate.Value;
        _historyActivityDetails.Checked = _settings.IncludeActivityDetails;
        SelectFormat(_settings.OutputFormat);
        _showLog.Checked = _settings.ShowTechnicalLog;
        ToggleTechnicalLog();
        RefreshReviewPeriod();
        RefreshOutputPreview();
    }

    private void SaveSettings()
    {
        if (_reviewWeeks.IsHandleCreated)
            _settings.ReviewWeeks = (int)_reviewWeeks.Value;
        if (_formatCombo.SelectedItem is FormatChoice format)
            _settings.OutputFormat = format.Code;
        if (_startDate.IsHandleCreated)
        {
            _settings.StartDate = _startDate.Value.Date;
            _settings.EndDate = _endDate.Value.Date;
            _settings.IncludeActivityDetails = _historyActivityDetails.Checked;
        }
        _settings.ShowTechnicalLog = _showLog.Checked;
        try
        {
            AtomicJsonStore.Write(AppPaths.SettingsFile, _settings);
        }
        catch (IOException)
        {
            // Las preferencias no deben impedir una exportación.
        }
        catch (UnauthorizedAccessException)
        {
            // Las preferencias no deben impedir una exportación.
        }
    }

    private static DateTime ClampDate(DateTime value, DateTimePicker picker)
    {
        var date = value.Year < 2000 ? DateTime.Today : value.Date;
        if (date < picker.MinDate)
            return picker.MinDate;
        if (date > picker.MaxDate)
            return picker.MaxDate;
        return date;
    }

    private void ShowError(string message)
    {
        MessageBox.Show(
            this,
            message,
            "EntrenaIA",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void ShowStorageRecoveryNotices()
    {
        var notices = AtomicJsonStore.ConsumeRecoveryNotices();
        if (notices.Count == 0)
            return;
        MessageBox.Show(
            this,
            string.Join("\n\n", notices),
            "Copia de seguridad restaurada",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (SessionLoginLauncher.IsRunning)
        {
            MessageBox.Show(
                this,
                "Termina o cierra primero la ventana negra de inicio de sesión.\n\n" +
                "El programa permanecerá abierto para proteger la sesión mientras se guarda.",
                "Inicio de sesión en curso",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            e.Cancel = true;
            return;
        }
        if (_currentProcess is not null && !_currentProcess.HasExited)
        {
            var answer = MessageBox.Show(
                this,
                "Hay una descarga en curso. ¿Quieres cancelarla y cerrar?",
                "Descarga en curso",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            CancelCurrentOperation();
        }
        SaveSettings();
    }

    private enum ReportMode
    {
        Preparation = 0,
        Activity = 1,
        History = 2,
    }

    private sealed record FormatChoice(string Code, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed record ExportDiscovery(
        List<string> Files,
        bool IsPartial,
        List<string> ErrorSections);
}

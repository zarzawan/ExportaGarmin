using GarminDataExport.Launcher.Models;
using GarminDataExport.Launcher.Services;
using System.Text.RegularExpressions;

namespace GarminDataExport.Launcher.Views;

internal sealed class JournalForm : Form
{
    private readonly string _path;
    private readonly string _initialActivityId;
    private readonly bool _isPreview;
    private readonly IReadOnlyDictionary<string, RecentActivity> _activitiesById;
    private JournalDocument _document;
    private string? _editingEntryId;

    private readonly DateTimePicker _date = new();
    private readonly TextBox _activityId = new();
    private readonly ComboBox _activityPicker = new();
    private readonly TextBox _purpose = new();
    private readonly ComboBox _pain = CreateScoreCombo(0, 10);
    private readonly TextBox _painLocation = new();
    private readonly ComboBox _effort = CreateScoreCombo(1, 10);
    private readonly ComboBox _fatigue = CreateScoreCombo(1, 5);
    private readonly ComboBox _motivation = CreateScoreCombo(1, 5);
    private readonly ComboBox _lifeStress = CreateScoreCombo(1, 10);
    private readonly TextBox _carbohydrates = new();
    private readonly TextBox _fluid = new();
    private readonly TextBox _sodium = new();
    private readonly ComboBox _giTolerance = new();
    private readonly TextBox _comment = new();
    private readonly DataGridView _recentEntries = new();
    private readonly ComboBox _entrySelector = new();
    private readonly Panel _advancedPanel = new();
    private readonly Button _advancedButton = MakeButton("Más datos opcionales  ▾");
    private readonly Button _saveButton = MakeButton("Guardar anotación");
    private readonly Button _cancelEditButton = MakeButton("Cancelar edición");
    private readonly Button _editButton = MakeButton("Editar seleccionada");
    private readonly Button _deleteButton = MakeButton("Eliminar");
    private readonly Label _editorTitle = new();
    private readonly Label _saveStatus = new();

    public JournalForm(
        UserProfile profile,
        string? activityId = null,
        IReadOnlyList<RecentActivity>? activities = null,
        JournalDocument? previewDocument = null)
    {
        _isPreview = previewDocument is not null;
        _path = _isPreview ? "" : AppPaths.JournalFile(profile);
        _initialActivityId = activityId?.Trim() ?? "";
        _activitiesById = (activities ?? [])
            .Where(activity => IsPrivateActivityReferenceOrEmpty(activity.Id) &&
                               !string.IsNullOrWhiteSpace(activity.Id))
            .GroupBy(activity => activity.Id, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
        _document = previewDocument ??
            AtomicJsonStore.Read<JournalDocument>(_path) ??
            new JournalDocument();
        _document.Entries ??= [];

        Text = $"Mi diario — {profile.Alias}";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(860, 680);
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1200, 900);
        Size = new Size(
            Math.Min(1040, Math.Max(860, workingArea.Width - 70)),
            Math.Min(840, Math.Max(680, workingArea.Height - 70)));
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(247, 249, 252);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(20, 16, 20, 16),
            BackColor = BackColor,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildEditor(), 0, 1);
        root.Controls.Add(BuildHistory(), 0, 2);
        root.Controls.Add(BuildActions(), 0, 3);

        _advancedButton.Click += (_, _) => SetAdvancedVisible(!_advancedPanel.Visible);
        _saveButton.Font = new Font(_saveButton.Font, FontStyle.Bold);
        _saveButton.Click += (_, _) => SaveEntry();
        _cancelEditButton.Click += (_, _) => StartNewEntry();
        _editButton.Click += (_, _) => EditSelectedEntry();
        _deleteButton.Click += (_, _) => DeleteSelectedEntry();
        _recentEntries.CellDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex >= 0)
                EditSelectedEntry();
        };
        _recentEntries.SelectionChanged += (_, _) =>
        {
            if (SelectedEntryId() is { } entryId)
                SelectEntryChoice(entryId);
            UpdateSelectionButtons();
        };
        _entrySelector.SelectedIndexChanged += (_, _) => UpdateSelectionButtons();

        ConfigureInputs();
        StartNewEntry();
        RefreshEntries();
        Shown += (_, _) => BeginInvoke(() =>
        {
            _recentEntries.ClearSelection();
            UpdateSelectionButtons();
            _comment.Focus();
        });
    }

    internal static JournalForm CreateReadmePreview()
    {
        var profile = new UserProfile
        {
            Id = "f1e2d3c4b5a697887766554433221100",
            Alias = "Perfil de ejemplo",
            OutputFolderName = "Perfil de ejemplo",
        };
        var activities = new List<RecentActivity>
        {
            new()
            {
                Id = "activity_123456789abc",
                Date = "2026-08-16",
                Sport = "Carrera",
                Label = "Tirada larga",
                DistanceMeters = 18000,
                DurationSeconds = 6300,
            },
            new()
            {
                Id = "activity_abcdef123456",
                Date = "2026-08-14",
                Sport = "Carrera",
                Label = "Series 6 × 1000 m",
                DistanceMeters = 10500,
                DurationSeconds = 3120,
            },
        };
        var document = new JournalDocument
        {
            Entries =
            [
                new JournalEntry
                {
                    Date = new DateTime(2026, 8, 16),
                    ActivityId = "activity_123456789abc",
                    IntendedPurpose = "Tirada larga progresiva",
                    PerceivedEffort1To10 = 6,
                    PainScore0To10 = 1,
                    PrivateComment =
                        "Buenas sensaciones durante casi toda la sesión. " +
                        "El calor se notó al final, pero pude mantener el ritmo previsto.",
                    IncludeCommentInExport = true,
                },
                new JournalEntry
                {
                    Date = new DateTime(2026, 8, 14),
                    ActivityId = "activity_abcdef123456",
                    IntendedPurpose = "Series",
                    PerceivedEffort1To10 = 8,
                    PainScore0To10 = 0,
                    PrivateComment =
                        "Las repeticiones salieron regulares. Recuperé bien y terminé con margen.",
                    IncludeCommentInExport = true,
                },
                new JournalEntry
                {
                    Date = new DateTime(2026, 8, 12),
                    IntendedPurpose = "Descanso",
                    Fatigue1To5 = 3,
                },
            ],
        };
        return new JournalForm(
            profile,
            "activity_123456789abc",
            activities,
            document);
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Margin = new Padding(0, 0, 0, 12),
        };
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Mi diario de entrenamiento",
            Font = new Font(Font.FontFamily, 17F, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 40, 61),
            Margin = new Padding(0, 0, 0, 3),
        });
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "Escribe lo que el reloj no puede saber: sensaciones, contexto y cualquier detalle útil.",
            ForeColor = Color.FromArgb(78, 91, 108),
            Margin = new Padding(0),
        });
        return panel;
    }

    private Control BuildEditor()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Text = "Anotación",
            Padding = new Padding(14, 10, 14, 12),
            Margin = new Padding(0, 0, 0, 12),
        };

        var editor = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
        };
        group.Controls.Add(editor);

        _editorTitle.AutoSize = true;
        _editorTitle.Font = new Font(Font, FontStyle.Bold);
        _editorTitle.ForeColor = Color.FromArgb(24, 40, 61);
        _editorTitle.Margin = new Padding(0, 0, 0, 8);
        editor.Controls.Add(_editorTitle);

        var context = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            Margin = new Padding(0, 0, 0, 8),
        };
        context.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        context.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        context.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        context.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        context.Controls.Add(MakeLabel("Fecha"), 0, 0);
        context.Controls.Add(Prepare(_date), 1, 0);
        context.Controls.Add(MakeLabel("Actividad"), 2, 0);
        context.Controls.Add(Prepare(_activityPicker), 3, 0);
        editor.Controls.Add(context);

        editor.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Comentario para el informe de IA",
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 40, 61),
            Margin = new Padding(0, 2, 0, 5),
        });
        editor.Controls.Add(_comment);
        editor.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Si escribes un comentario, se incluirá automáticamente en los próximos informes para la IA.",
            ForeColor = Color.FromArgb(42, 112, 72),
            Margin = new Padding(0, 4, 0, 9),
        });

        var essentials = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            Margin = new Padding(0),
        };
        essentials.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        essentials.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        essentials.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        essentials.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        essentials.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        essentials.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        essentials.Controls.Add(MakeLabel("Objetivo de la sesión"), 0, 0);
        essentials.Controls.Add(Prepare(_purpose), 1, 0);
        essentials.Controls.Add(MakeLabel("Esfuerzo"), 2, 0);
        essentials.Controls.Add(Prepare(_effort), 3, 0);
        essentials.Controls.Add(MakeLabel("Dolor"), 4, 0);
        essentials.Controls.Add(Prepare(_pain), 5, 0);
        editor.Controls.Add(essentials);

        _advancedButton.AutoSize = true;
        _advancedButton.Anchor = AnchorStyles.Left;
        _advancedButton.FlatStyle = FlatStyle.Flat;
        _advancedButton.FlatAppearance.BorderSize = 0;
        _advancedButton.ForeColor = Color.FromArgb(38, 91, 153);
        _advancedButton.Margin = new Padding(0, 2, 0, 2);
        editor.Controls.Add(_advancedButton);

        _advancedPanel.AutoSize = true;
        _advancedPanel.Dock = DockStyle.Fill;
        _advancedPanel.Margin = new Padding(0);
        _advancedPanel.Controls.Add(BuildAdvancedFields());
        editor.Controls.Add(_advancedPanel);
        return group;
    }

    private Control BuildAdvancedFields()
    {
        var fields = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            Padding = new Padding(0, 4, 0, 0),
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        AddField(fields, 0, "Zona del dolor", _painLocation, "Fatiga de hoy", _fatigue);
        AddField(fields, 1, "Motivación", _motivation, "Estrés vital", _lifeStress);
        AddField(fields, 2, "Carbohidratos (g/h)", _carbohydrates, "Líquido (ml/h)", _fluid);
        AddField(fields, 3, "Sodio (mg/h)", _sodium, "Tolerancia digestiva", _giTolerance);
        return fields;
    }

    private Control BuildHistory()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Anotaciones guardadas",
            Padding = new Padding(10, 8, 10, 10),
            Margin = new Padding(0),
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        group.Controls.Add(layout);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Elige una anotación para editarla. También puedes hacer doble clic en la tabla.",
            ForeColor = Color.FromArgb(78, 91, 108),
            Margin = new Padding(0, 0, 0, 7),
        });

        var entryChooser = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Margin = new Padding(0, 0, 0, 8),
        };
        entryChooser.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        entryChooser.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        entryChooser.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        entryChooser.Controls.Add(MakeLabel("Editar"), 0, 0);
        _entrySelector.DropDownStyle = ComboBoxStyle.DropDownList;
        entryChooser.Controls.Add(Prepare(_entrySelector), 1, 0);
        _editButton.Text = "Abrir anotación";
        entryChooser.Controls.Add(_editButton, 2, 0);
        layout.Controls.Add(entryChooser, 0, 1);

        _recentEntries.Dock = DockStyle.Fill;
        _recentEntries.ReadOnly = true;
        _recentEntries.AllowUserToAddRows = false;
        _recentEntries.AllowUserToDeleteRows = false;
        _recentEntries.AllowUserToResizeRows = true;
        _recentEntries.MultiSelect = false;
        _recentEntries.RowHeadersVisible = false;
        _recentEntries.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _recentEntries.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders;
        _recentEntries.BackgroundColor = Color.White;
        _recentEntries.BorderStyle = BorderStyle.Fixed3D;
        _recentEntries.GridColor = Color.FromArgb(225, 229, 235);
        _recentEntries.EnableHeadersVisualStyles = false;
        _recentEntries.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 240, 247);
        _recentEntries.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(24, 40, 61);
        _recentEntries.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
        _recentEntries.ColumnHeadersHeight = 34;
        _recentEntries.RowTemplate.MinimumHeight = 34;
        _recentEntries.DefaultCellStyle.SelectionBackColor = Color.FromArgb(212, 229, 249);
        _recentEntries.DefaultCellStyle.SelectionForeColor = Color.Black;
        _recentEntries.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Date",
            HeaderText = "Fecha",
            Width = 95,
        });
        _recentEntries.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Comment",
            HeaderText = "Comentario y sensaciones",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 280,
            DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True },
        });
        _recentEntries.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Purpose",
            HeaderText = "Objetivo",
            Width = 145,
            DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True },
        });
        _recentEntries.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Effort",
            HeaderText = "Esfuerzo",
            Width = 75,
        });
        _recentEntries.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Pain",
            HeaderText = "Dolor",
            Width = 65,
        });
        _recentEntries.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Activity",
            HeaderText = "Actividad",
            Width = 210,
            DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True },
        });
        _recentEntries.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "AiReport",
            HeaderText = "Informe IA",
            Width = 90,
        });
        layout.Controls.Add(_recentEntries, 0, 2);
        return group;
    }

    private Control BuildActions()
    {
        var bar = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Padding = new Padding(0, 10, 0, 0),
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _saveStatus.AutoSize = true;
        _saveStatus.Anchor = AnchorStyles.Left;
        _saveStatus.ForeColor = Color.FromArgb(42, 112, 72);
        _saveStatus.Margin = new Padding(0, 7, 8, 0);
        bar.Controls.Add(_saveStatus, 0, 0);

        var editActions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
        };
        editActions.Controls.Add(_deleteButton);
        bar.Controls.Add(editActions, 1, 0);

        var mainActions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(8, 0, 0, 0),
        };
        var close = MakeButton("Cerrar");
        close.Click += (_, _) => Close();
        mainActions.Controls.AddRange([close, _cancelEditButton, _saveButton]);
        bar.Controls.Add(mainActions, 2, 0);
        return bar;
    }

    private void ConfigureInputs()
    {
        _date.Format = DateTimePickerFormat.Short;
        _date.MaxDate = DateTime.Today;
        _activityPicker.DropDownStyle = ComboBoxStyle.DropDownList;
        PopulateActivityPicker();
        _activityPicker.SelectedIndexChanged += (_, _) =>
        {
            if (_activityPicker.SelectedItem is ActivityChoice choice)
                _activityId.Text = choice.ActivityId;
        };
        _purpose.PlaceholderText = "Ejemplo: rodaje fácil, series o descanso";
        _painLocation.PlaceholderText = "Ejemplo: gemelo derecho";
        _carbohydrates.PlaceholderText = "Opcional";
        _fluid.PlaceholderText = "Opcional";
        _sodium.PlaceholderText = "Opcional";
        _giTolerance.DropDownStyle = ComboBoxStyle.DropDownList;
        _giTolerance.Items.AddRange(["Sin indicar", "Buena", "Regular", "Mala"]);
        _giTolerance.SelectedIndex = 0;
        _comment.Multiline = true;
        _comment.AcceptsReturn = true;
        _comment.ScrollBars = ScrollBars.Vertical;
        _comment.MinimumSize = new Size(0, 105);
        _comment.Dock = DockStyle.Fill;
        _comment.Margin = new Padding(0);
        _comment.PlaceholderText =
            "Ejemplo: hoy me encontré ligero, dormí mal, hizo mucho calor o apareció una molestia...";
    }

    private static ComboBox CreateScoreCombo(int minimum, int maximum)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.Add("Sin indicar");
        for (var value = minimum; value <= maximum; value++)
            combo.Items.Add(value.ToString());
        combo.SelectedIndex = 0;
        return combo;
    }

    private static void AddField(
        TableLayoutPanel panel,
        int row,
        string leftLabel,
        Control leftControl,
        string rightLabel,
        Control rightControl)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(MakeLabel(leftLabel), 0, row);
        panel.Controls.Add(Prepare(leftControl), 1, row);
        panel.Controls.Add(MakeLabel(rightLabel), 2, row);
        panel.Controls.Add(Prepare(rightControl), 3, row);
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 7, 8, 6),
    };

    private static Control Prepare(Control control)
    {
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 3, 14, 6);
        return control;
    }

    private static Button MakeButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Padding = new Padding(12, 5, 12, 5),
        Margin = new Padding(6, 0, 0, 0),
    };

    private void SetAdvancedVisible(bool visible)
    {
        _advancedPanel.Visible = visible;
        _advancedButton.Text = visible
            ? "Ocultar datos opcionales  ▴"
            : "Más datos opcionales  ▾";
    }

    private void SaveEntry()
    {
        if (!IsPrivateActivityReferenceOrEmpty(_activityId.Text))
        {
            MessageBox.Show(
                this,
                "La referencia privada de actividad no es válida. Vuelve a elegir la actividad desde la ventana principal.",
                "Protección de privacidad",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (!TryOptionalInt(_carbohydrates.Text, 0, 1000, "carbohidratos", out var carbohydrates) ||
            !TryOptionalInt(_fluid.Text, 0, 5000, "líquido", out var fluid) ||
            !TryOptionalInt(_sodium.Text, 0, 10000, "sodio", out var sodium))
        {
            SetAdvancedVisible(true);
            return;
        }

        var comment = _comment.Text.Trim();
        var entry = _editingEntryId is null
            ? new JournalEntry()
            : _document.Entries.FirstOrDefault(item =>
                string.Equals(item.EntryId, _editingEntryId, StringComparison.Ordinal));
        if (entry is null)
        {
            MessageBox.Show(
                this,
                "La anotación que estabas editando ya no existe. Se ha abierto una entrada nueva.",
                "Diario",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            StartNewEntry();
            RefreshEntries();
            return;
        }

        entry.Date = _date.Value.Date;
        entry.ActivityId = _activityId.Text.Trim();
        entry.ActivityDisplayName = CurrentActivityDisplayName();
        entry.IntendedPurpose = _purpose.Text.Trim();
        entry.PainScore0To10 = GetOptionalScore(_pain);
        entry.PainLocation = _painLocation.Text.Trim();
        entry.PerceivedEffort1To10 = GetOptionalScore(_effort);
        entry.Fatigue1To5 = GetOptionalScore(_fatigue);
        entry.Motivation1To5 = GetOptionalScore(_motivation);
        entry.LifeStress1To10 = GetOptionalScore(_lifeStress);
        entry.CarbohydratesGramsPerHour = carbohydrates;
        entry.FluidMillilitresPerHour = fluid;
        entry.SodiumMilligramsPerHour = sodium;
        entry.GastrointestinalTolerance = _giTolerance.SelectedIndex <= 0 ? "" : _giTolerance.Text;
        entry.PrivateComment = comment;
        entry.IncludeCommentInExport = !string.IsNullOrWhiteSpace(comment);

        if (!HasContent(entry))
        {
            MessageBox.Show(
                this,
                "Escribe un comentario o añade al menos un dato antes de guardar.",
                "Anotación vacía",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            _comment.Focus();
            return;
        }

        var wasEditing = _editingEntryId is not null;
        if (!wasEditing)
            _document.Entries.Add(entry);
        SortAndSave();
        StartNewEntry();
        RefreshEntries();
        _saveStatus.Text = wasEditing
            ? "Cambios guardados."
            : "Anotación guardada.";
        if (!string.IsNullOrWhiteSpace(comment))
            _saveStatus.Text += " El comentario se incluirá en el informe para la IA.";
    }

    private static bool HasContent(JournalEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.IntendedPurpose) ||
        entry.PainScore0To10 is not null ||
        entry.PerceivedEffort1To10 is not null ||
        entry.Fatigue1To5 is not null ||
        entry.Motivation1To5 is not null ||
        entry.LifeStress1To10 is not null ||
        entry.CarbohydratesGramsPerHour is not null ||
        entry.FluidMillilitresPerHour is not null ||
        entry.SodiumMilligramsPerHour is not null ||
        !string.IsNullOrWhiteSpace(entry.PainLocation) ||
        !string.IsNullOrWhiteSpace(entry.GastrointestinalTolerance) ||
        !string.IsNullOrWhiteSpace(entry.PrivateComment);

    private bool TryOptionalInt(
        string text,
        int minimum,
        int maximum,
        string field,
        out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (int.TryParse(text.Trim(), out var parsed) &&
            parsed >= minimum &&
            parsed <= maximum)
        {
            value = parsed;
            return true;
        }

        MessageBox.Show(
            this,
            $"El valor de {field} debe ser un número entre {minimum} y {maximum}, o quedar vacío.",
            "Revisa el diario",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        return false;
    }

    private static int? GetOptionalScore(ComboBox combo) =>
        combo.SelectedIndex <= 0 || combo.SelectedItem is not string value
            ? null
            : int.Parse(value);

    private static void SetOptionalScore(ComboBox combo, int? value)
    {
        if (value is null)
        {
            combo.SelectedIndex = 0;
            return;
        }
        var index = combo.FindStringExact(value.Value.ToString());
        combo.SelectedIndex = index >= 0 ? index : 0;
    }

    private void StartNewEntry()
    {
        _editingEntryId = null;
        _editorTitle.Text = "Escribe una nueva anotación";
        _saveButton.Text = "Guardar anotación";
        _cancelEditButton.Visible = false;
        _date.Value = DateTime.Today;
        SetActivityReference(_initialActivityId);
        _purpose.Clear();
        SetOptionalScore(_pain, null);
        _painLocation.Clear();
        SetOptionalScore(_effort, null);
        SetOptionalScore(_fatigue, null);
        SetOptionalScore(_motivation, null);
        SetOptionalScore(_lifeStress, null);
        _carbohydrates.Clear();
        _fluid.Clear();
        _sodium.Clear();
        _giTolerance.SelectedIndex = 0;
        _comment.Clear();
        _saveStatus.Text = "";
        SetAdvancedVisible(false);
        _recentEntries.ClearSelection();
        if (_entrySelector.Items.Count > 0)
            _entrySelector.SelectedIndex = 0;
        UpdateSelectionButtons();
    }

    private void RefreshEntries()
    {
        _recentEntries.Rows.Clear();
        foreach (var entry in _document.Entries
                     .OrderByDescending(item => item.Date)
                     .ThenByDescending(item => item.CreatedAtUtc)
                     .Take(100))
        {
            var hasComment = !string.IsNullOrWhiteSpace(entry.PrivateComment);
            var rowIndex = _recentEntries.Rows.Add(
                entry.Date.ToString("dd/MM/yyyy"),
                hasComment ? entry.PrivateComment : "Sin comentario",
                entry.IntendedPurpose,
                entry.PerceivedEffort1To10?.ToString() ?? "",
                entry.PainScore0To10?.ToString() ?? "",
                ActivityDisplayName(entry),
                hasComment ? (entry.IncludeCommentInExport ? "Sí" : "No (antigua)") : "—");
            var row = _recentEntries.Rows[rowIndex];
            row.Tag = entry.EntryId;
            row.Cells[1].ToolTipText = entry.PrivateComment;
            if (!hasComment)
                row.DefaultCellStyle.ForeColor = Color.FromArgb(105, 112, 122);
            else if (!entry.IncludeCommentInExport)
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 224);
        }
        _recentEntries.ClearSelection();
        RefreshEntrySelector();
        UpdateSelectionButtons();
    }

    private void RefreshEntrySelector()
    {
        var selectedEntryId = _editingEntryId;
        _entrySelector.Items.Clear();
        _entrySelector.Items.Add(new EntryChoice("", "Selecciona una anotación guardada"));
        foreach (var entry in _document.Entries
                     .OrderByDescending(item => item.Date)
                     .ThenByDescending(item => item.CreatedAtUtc)
                     .Take(100))
        {
            var context = ActivityDisplayName(entry);
            if (string.Equals(context, "Sin actividad", StringComparison.Ordinal))
                context = entry.IntendedPurpose;
            var comment = string.IsNullOrWhiteSpace(entry.PrivateComment)
                ? "Sin comentario"
                : entry.PrivateComment.ReplaceLineEndings(" ").Trim();
            if (comment.Length > 70)
                comment = comment[..67] + "…";
            var parts = new[]
            {
                entry.Date.ToString("dd/MM/yyyy"),
                context,
                comment,
            }.Where(value => !string.IsNullOrWhiteSpace(value));
            _entrySelector.Items.Add(new EntryChoice(
                entry.EntryId,
                string.Join(" · ", parts)));
        }
        var selected = _entrySelector.Items
            .OfType<EntryChoice>()
            .FirstOrDefault(choice =>
                string.Equals(choice.EntryId, selectedEntryId, StringComparison.Ordinal));
        _entrySelector.SelectedItem = selected ?? _entrySelector.Items[0];
    }

    private void UpdateSelectionButtons()
    {
        var hasSelection = SelectedEntryId() is not null;
        _editButton.Enabled = hasSelection || SelectedEntryChoiceId() is not null;
        _deleteButton.Enabled = hasSelection;
    }

    private string? SelectedEntryChoiceId() =>
        _entrySelector.SelectedItem is EntryChoice { EntryId.Length: > 0 } choice
            ? choice.EntryId
            : null;

    private string? SelectedEntryId() =>
        _recentEntries.SelectedRows.Count > 0
            ? _recentEntries.SelectedRows[0].Tag as string
            : null;

    private void EditSelectedEntry()
    {
        var entryId = SelectedEntryChoiceId() ?? SelectedEntryId();
        var entry = _document.Entries.FirstOrDefault(item =>
            string.Equals(item.EntryId, entryId, StringComparison.Ordinal));
        if (entry is null)
        {
            MessageBox.Show(
                this,
                "Selecciona primero una anotación de la lista.",
                "Diario",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _editingEntryId = entry.EntryId;
        _editorTitle.Text = $"Editando la anotación del {entry.Date:dd/MM/yyyy}";
        _saveButton.Text = "Guardar cambios";
        _cancelEditButton.Visible = true;
        _date.Value = entry.Date.Date <= _date.MaxDate ? entry.Date.Date : _date.MaxDate;
        SetActivityReference(entry.ActivityId, entry.ActivityDisplayName);
        _purpose.Text = entry.IntendedPurpose;
        SetOptionalScore(_pain, entry.PainScore0To10);
        _painLocation.Text = entry.PainLocation;
        SetOptionalScore(_effort, entry.PerceivedEffort1To10);
        SetOptionalScore(_fatigue, entry.Fatigue1To5);
        SetOptionalScore(_motivation, entry.Motivation1To5);
        SetOptionalScore(_lifeStress, entry.LifeStress1To10);
        _carbohydrates.Text = entry.CarbohydratesGramsPerHour?.ToString() ?? "";
        _fluid.Text = entry.FluidMillilitresPerHour?.ToString() ?? "";
        _sodium.Text = entry.SodiumMilligramsPerHour?.ToString() ?? "";
        _giTolerance.SelectedIndex = Math.Max(0, _giTolerance.FindStringExact(entry.GastrointestinalTolerance));
        _comment.Text = entry.PrivateComment;
        _saveStatus.Text = !string.IsNullOrWhiteSpace(entry.PrivateComment) && !entry.IncludeCommentInExport
            ? "Esta anotación es antigua. Al guardarla, su comentario se incluirá en el informe para la IA."
            : "";
        SetAdvancedVisible(HasAdvancedContent(entry));
        SelectEntryChoice(entry.EntryId);
        _comment.Focus();
        _comment.SelectionStart = _comment.TextLength;
    }

    private void SelectEntryChoice(string entryId)
    {
        var choice = _entrySelector.Items
            .OfType<EntryChoice>()
            .FirstOrDefault(item =>
                string.Equals(item.EntryId, entryId, StringComparison.Ordinal));
        if (choice is not null)
            _entrySelector.SelectedItem = choice;
    }

    private static bool HasAdvancedContent(JournalEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.PainLocation) ||
        entry.Fatigue1To5 is not null ||
        entry.Motivation1To5 is not null ||
        entry.LifeStress1To10 is not null ||
        entry.CarbohydratesGramsPerHour is not null ||
        entry.FluidMillilitresPerHour is not null ||
        entry.SodiumMilligramsPerHour is not null ||
        !string.IsNullOrWhiteSpace(entry.GastrointestinalTolerance);

    private void PopulateActivityPicker()
    {
        _activityPicker.Items.Clear();
        _activityPicker.Items.Add(new ActivityChoice("", "Sin actividad vinculada", ""));
        foreach (var activity in _activitiesById.Values
                     .OrderByDescending(item => item.Date)
                     .ThenBy(item => item.ToShortString(), StringComparer.CurrentCultureIgnoreCase))
        {
            _activityPicker.Items.Add(new ActivityChoice(
                activity.Id,
                activity.ToString(),
                activity.ToShortString()));
        }
        _activityPicker.SelectedIndex = 0;
    }

    private void SetActivityReference(string? activityId, string? storedDisplayName = null)
    {
        var reference = activityId?.Trim() ?? "";
        var existing = _activityPicker.Items
            .OfType<ActivityChoice>()
            .FirstOrDefault(choice =>
                string.Equals(choice.ActivityId, reference, StringComparison.Ordinal));
        if (existing is null && !string.IsNullOrWhiteSpace(reference))
        {
            var display = string.IsNullOrWhiteSpace(storedDisplayName)
                ? "Actividad vinculada"
                : storedDisplayName.Trim();
            existing = new ActivityChoice(reference, display, display);
            _activityPicker.Items.Add(existing);
        }
        _activityPicker.SelectedItem = existing ?? _activityPicker.Items[0];
        _activityId.Text = reference;
    }

    private string CurrentActivityDisplayName() =>
        _activityPicker.SelectedItem is ActivityChoice choice
            ? choice.ShortDisplay
            : "";

    private string ActivityDisplayName(JournalEntry entry)
    {
        if (_activitiesById.TryGetValue(entry.ActivityId, out var activity))
            return activity.ToShortString();
        if (!string.IsNullOrWhiteSpace(entry.ActivityDisplayName))
            return entry.ActivityDisplayName;
        return string.IsNullOrWhiteSpace(entry.ActivityId)
            ? "Sin actividad"
            : "Actividad vinculada";
    }

    private void DeleteSelectedEntry()
    {
        var entryId = SelectedEntryId();
        if (entryId is null)
        {
            MessageBox.Show(
                this,
                "Selecciona primero una anotación de la lista.",
                "Diario",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(
                this,
                "¿Quieres eliminar únicamente esta anotación del diario?",
                "Confirmar eliminación",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        _document.Entries.RemoveAll(entry =>
            string.Equals(entry.EntryId, entryId, StringComparison.Ordinal));
        SortAndSave();
        if (string.Equals(_editingEntryId, entryId, StringComparison.Ordinal))
            StartNewEntry();
        RefreshEntries();
        _saveStatus.Text = "Anotación eliminada.";
    }

    private void SortAndSave()
    {
        _document.Entries = _document.Entries
            .OrderBy(item => item.Date)
            .ThenBy(item => item.CreatedAtUtc)
            .ToList();
        if (!_isPreview)
            AtomicJsonStore.Write(_path, _document);
    }

    private static bool IsPrivateActivityReferenceOrEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        Regex.IsMatch(value.Trim(), @"\Aactivity_[0-9a-f]{12}\z");

    private sealed record ActivityChoice(
        string ActivityId,
        string Display,
        string ShortDisplay)
    {
        public override string ToString() => Display;
    }

    private sealed record EntryChoice(string EntryId, string Display)
    {
        public override string ToString() => Display;
    }
}

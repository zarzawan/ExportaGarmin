using System.Globalization;
using GarminDataExport.Launcher.Models;
using GarminDataExport.Launcher.Services;

namespace GarminDataExport.Launcher.Views;

internal sealed class RaceContextForm : Form
{
    private static readonly (string Code, string Text, decimal? Distance)[] RaceTypes =
    [
        ("marathon", "Maratón (42,195 km)", 42.195m),
        ("half_marathon", "Media maratón (21,097 km)", 21.097m),
        ("ten_k", "10 km", 10m),
        ("five_k", "5 km", 5m),
        ("custom", "Otra distancia", null),
    ];

    private static readonly (string Code, string Text)[] GoalTypes =
    [
        ("finish", "Terminar con buenas sensaciones"),
        ("target_time", "Conseguir un tiempo objetivo"),
        ("personal_best", "Mejorar mi marca personal"),
        ("training", "Completarla como entrenamiento"),
    ];

    private readonly string _path;
    private readonly TextBox _raceName = new();
    private readonly ComboBox _raceType = new();
    private readonly NumericUpDown _distance = new();
    private readonly DateTimePicker _raceDate = new();
    private readonly ComboBox _goalType = new();
    private readonly TextBox _targetTime = new();
    private readonly TextBox _experience = new();
    private readonly NumericUpDown _availableDays = new();
    private readonly CheckBox _availableDaysSpecified = new();
    private readonly ComboBox _longRunDay = new();
    private readonly NumericUpDown _strengthDays = new();
    private readonly CheckBox _strengthDaysSpecified = new();
    private readonly NumericUpDown _weeklyMinutes = new();
    private readonly CheckBox _weeklyMinutesSpecified = new();
    private readonly TextBox _terrain = new();
    private readonly TextBox _climate = new();
    private readonly TextBox _recentPerformance = new();
    private readonly TextBox _constraints = new();

    public RaceContextForm(UserProfile profile)
    {
        _path = AppPaths.RaceContextFile(profile);
        Text = $"Mi carrera — {profile.Alias}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 690);
        Size = new Size(790, 790);
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(720, 0),
            Text = "Estos datos ayudan a la IA a interpretar tu entrenamiento según la prueba que preparas. " +
                   "Son opcionales: se guardan en tu perfil y se copian a los archivos que tú crees. " +
                   "El programa no los sube automáticamente.",
            Margin = new Padding(0, 0, 0, 14),
        });

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 2,
            Padding = new Padding(0, 4, 8, 4),
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(fields, 0, 1);

        ConfigureControls();
        AddRow(fields, "Nombre de la carrera (opcional)", _raceName);
        AddRow(fields, "Distancia", _raceType);
        AddRow(fields, "Kilómetros", _distance);
        AddRow(fields, "Fecha de la carrera", _raceDate);
        AddRow(fields, "Objetivo", _goalType);
        AddRow(fields, "Tiempo objetivo (h:mm:ss)", _targetTime);
        AddRow(fields, "Experiencia corriendo", _experience);
        AddRow(
            fields,
            "Días disponibles por semana",
            MakeOptionalNumber(
                _availableDays,
                _availableDaysSpecified,
                "días"));
        AddRow(fields, "Día preferido de tirada larga", _longRunDay);
        AddRow(
            fields,
            "Días de fuerza por semana",
            MakeOptionalNumber(
                _strengthDays,
                _strengthDaysSpecified,
                "días"));
        AddRow(
            fields,
            "Minutos disponibles por semana",
            MakeOptionalNumber(
                _weeklyMinutes,
                _weeklyMinutesSpecified,
                "minutos"));
        AddRow(fields, "Terreno habitual", _terrain);
        AddRow(fields, "Clima esperado", _climate);
        AddRow(fields, "Marca o carrera reciente", _recentPerformance);
        AddRow(fields, "Limitaciones que la IA debe respetar", _constraints, 86);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 12, 0, 0),
        };
        var save = MakeButton("Guardar");
        save.Font = new Font(save.Font, FontStyle.Bold);
        save.Click += (_, _) => SaveContext();
        var cancel = MakeButton("Cancelar");
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.AddRange([save, cancel]);
        root.Controls.Add(buttons, 0, 2);
        CancelButton = cancel;

        LoadContext();
    }

    private void ConfigureControls()
    {
        foreach (var (_, text, _) in RaceTypes)
            _raceType.Items.Add(text);
        _raceType.DropDownStyle = ComboBoxStyle.DropDownList;
        _raceType.SelectedIndexChanged += (_, _) =>
        {
            if (_raceType.SelectedIndex < 0)
                return;
            var fixedDistance = RaceTypes[_raceType.SelectedIndex].Distance;
            _distance.Enabled = fixedDistance is null;
            if (fixedDistance is not null)
                _distance.Value = fixedDistance.Value;
        };

        _distance.DecimalPlaces = 3;
        _distance.Increment = 0.1m;
        _distance.Minimum = 1;
        _distance.Maximum = 500;

        _raceDate.Format = DateTimePickerFormat.Long;
        _raceDate.ShowCheckBox = true;
        _raceDate.MinDate = DateTime.Today.AddYears(-1);
        _raceDate.MaxDate = DateTime.Today.AddYears(10);

        foreach (var (_, text) in GoalTypes)
            _goalType.Items.Add(text);
        _goalType.DropDownStyle = ComboBoxStyle.DropDownList;

        _availableDays.Minimum = 0;
        _availableDays.Maximum = 7;
        _strengthDays.Minimum = 0;
        _strengthDays.Maximum = 7;
        _weeklyMinutes.Minimum = 0;
        _weeklyMinutes.Maximum = 5000;
        _weeklyMinutes.Increment = 30;
        ConfigureOptionalNumber(_availableDays, _availableDaysSpecified);
        ConfigureOptionalNumber(_strengthDays, _strengthDaysSpecified);
        ConfigureOptionalNumber(_weeklyMinutes, _weeklyMinutesSpecified);

        _longRunDay.DropDownStyle = ComboBoxStyle.DropDownList;
        _longRunDay.Items.AddRange(
            ["Sin indicar", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo"]);

        _targetTime.PlaceholderText = "Ejemplo: 3:45:00";
        _experience.PlaceholderText = "Ejemplo: 2 años; primera maratón";
        _terrain.PlaceholderText = "Ejemplo: asfalto, con algunas cuestas";
        _climate.PlaceholderText = "Ejemplo: templado y húmedo";
        _recentPerformance.PlaceholderText = "Ejemplo: media maratón 1:45 en mayo";
        _constraints.Multiline = true;
        _constraints.ScrollBars = ScrollBars.Vertical;
        _constraints.PlaceholderText =
            "Opcional. Ejemplo: días en los que no puedes entrenar o indicaciones de un profesional.";
    }

    private static void ConfigureOptionalNumber(
        NumericUpDown number,
        CheckBox specified)
    {
        specified.AutoSize = true;
        specified.Text = "Indicar";
        specified.CheckedChanged += (_, _) =>
        {
            number.Enabled = specified.Checked;
        };
        number.Enabled = false;
    }

    private static Control MakeOptionalNumber(
        NumericUpDown number,
        CheckBox specified,
        string unit)
    {
        number.Width = 95;
        var unitLabel = new Label
        {
            AutoSize = true,
            Text = unit,
            Margin = new Padding(4, 7, 0, 0),
        };
        return new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Controls = { specified, number, unitLabel },
        };
    }

    private static void AddRow(
        TableLayoutPanel panel,
        string label,
        Control control,
        int height = 38)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        panel.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 10, 0),
        }, 0, row);
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 2, 4, 6);
        panel.Controls.Add(control, 1, row);
    }

    private static Button MakeButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Padding = new Padding(12, 5, 12, 5),
        Margin = new Padding(6, 0, 0, 0),
    };

    private void LoadContext()
    {
        var context = AtomicJsonStore.Read<RaceContext>(_path) ?? new RaceContext();
        _raceName.Text = context.RaceName;
        _raceType.SelectedIndex = Math.Max(
            0,
            Array.FindIndex(RaceTypes, option => option.Code == context.RaceType));
        if (context.DistanceKm is > 0)
            _distance.Value = Math.Clamp((decimal)context.DistanceKm.Value, _distance.Minimum, _distance.Maximum);
        _raceDate.Checked = context.RaceDate.HasValue;
        var loadedRaceDate = context.RaceDate is { } date
            ? DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified)
            : DateTime.Today;
        _raceDate.Value = loadedRaceDate < _raceDate.MinDate
            ? _raceDate.MinDate
            : loadedRaceDate > _raceDate.MaxDate
                ? _raceDate.MaxDate
                : loadedRaceDate;
        _goalType.SelectedIndex = Math.Max(
            0,
            Array.FindIndex(GoalTypes, option => option.Code == context.GoalType));
        _targetTime.Text = context.TargetTimeSeconds is { } seconds
            ? TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : "";
        _experience.Text = context.Experience;
        _availableDaysSpecified.Checked =
            context.AvailableDaysPerWeek.HasValue;
        _availableDays.Value = context.AvailableDaysPerWeek ?? 0;
        _longRunDay.SelectedItem = string.IsNullOrWhiteSpace(context.LongRunDay)
            ? "Sin indicar"
            : context.LongRunDay;
        if (_longRunDay.SelectedIndex < 0)
            _longRunDay.SelectedIndex = 0;
        _strengthDaysSpecified.Checked =
            context.StrengthDaysPerWeek.HasValue;
        _strengthDays.Value = context.StrengthDaysPerWeek ?? 0;
        _weeklyMinutesSpecified.Checked =
            context.AvailableMinutesPerWeek.HasValue;
        _weeklyMinutes.Value = context.AvailableMinutesPerWeek ?? 0;
        _terrain.Text = context.Terrain;
        _climate.Text = context.ExpectedClimate;
        _recentPerformance.Text = context.RecentPerformance;
        _constraints.Text = context.TrainingConstraints;
    }

    private void SaveContext()
    {
        int? targetSeconds = null;
        if (!string.IsNullOrWhiteSpace(_targetTime.Text))
        {
            if (!TimeSpan.TryParse(_targetTime.Text.Trim(), CultureInfo.InvariantCulture, out var target) ||
                target <= TimeSpan.Zero ||
                target.TotalHours >= 24)
            {
                MessageBox.Show(
                    this,
                    "El tiempo objetivo debe escribirse como horas:minutos:segundos. Ejemplo: 3:45:00.",
                    "Revisa el tiempo",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            targetSeconds = checked((int)target.TotalSeconds);
        }

        var selectedRace = RaceTypes[Math.Max(0, _raceType.SelectedIndex)];
        var selectedGoal = GoalTypes[Math.Max(0, _goalType.SelectedIndex)];
        var context = new RaceContext
        {
            RaceName = _raceName.Text.Trim(),
            RaceType = selectedRace.Code,
            DistanceKm = (double)_distance.Value,
            RaceDate = _raceDate.Checked ? _raceDate.Value.Date : null,
            GoalType = selectedGoal.Code,
            TargetTimeSeconds = targetSeconds,
            Experience = _experience.Text.Trim(),
            AvailableDaysPerWeek = _availableDaysSpecified.Checked
                ? (int)_availableDays.Value
                : null,
            LongRunDay = _longRunDay.SelectedIndex <= 0 ? "" : _longRunDay.Text,
            StrengthDaysPerWeek = _strengthDaysSpecified.Checked
                ? (int)_strengthDays.Value
                : null,
            AvailableMinutesPerWeek = _weeklyMinutesSpecified.Checked
                ? (int)_weeklyMinutes.Value
                : null,
            Terrain = _terrain.Text.Trim(),
            ExpectedClimate = _climate.Text.Trim(),
            RecentPerformance = _recentPerformance.Text.Trim(),
            TrainingConstraints = _constraints.Text.Trim(),
            UpdatedAtUtc = DateTime.UtcNow,
        };
        AtomicJsonStore.Write(_path, context);
        DialogResult = DialogResult.OK;
        Close();
    }
}

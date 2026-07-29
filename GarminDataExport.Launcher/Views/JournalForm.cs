using GarminDataExport.Launcher.Models;
using GarminDataExport.Launcher.Services;
using System.Text.RegularExpressions;

namespace GarminDataExport.Launcher.Views;

internal sealed class JournalForm : Form
{
    private readonly string _path;
    private JournalDocument _document;
    private readonly DateTimePicker _date = new();
    private readonly TextBox _activityId = new();
    private readonly TextBox _purpose = new();
    private readonly TextBox _pain = new();
    private readonly TextBox _painLocation = new();
    private readonly TextBox _effort = new();
    private readonly TextBox _fatigue = new();
    private readonly TextBox _motivation = new();
    private readonly TextBox _lifeStress = new();
    private readonly TextBox _carbohydrates = new();
    private readonly TextBox _fluid = new();
    private readonly TextBox _sodium = new();
    private readonly ComboBox _giTolerance = new();
    private readonly TextBox _comment = new();
    private readonly CheckBox _includeComment = new();
    private readonly ListView _recentEntries = new();

    public JournalForm(UserProfile profile, string? activityId = null)
    {
        _path = AppPaths.JournalFile(profile);
        _document = AtomicJsonStore.Read<JournalDocument>(_path) ?? new JournalDocument();

        Text = $"Mi diario opcional — {profile.Alias}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(830, 690);
        Size = new Size(900, 780);
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(830, 0),
            Text = "Añade solo lo que el reloj no conoce. Todo es opcional. El comentario se queda " +
                   "en este PC salvo que marques expresamente «Incluir comentario en el archivo».",
            Margin = new Padding(0, 0, 0, 12),
        });

        var fields = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 4,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.Controls.Add(fields);

        _date.Format = DateTimePickerFormat.Short;
        _date.MaxDate = DateTime.Today;
        _activityId.Text = activityId ?? "";
        _activityId.PlaceholderText = "Se rellena al elegir una actividad";
        _purpose.PlaceholderText = "Ejemplo: rodaje fácil";
        _pain.PlaceholderText = "Vacío o 0–10";
        _effort.PlaceholderText = "Vacío o 1–10";
        _fatigue.PlaceholderText = "Vacío o 1–5";
        _motivation.PlaceholderText = "Vacío o 1–5";
        _lifeStress.PlaceholderText = "Vacío o 1–10";
        _carbohydrates.PlaceholderText = "g/h (opcional)";
        _fluid.PlaceholderText = "ml/h (opcional)";
        _sodium.PlaceholderText = "mg/h (opcional)";
        _giTolerance.DropDownStyle = ComboBoxStyle.DropDownList;
        _giTolerance.Items.AddRange(["Sin indicar", "Buena", "Regular", "Mala"]);
        _giTolerance.SelectedIndex = 0;
        _comment.Multiline = true;
        _comment.Height = 58;
        _includeComment.AutoSize = true;
        _includeComment.Text = "Incluir comentario en el archivo para la IA";

        AddField(fields, 0, "Fecha", _date, "Referencia privada de actividad", _activityId);
        AddField(fields, 1, "Objetivo de la sesión", _purpose, "Esfuerzo percibido", _effort);
        AddField(fields, 2, "Dolor (0–10)", _pain, "Zona del dolor", _painLocation);
        AddField(fields, 3, "Carbohidratos", _carbohydrates, "Líquido", _fluid);
        AddField(fields, 4, "Sodio", _sodium, "Tolerancia digestiva", _giTolerance);
        AddField(fields, 5, "Fatiga de hoy (1–5)", _fatigue, "Motivación (1–5)", _motivation);
        AddField(fields, 6, "Estrés vital (1–10)", _lifeStress, "Comentario privado", _comment);
        AddField(fields, 7, "", new Panel(), "", _includeComment);

        _recentEntries.Dock = DockStyle.Fill;
        _recentEntries.View = View.Details;
        _recentEntries.FullRowSelect = true;
        _recentEntries.Columns.Add("Fecha", 100);
        _recentEntries.Columns.Add("Objetivo", 230);
        _recentEntries.Columns.Add("Esfuerzo", 80);
        _recentEntries.Columns.Add("Dolor", 70);
        _recentEntries.Columns.Add("Referencia privada", 180);
        root.Controls.Add(_recentEntries);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 0, 0),
        };
        var close = MakeButton("Cerrar");
        close.Click += (_, _) => Close();
        var delete = MakeButton("Eliminar seleccionada");
        delete.Click += (_, _) => DeleteSelectedEntry();
        var add = MakeButton("Añadir entrada");
        add.Font = new Font(add.Font, FontStyle.Bold);
        add.Click += (_, _) => AddEntry();
        buttons.Controls.AddRange([close, delete, add]);
        root.Controls.Add(buttons);
        RefreshEntries();
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
        control.Margin = new Padding(0, 3, 12, 6);
        return control;
    }

    private static Button MakeButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Padding = new Padding(12, 5, 12, 5),
        Margin = new Padding(6, 0, 0, 0),
    };

    private void AddEntry()
    {
        if (!IsPrivateActivityReferenceOrEmpty(_activityId.Text))
        {
            MessageBox.Show(
                this,
                "La referencia de actividad no es válida. No escribas aquí un ID de Garmin. " +
                "Usa el botón para elegir una actividad y el programa la rellenará de forma privada.",
                "Protección de privacidad",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (!TryOptionalInt(_pain.Text, 0, 10, "dolor", out var pain) ||
            !TryOptionalInt(_effort.Text, 1, 10, "esfuerzo", out var effort) ||
            !TryOptionalInt(_fatigue.Text, 1, 5, "fatiga", out var fatigue) ||
            !TryOptionalInt(_motivation.Text, 1, 5, "motivación", out var motivation) ||
            !TryOptionalInt(_lifeStress.Text, 1, 10, "estrés vital", out var lifeStress) ||
            !TryOptionalInt(_carbohydrates.Text, 0, 1000, "carbohidratos", out var carbohydrates) ||
            !TryOptionalInt(_fluid.Text, 0, 5000, "líquido", out var fluid) ||
            !TryOptionalInt(_sodium.Text, 0, 10000, "sodio", out var sodium))
        {
            return;
        }

        var entry = new JournalEntry
        {
            Date = _date.Value.Date,
            ActivityId = _activityId.Text.Trim(),
            IntendedPurpose = _purpose.Text.Trim(),
            PainScore0To10 = pain,
            PainLocation = _painLocation.Text.Trim(),
            PerceivedEffort1To10 = effort,
            Fatigue1To5 = fatigue,
            Motivation1To5 = motivation,
            LifeStress1To10 = lifeStress,
            CarbohydratesGramsPerHour = carbohydrates,
            FluidMillilitresPerHour = fluid,
            SodiumMilligramsPerHour = sodium,
            GastrointestinalTolerance = _giTolerance.SelectedIndex <= 0 ? "" : _giTolerance.Text,
            PrivateComment = _comment.Text.Trim(),
            IncludeCommentInExport = _includeComment.Checked,
        };

        if (string.IsNullOrWhiteSpace(entry.IntendedPurpose) &&
            entry.PainScore0To10 is null &&
            entry.PerceivedEffort1To10 is null &&
            entry.Fatigue1To5 is null &&
            entry.Motivation1To5 is null &&
            entry.LifeStress1To10 is null &&
            entry.CarbohydratesGramsPerHour is null &&
            entry.FluidMillilitresPerHour is null &&
            entry.SodiumMilligramsPerHour is null &&
            string.IsNullOrWhiteSpace(entry.PainLocation) &&
            string.IsNullOrWhiteSpace(entry.GastrointestinalTolerance) &&
            string.IsNullOrWhiteSpace(entry.PrivateComment))
        {
            MessageBox.Show(
                this,
                "Añade al menos un dato antes de guardar la entrada.",
                "Entrada vacía",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _document.Entries.Add(entry);
        _document.Entries = _document.Entries
            .OrderBy(item => item.Date)
            .ThenBy(item => item.CreatedAtUtc)
            .ToList();
        AtomicJsonStore.Write(_path, _document);
        ClearEntryFields();
        RefreshEntries();
    }

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

    private void ClearEntryFields()
    {
        _purpose.Clear();
        _pain.Clear();
        _painLocation.Clear();
        _effort.Clear();
        _fatigue.Clear();
        _motivation.Clear();
        _lifeStress.Clear();
        _carbohydrates.Clear();
        _fluid.Clear();
        _sodium.Clear();
        _giTolerance.SelectedIndex = 0;
        _comment.Clear();
        _includeComment.Checked = false;
    }

    private void RefreshEntries()
    {
        _recentEntries.BeginUpdate();
        _recentEntries.Items.Clear();
        foreach (var entry in _document.Entries
                     .OrderByDescending(item => item.Date)
                     .ThenByDescending(item => item.CreatedAtUtc)
                     .Take(100))
        {
            var item = new ListViewItem(entry.Date.ToString("yyyy-MM-dd"));
            item.SubItems.Add(entry.IntendedPurpose);
            item.SubItems.Add(entry.PerceivedEffort1To10?.ToString() ?? "");
            item.SubItems.Add(entry.PainScore0To10?.ToString() ?? "");
            item.SubItems.Add(entry.ActivityId);
            item.Tag = entry.EntryId;
            _recentEntries.Items.Add(item);
        }
        _recentEntries.EndUpdate();
    }

    private void DeleteSelectedEntry()
    {
        if (_recentEntries.SelectedItems.Count == 0 ||
            _recentEntries.SelectedItems[0].Tag is not string entryId)
        {
            MessageBox.Show(
                this,
                "Selecciona primero una entrada del diario.",
                "Diario",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(
                this,
                "¿Quieres eliminar únicamente esta entrada del diario?",
                "Confirmar eliminación",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        _document.Entries.RemoveAll(entry =>
            string.Equals(entry.EntryId, entryId, StringComparison.Ordinal));
        AtomicJsonStore.Write(_path, _document);
        RefreshEntries();
    }

    private static bool IsPrivateActivityReferenceOrEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        Regex.IsMatch(value.Trim(), @"\Aactivity_[0-9a-f]{12}\z");
}

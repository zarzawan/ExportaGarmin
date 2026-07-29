using GarminDataExport.Launcher.Models;

namespace GarminDataExport.Launcher.Views;

internal sealed class ActivityPickerForm : Form
{
    private readonly ListBox _activities = new();

    public ActivityPickerForm(IReadOnlyList<RecentActivity> activities)
    {
        Text = "Elige una actividad";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(650, 430);
        Size = new Size(740, 520);
        Font = new Font("Segoe UI", 10F);

        var label = new Label
        {
            Dock = DockStyle.Top,
            Height = 58,
            Padding = new Padding(16, 16, 16, 6),
            Text = "Elige la sesión que quieres estudiar con el máximo detalle disponible.",
        };
        _activities.Dock = DockStyle.Fill;
        foreach (var activity in activities)
            _activities.Items.Add(activity);
        if (_activities.Items.Count > 0)
            _activities.SelectedIndex = 0;
        _activities.DoubleClick += (_, _) => SelectCurrent();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 10, 16, 10),
        };
        var select = new Button
        {
            Text = "Elegir",
            AutoSize = true,
            Padding = new Padding(12, 4, 12, 4),
        };
        select.Click += (_, _) => SelectCurrent();
        var cancel = new Button
        {
            Text = "Cancelar",
            AutoSize = true,
            Padding = new Padding(12, 4, 12, 4),
            DialogResult = DialogResult.Cancel,
        };
        buttons.Controls.AddRange([select, cancel]);

        Controls.Add(_activities);
        Controls.Add(label);
        Controls.Add(buttons);
        CancelButton = cancel;
    }

    public RecentActivity? SelectedActivity { get; private set; }

    private void SelectCurrent()
    {
        if (_activities.SelectedItem is not RecentActivity activity)
            return;
        SelectedActivity = activity;
        DialogResult = DialogResult.OK;
        Close();
    }
}

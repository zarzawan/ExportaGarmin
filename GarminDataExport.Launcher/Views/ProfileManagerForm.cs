using GarminDataExport.Launcher.Models;
using GarminDataExport.Launcher.Services;

namespace GarminDataExport.Launcher.Views;

internal sealed class ProfileManagerForm : Form
{
    private readonly ProfileStore _store;
    private readonly ListBox _profiles = new();

    public ProfileManagerForm(ProfileStore store, UserProfile? selected)
    {
        _store = store;
        SelectedProfile = selected;
        Text = "Perfiles de este ordenador";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(580, 390);
        Size = new Size(650, 440);
        Font = new Font("Segoe UI", 10F);

        var explanation = new Label
        {
            Dock = DockStyle.Top,
            Height = 72,
            Padding = new Padding(16, 14, 16, 4),
            Text = "Cada perfil mantiene separados la sesión, la caché, el contexto de carrera, " +
                   "el diario y los archivos. No se guardan correos ni contraseñas.",
        };
        _profiles.Dock = DockStyle.Fill;
        _profiles.Margin = new Padding(16);
        _profiles.DoubleClick += (_, _) => SelectAndClose();

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 68,
            Padding = new Padding(14, 10, 14, 10),
            FlowDirection = FlowDirection.LeftToRight,
        };
        var add = MakeButton("Añadir persona");
        add.Click += (_, _) => AddProfile();
        var rename = MakeButton("Cambiar nombre");
        rename.Click += (_, _) => RenameProfile();
        var choose = MakeButton("Usar este perfil");
        choose.Font = new Font(choose.Font, FontStyle.Bold);
        choose.Click += (_, _) => SelectAndClose();
        var cancel = MakeButton("Cerrar");
        cancel.Click += (_, _) => Close();
        actions.Controls.AddRange([add, rename, choose, cancel]);

        Controls.Add(_profiles);
        Controls.Add(explanation);
        Controls.Add(actions);
        RefreshProfiles();
    }

    public UserProfile? SelectedProfile { get; private set; }

    private static Button MakeButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Padding = new Padding(8, 4, 8, 4),
        Margin = new Padding(4),
    };

    private void RefreshProfiles(string? selectedId = null)
    {
        selectedId ??= (_profiles.SelectedItem as UserProfile)?.Id
                       ?? SelectedProfile?.Id;
        _profiles.BeginUpdate();
        _profiles.Items.Clear();
        foreach (var profile in _store.Profiles)
            _profiles.Items.Add(profile);
        _profiles.EndUpdate();
        _profiles.SelectedItem = _store.Find(selectedId) ?? _store.Profiles.FirstOrDefault();
    }

    private void AddProfile()
    {
        using var dialog = new TextPromptDialog(
            "Añadir persona",
            "Escribe un nombre sencillo, por ejemplo «Ana»:");
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            SelectedProfile = _store.Create(dialog.Value);
            RefreshProfiles(SelectedProfile.Id);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "Nombre no válido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RenameProfile()
    {
        if (_profiles.SelectedItem is not UserProfile profile)
            return;
        using var dialog = new TextPromptDialog(
            "Cambiar nombre",
            "Nuevo nombre visible del perfil:",
            profile.Alias);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            _store.Rename(profile, dialog.Value);
            RefreshProfiles(profile.Id);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "Nombre no válido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SelectAndClose()
    {
        if (_profiles.SelectedItem is not UserProfile profile)
            return;
        SelectedProfile = profile;
        DialogResult = DialogResult.OK;
        Close();
    }
}

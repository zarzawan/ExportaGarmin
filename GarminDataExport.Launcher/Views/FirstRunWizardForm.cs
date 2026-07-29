using GarminDataExport.Launcher.Models;
using GarminDataExport.Launcher.Services;

namespace GarminDataExport.Launcher.Views;

internal sealed class FirstRunWizardForm : Form
{
    private readonly ProfileStore _profiles;
    private readonly string? _projectRoot;
    private readonly TabControl _steps = new();
    private readonly ComboBox _profileCombo = new();
    private readonly Label _sessionStatus = new();
    private readonly Button _previous = new();
    private readonly Button _next = new();
    private bool _sessionOperationRunning;

    public FirstRunWizardForm(
        ProfileStore profiles,
        string? projectRoot,
        UserProfile? selectedProfile)
    {
        _profiles = profiles;
        _projectRoot = projectRoot;
        SelectedProfile = selectedProfile ?? profiles.Profiles.FirstOrDefault();

        Text = "Primeros pasos";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(750, 570);
        Font = new Font("Segoe UI", 10F);

        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = 64,
            Padding = new Padding(22, 17, 16, 8),
            Text = "Asistente de EntrenaIA",
            Font = new Font("Segoe UI", 17F, FontStyle.Bold),
        };
        Controls.Add(title);

        _steps.Dock = DockStyle.Fill;
        _steps.Appearance = TabAppearance.FlatButtons;
        _steps.ItemSize = new Size(0, 1);
        _steps.SizeMode = TabSizeMode.Fixed;
        _steps.TabPages.Add(BuildWelcomePage());
        _steps.TabPages.Add(BuildProfilePage());
        _steps.TabPages.Add(BuildRacePage());
        _steps.TabPages.Add(BuildRoutinePage());
        _steps.SelectedIndexChanged += (_, _) => UpdateNavigation();
        Controls.Add(_steps);

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 62,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(16, 10, 18, 10),
        };
        _next.Text = "Siguiente";
        _next.AutoSize = true;
        _next.Padding = new Padding(15, 5, 15, 5);
        _next.Font = new Font(_next.Font, FontStyle.Bold);
        _next.Click += (_, _) => NextStep();
        _previous.Text = "Atrás";
        _previous.AutoSize = true;
        _previous.Padding = new Padding(15, 5, 15, 5);
        _previous.Click += (_, _) => _steps.SelectedIndex--;
        navigation.Controls.AddRange([_next, _previous]);
        Controls.Add(navigation);

        RefreshProfiles();
        UpdateNavigation();
        FormClosing += (_, eventArgs) =>
        {
            if (!SessionLoginLauncher.IsRunning &&
                !_sessionOperationRunning)
                return;
            MessageBox.Show(
                this,
                SessionLoginLauncher.IsRunning
                    ? "Termina o cierra primero la ventana negra de inicio de sesión."
                    : "Espera a que termine la comprobación de la sesión.",
                "Operación de sesión en curso",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            eventArgs.Cancel = true;
        };
    }

    public UserProfile? SelectedProfile { get; private set; }

    private TabPage BuildWelcomePage()
    {
        var page = NewPage();
        page.Controls.Add(MakeContent(
            "Bienvenido",
            """
            Este programa crea un archivo ordenado con tus datos de Garmin para que puedas subirlo manualmente a ChatGPT u otra IA.

            Se conecta a Garmin Connect para descargar tus datos. No sube el archivo a ChatGPT ni a ninguna otra IA: tú decides si lo compartes y con quién.

            Dispondrás de tres acciones sencillas:

            • Revisión recomendada: seguimiento semanal de tu preparación.
            • Analizar una actividad: máxima atención a una sesión concreta.
            • Archivo histórico: un intervalo elegido por ti.

            La IA puede ayudarte a entender tendencias, pero no sustituye a un entrenador ni a un profesional sanitario.
            """));
        return page;
    }

    private TabPage BuildProfilePage()
    {
        var page = NewPage();
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(24),
        };
        root.Controls.Add(new Label
        {
            Text = "1. ¿Quién va a usar Garmin?",
            AutoSize = true,
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 12),
        });
        root.Controls.Add(new Label
        {
            Text = "Cada persona tiene sus propios archivos y sesión. Un perfil no guarda el correo ni la contraseña.",
            AutoSize = true,
            MaximumSize = new Size(660, 0),
            Margin = new Padding(0, 0, 0, 14),
        });

        var profileLine = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false,
        };
        _profileCombo.Width = 350;
        _profileCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _profileCombo.SelectedIndexChanged += (_, _) =>
        {
            SelectedProfile = _profileCombo.SelectedItem as UserProfile;
            RefreshSessionStatus();
        };
        profileLine.Controls.Add(_profileCombo);
        var addProfile = MakeButton("Añadir persona");
        addProfile.Click += (_, _) => AddProfile();
        profileLine.Controls.Add(addProfile);
        root.Controls.Add(profileLine);

        _sessionStatus.AutoSize = true;
        _sessionStatus.Font = new Font(_sessionStatus.Font, FontStyle.Bold);
        _sessionStatus.Margin = new Padding(0, 20, 0, 8);
        root.Controls.Add(_sessionStatus);

        var sessionActions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
        };
        var login = MakeButton("Iniciar sesión en Garmin");
        login.Click += async (_, _) => await StartLoginAsync();
        login.Enabled = _projectRoot is not null;
        var check = MakeButton("Comprobar de nuevo");
        check.Click += async (_, _) => await CheckSessionAsync();
        sessionActions.Controls.AddRange([login, check]);
        root.Controls.Add(sessionActions);
        root.Controls.Add(new Label
        {
            Text = "Las credenciales y el MFA se escriben únicamente en la ventana negra que se abre.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 10, 0, 0),
        });
        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildRacePage()
    {
        var page = NewPage();
        var content = MakeContent(
            "2. Describe tu objetivo",
            """
            Indica la carrera, la fecha y tus límites de tiempo. Así la IA no analizará una maratón como si fuera una carrera de 5 km.

            No necesitas rellenarlo todo. Puedes modificarlo en cualquier momento desde «Mi carrera».

            Esta información se guarda en tu perfil local y se etiqueta como aportada por el usuario.
            """);
        var configure = MakeButton("Configurar mi carrera");
        configure.Location = new Point(28, 290);
        configure.Click += (_, _) =>
        {
            if (SelectedProfile is null)
                return;
            using var form = new RaceContextForm(SelectedProfile);
            form.ShowDialog(this);
        };
        page.Controls.Add(content);
        page.Controls.Add(configure);
        return page;
    }

    private TabPage BuildRoutinePage()
    {
        var page = NewPage();
        page.Controls.Add(MakeContent(
            "3. Una rutina muy sencilla",
            """
            Cada día
            Sincroniza el reloj y completa la autoevaluación de Garmin. El diario del programa es opcional.

            Cada semana
            Crea la «Revisión recomendada», sube ese único archivo a tu IA y pega la pregunta que prepara el programa.

            Cada mes
            Pide una revisión más estratégica del bloque, la tirada larga, la constancia y las semanas que faltan.

            Encontrarás esta explicación en el botón «Guía sencilla». Ya está todo preparado.
            """));
        return page;
    }

    private static TabPage NewPage() => new() { BackColor = SystemColors.Window };

    private static Control MakeContent(string heading, string body)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24) };
        panel.Controls.Add(new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Window,
            Font = new Font("Segoe UI", 11F),
            Text = body.Replace("\n", Environment.NewLine, StringComparison.Ordinal),
        });
        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Text = heading,
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
        });
        return panel;
    }

    private static Button MakeButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Padding = new Padding(10, 4, 10, 4),
        Margin = new Padding(8, 0, 0, 0),
    };

    private void RefreshProfiles()
    {
        var selectedId = SelectedProfile?.Id;
        _profileCombo.BeginUpdate();
        _profileCombo.Items.Clear();
        foreach (var profile in _profiles.Profiles)
            _profileCombo.Items.Add(profile);
        _profileCombo.EndUpdate();
        _profileCombo.SelectedItem = _profiles.Find(selectedId) ?? _profiles.Profiles.FirstOrDefault();
        SelectedProfile = _profileCombo.SelectedItem as UserProfile;
        RefreshSessionStatus();
    }

    private void AddProfile()
    {
        using var dialog = new TextPromptDialog(
            "Añadir persona",
            "Nombre sencillo del perfil:");
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            SelectedProfile = _profiles.Create(dialog.Value);
            RefreshProfiles();
            _profileCombo.SelectedItem = SelectedProfile;
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "Nombre no válido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task StartLoginAsync()
    {
        if (SelectedProfile is null || _projectRoot is null)
            return;
        if (SessionLoginLauncher.IsRunning)
        {
            _sessionStatus.Text =
                "Ya hay un inicio de sesión abierto. Termínalo o cierra su ventana.";
            _sessionStatus.ForeColor = Color.DarkOrange;
            return;
        }
        var profile = SelectedProfile;
        SetSessionOperationState(true);
        try
        {
            MessageBox.Show(
                this,
                "Se abrirá una ventana negra.\n\n" +
                "Escribe allí el correo, la contraseña y el MFA. " +
                "La ventana se cerrará sola al terminar.",
                "Inicio de sesión",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            _sessionStatus.Text =
                "Completa el inicio de sesión en la ventana negra…";
            _sessionStatus.ForeColor = Color.DarkGoldenrod;
            var exitCode = await SessionLoginLauncher.RunAsync(
                profile,
                _projectRoot);
            _sessionStatus.Text = exitCode == 0
                ? "Acceso terminado; pulsa «Comprobar de nuevo»"
                : "Garmin no completó el acceso. Vuelve a intentarlo.";
            _sessionStatus.ForeColor = exitCode == 0
                ? Color.DarkGoldenrod
                : Color.DarkOrange;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "No se pudo iniciar", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetSessionOperationState(false);
        }
    }

    private void RefreshSessionStatus()
    {
        var ready = SelectedProfile is not null && AppPaths.HasSession(SelectedProfile);
        _sessionStatus.Text = ready
            ? "Sesión guardada; pulsa «Comprobar de nuevo»"
            : "Falta iniciar sesión en Garmin";
        _sessionStatus.ForeColor = ready ? Color.DarkGoldenrod : Color.DarkOrange;
    }

    private async Task CheckSessionAsync()
    {
        if (SelectedProfile is null || _projectRoot is null)
            return;
        if (SessionLoginLauncher.IsRunning)
        {
            _sessionStatus.Text =
                "El inicio de sesión sigue abierto. Termínalo o cierra su ventana y vuelve a comprobar.";
            _sessionStatus.ForeColor = Color.DarkOrange;
            return;
        }
        var profile = SelectedProfile;
        SetSessionOperationState(true);
        _sessionStatus.Text = "Comprobando la sesión con Garmin…";
        _sessionStatus.ForeColor = Color.DarkGoldenrod;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var valid = await SessionValidator.ValidateAsync(
                profile,
                _projectRoot,
                timeout.Token);
            if (!ReferenceEquals(profile, SelectedProfile))
            {
                RefreshSessionStatus();
                return;
            }
            _sessionStatus.Text = valid
                ? "✓ Sesión comprobada correctamente"
                : "No se pudo validar. Inicia sesión y vuelve a comprobar.";
            _sessionStatus.ForeColor = valid ? Color.DarkGreen : Color.DarkOrange;
        }
        finally
        {
            SetSessionOperationState(false);
        }
    }

    private void SetSessionOperationState(bool running)
    {
        _sessionOperationRunning = running;
        _steps.Enabled = !running;
        _profileCombo.Enabled = !running;
        if (running)
        {
            _next.Enabled = false;
            _previous.Enabled = false;
            return;
        }

        _next.Enabled = true;
        UpdateNavigation();
    }

    private void UpdateNavigation()
    {
        if (_sessionOperationRunning)
        {
            _previous.Enabled = false;
            _next.Enabled = false;
            return;
        }
        _previous.Enabled = _steps.SelectedIndex > 0;
        _next.Text = _steps.SelectedIndex == _steps.TabCount - 1
            ? "Terminar"
            : "Siguiente";
    }

    private void NextStep()
    {
        if (_steps.SelectedIndex < _steps.TabCount - 1)
        {
            _steps.SelectedIndex++;
            return;
        }

        SelectedProfile ??= _profiles.Profiles.FirstOrDefault();
        DialogResult = DialogResult.OK;
        Close();
    }
}

using GarminDataExport.Launcher.Models;

namespace GarminDataExport.Launcher.Services;

internal static class AppPaths
{
    private static readonly HashSet<string> ReservedWindowsNames =
        new(
            new[]
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5",
                "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5",
                "LPT6", "LPT7", "LPT8", "LPT9",
            },
            StringComparer.OrdinalIgnoreCase);

    public static string LocalRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GarminDataExportLauncher");

    public static string SettingsFile => Path.Combine(LocalRoot, "settings.json");
    public static string UpdateCheckFile => Path.Combine(LocalRoot, "actualizaciones.json");
    public static string ProfilesFile => Path.Combine(LocalRoot, "profiles.json");
    public static string ProfilesRoot => Path.Combine(LocalRoot, "profiles");

    public static string FriendlyDocumentsRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Garmin para IA");

    public static string ProfileDataRoot(UserProfile profile) =>
        Path.Combine(ProfilesRoot, SafeProfileId(profile.Id));

    public static string TokenStore(UserProfile profile) =>
        profile.Id == UserProfile.LegacyId
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".garminconnect")
            : Path.Combine(ProfileDataRoot(profile), "sesion");

    public static string CacheDirectory(UserProfile profile) =>
        Path.Combine(ProfileDataRoot(profile), "cache");

    public static string RaceContextFile(UserProfile profile) =>
        Path.Combine(ProfileDataRoot(profile), "contexto-carrera.json");

    public static string JournalFile(UserProfile profile) =>
        Path.Combine(ProfileDataRoot(profile), "diario.json");

    public static string ManifestFile(UserProfile profile) =>
        Path.Combine(ProfileDataRoot(profile), "ultima-exportacion.json");

    public static string ActivityListFile(UserProfile profile) =>
        Path.Combine(ProfileDataRoot(profile), "actividades-recientes.json");

    public static string LoginScriptFile(UserProfile profile) =>
        Path.Combine(ProfileDataRoot(profile), "iniciar-sesion.cmd");

    public static string OutputDirectory(UserProfile profile) =>
        Path.Combine(FriendlyDocumentsRoot, SafeOutputFolder(profile.OutputFolderName));

    public static bool HasSession(UserProfile profile)
    {
        var directory = TokenStore(profile);
        try
        {
            return Directory.Exists(directory) &&
                   File.Exists(Path.Combine(directory, "oauth1_token.json")) &&
                   File.Exists(Path.Combine(directory, "oauth2_token.json"));
        }
        catch
        {
            return false;
        }
    }

    private static string SafeProfileId(string id) =>
        id == UserProfile.LegacyId ||
        (id.Length == 32 && id.All(Uri.IsHexDigit))
            ? id
            : throw new InvalidDataException("El identificador del perfil no es válido.");

    internal static bool IsSafeOutputFolderName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value is "." or ".." ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            value.EndsWith('.') ||
            !string.Equals(
                value,
                Path.GetFileName(value),
                StringComparison.Ordinal) ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }
        var stem = value.Split('.', 2)[0];
        return !ReservedWindowsNames.Contains(stem);
    }

    private static string SafeOutputFolder(string value) =>
        IsSafeOutputFolderName(value)
            ? value
            : throw new InvalidDataException(
                "La carpeta de salida del perfil no es válida.");
}

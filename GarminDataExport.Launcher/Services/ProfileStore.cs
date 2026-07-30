using GarminDataExport.Launcher.Models;

namespace GarminDataExport.Launcher.Services;

internal sealed class ProfileStore
{
    private sealed class ProfileDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public List<UserProfile> Profiles { get; set; } = [];
    }

    private readonly List<UserProfile> _profiles;
    private readonly bool _persistent;

    public ProfileStore()
    {
        _persistent = true;
        var document = AtomicJsonStore.Read<ProfileDocument>(AppPaths.ProfilesFile);
        var candidates = document?.Profiles
            ?.Where(IsValid)
            .ToList() ?? [];
        foreach (var profile in candidates)
        {
            if (profile.Id != UserProfile.LegacyId)
                profile.Id = profile.Id.ToLowerInvariant();
        }
        _profiles = candidates
            .GroupBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var migratedProfiles = false;
        var usedOutputFolders = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var profile in _profiles.OrderByDescending(
                     profile => profile.Id == UserProfile.LegacyId))
        {
            var shouldUseLegacySession = profile.Id == UserProfile.LegacyId;
            if (profile.UsesLegacySession != shouldUseLegacySession)
            {
                profile.UsesLegacySession = shouldUseLegacySession;
                migratedProfiles = true;
            }
            if (!AppPaths.IsSafeOutputFolderName(profile.OutputFolderName) ||
                !usedOutputFolders.Add(profile.OutputFolderName))
            {
                profile.OutputFolderName = BuildUniqueOutputFolderName(
                    profile,
                    usedOutputFolders);
                migratedProfiles = true;
            }
        }

        if (_profiles.Count == 0)
        {
            _profiles.Add(new UserProfile
            {
                Id = UserProfile.LegacyId,
                Alias = AppPaths.HasSession(new UserProfile
                {
                    Id = UserProfile.LegacyId,
                    OutputFolderName = "Mi Garmin",
                    UsesLegacySession = true,
                })
                    ? "Mi Garmin (sesión existente)"
                    : "Mi Garmin",
                OutputFolderName = "Mi Garmin",
                UsesLegacySession = true,
            });
            Save();
        }
        else if (migratedProfiles)
        {
            Save();
        }
    }

    private ProfileStore(List<UserProfile> profiles)
    {
        _profiles = profiles;
        _persistent = false;
    }

    internal static ProfileStore CreateReadmePreview() =>
        new(
        [
            new UserProfile
            {
                Id = "f1e2d3c4b5a697887766554433221100",
                Alias = "Perfil de ejemplo",
                OutputFolderName = "Perfil de ejemplo",
                UsesLegacySession = false,
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            },
        ]);

    public IReadOnlyList<UserProfile> Profiles => _profiles
        .OrderBy(profile => profile.CreatedAtUtc)
        .ToList();

    public UserProfile? Find(string? id) =>
        _profiles.FirstOrDefault(
            profile => string.Equals(profile.Id, id, StringComparison.Ordinal));

    public UserProfile Create(string alias)
    {
        var cleanAlias = ValidateAlias(alias);
        var profile = new UserProfile
        {
            Alias = cleanAlias,
            Id = Guid.NewGuid().ToString("N"),
            UsesLegacySession = false,
        };
        profile.OutputFolderName = BuildOutputFolderName(profile);
        _profiles.Add(profile);
        Save();
        return profile;
    }

    public void Rename(UserProfile profile, string alias)
    {
        profile.Alias = ValidateAlias(alias);
        Save();
    }

    public void Save()
    {
        if (!_persistent)
            return;
        AtomicJsonStore.Write(
            AppPaths.ProfilesFile,
            new ProfileDocument { Profiles = _profiles });
    }

    private static bool IsValid(UserProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.Alias) &&
        (profile.Id == UserProfile.LegacyId ||
         (profile.Id.Length == 32 && profile.Id.All(Uri.IsHexDigit)));

    private static string ValidateAlias(string alias)
    {
        var value = alias.Trim();
        if (value.Length is < 1 or > 50)
            throw new ArgumentException("El nombre del perfil debe tener entre 1 y 50 caracteres.");
        if (value.Any(char.IsControl))
            throw new ArgumentException("El nombre del perfil contiene caracteres no permitidos.");
        return value;
    }

    private static string BuildOutputFolderName(UserProfile profile)
    {
        if (profile.Id == UserProfile.LegacyId)
            return "Mi Garmin";
        var safeAlias = new string(
            profile.Alias
                .Where(character =>
                    !char.IsControl(character) &&
                    Array.IndexOf(Path.GetInvalidFileNameChars(), character) < 0)
                .ToArray())
            .Trim()
            .TrimEnd('.');
        if (string.IsNullOrWhiteSpace(safeAlias))
            safeAlias = "Perfil";
        if (safeAlias.Length > 32)
            safeAlias = safeAlias[..32].Trim();
        return $"{safeAlias}-{profile.Id[..8]}";
    }

    private static string BuildUniqueOutputFolderName(
        UserProfile profile,
        HashSet<string> used)
    {
        var baseName = BuildOutputFolderName(profile);
        var candidate = baseName;
        var suffix = 2;
        while (!used.Add(candidate))
        {
            var suffixText = $"-{suffix}";
            var maximumBaseLength = Math.Max(1, 60 - suffixText.Length);
            var shortened = baseName.Length > maximumBaseLength
                ? baseName[..maximumBaseLength].TrimEnd()
                : baseName;
            candidate = shortened + suffixText;
            suffix++;
        }
        return candidate;
    }
}

using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace GarminDataExport.Launcher.Services;

internal sealed record UpdateCheckResult(
    bool Success,
    bool UpdateAvailable,
    bool ShouldNotify,
    string CurrentVersion,
    string LatestVersion,
    string LatestTag,
    string ReleaseUrl);

internal static class UpdateChecker
{
    public const string ReleasePage =
        "https://github.com/zarzawan/ExportaGarmin/releases/latest";

    private static readonly Uri LatestReleaseApi = new(
        "https://api.github.com/repos/zarzawan/ExportaGarmin/releases/latest");
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);
    private static readonly HttpClient Client = CreateClient();

    public static string CurrentVersion => FormatVersion(GetCurrentVersion());

    public static async Task<UpdateCheckResult> CheckAsync(bool force)
    {
        var current = GetCurrentVersion();
        var state = ReadState();
        if (!force &&
            state.LastCheckedUtc > DateTime.UtcNow - CacheLifetime &&
            state.LastCheckedUtc <= DateTime.UtcNow.AddMinutes(5) &&
            TryParseVersion(state.LatestTag, out var cachedLatest))
        {
            return CreateResult(current, cachedLatest, state);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var response = await Client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if (!response.IsSuccessStatusCode)
                return Failed(current);

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: timeout.Token);
            if (!document.RootElement.TryGetProperty("tag_name", out var tagElement))
                return Failed(current);
            var tag = tagElement.GetString()?.Trim() ?? "";
            if (!TryParseVersion(tag, out var latest))
                return Failed(current);

            state.LastCheckedUtc = DateTime.UtcNow;
            state.LatestTag = tag;
            TryWriteState(state);
            return CreateResult(current, latest, state);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
            TaskCanceledException or
            JsonException or
            IOException or
            InvalidDataException or
            UnauthorizedAccessException)
        {
            return Failed(current);
        }
    }

    public static void MarkNotified(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return;
        var state = ReadState();
        state.LastNotifiedTag = tag.Trim();
        TryWriteState(state);
    }

    private static UpdateCheckResult CreateResult(
        Version current,
        Version latest,
        UpdateCheckState state)
    {
        var available = latest > current;
        return new UpdateCheckResult(
            Success: true,
            UpdateAvailable: available,
            ShouldNotify: available && !string.Equals(
                state.LastNotifiedTag,
                state.LatestTag,
                StringComparison.OrdinalIgnoreCase),
            CurrentVersion: FormatVersion(current),
            LatestVersion: FormatVersion(latest),
            LatestTag: state.LatestTag,
            ReleaseUrl: ReleasePage);
    }

    private static UpdateCheckResult Failed(Version current) => new(
        Success: false,
        UpdateAvailable: false,
        ShouldNotify: false,
        CurrentVersion: FormatVersion(current),
        LatestVersion: "",
        LatestTag: "",
        ReleaseUrl: ReleasePage);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ExportaGarmin", CurrentVersion));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static Version GetCurrentVersion()
    {
        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version ??
                              new Version(0, 0, 0);
        return new Version(
            Math.Max(0, assemblyVersion.Major),
            Math.Max(0, assemblyVersion.Minor),
            Math.Max(0, assemblyVersion.Build));
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        version = new Version(0, 0, 0);
        var plain = value.Trim().TrimStart('v', 'V');
        var separator = plain.IndexOfAny(['-', '+']);
        if (separator >= 0)
            plain = plain[..separator];
        if (!Version.TryParse(plain, out var parsed))
            return false;
        version = new Version(
            Math.Max(0, parsed.Major),
            Math.Max(0, parsed.Minor),
            Math.Max(0, parsed.Build));
        return true;
    }

    private static string FormatVersion(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";

    private static UpdateCheckState ReadState()
    {
        try
        {
            return AtomicJsonStore.Read<UpdateCheckState>(AppPaths.UpdateCheckFile) ?? new();
        }
        catch (InvalidDataException)
        {
            return new UpdateCheckState();
        }
    }

    private static void TryWriteState(UpdateCheckState state)
    {
        try
        {
            AtomicJsonStore.Write(AppPaths.UpdateCheckFile, state);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            // La comprobación de versiones nunca debe impedir usar la aplicación.
        }
    }

    private sealed class UpdateCheckState
    {
        public DateTime LastCheckedUtc { get; set; }
        public string LatestTag { get; set; } = "";
        public string LastNotifiedTag { get; set; } = "";
    }
}

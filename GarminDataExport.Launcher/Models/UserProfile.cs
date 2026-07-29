namespace GarminDataExport.Launcher.Models;

internal sealed class UserProfile
{
    public const string LegacyId = "legacy";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Alias { get; set; } = "Mi perfil";
    public string OutputFolderName { get; set; } = "";
    public bool UsesLegacySession { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public override string ToString() => Alias;
}

namespace GarminDataExport.Launcher.Models;

internal sealed class LauncherSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; }
    public bool FirstRunCompleted { get; set; }
    public string? ActiveProfileId { get; set; }
    public int ReviewWeeks { get; set; } = 16;
    public string OutputFormat { get; set; } = "txt";
    public DateTime StartDate { get; set; } = DateTime.Today.AddDays(-30);
    public DateTime EndDate { get; set; } = DateTime.Today;
    public bool IncludeActivityDetails { get; set; }
    public bool ShowTechnicalLog { get; set; }

    public bool ApplyMigrations()
    {
        if (SchemaVersion >= CurrentSchemaVersion)
            return false;

        OutputFormat = "txt";
        SchemaVersion = CurrentSchemaVersion;
        return true;
    }
}

namespace GarminDataExport.Launcher.Models;

internal sealed class LauncherSettings
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; }
    public bool FirstRunCompleted { get; set; }
    public string? ActiveProfileId { get; set; }
    public int ReviewWeeks { get; set; } = 16;
    public string OutputFormat { get; set; } = "txt";
    public DateTime StartDate { get; set; } = DateTime.Today.AddDays(-30);
    public DateTime EndDate { get; set; } = DateTime.Today;
    public bool IncludeActivityDetails { get; set; }
    public bool ShowTechnicalLog { get; set; } = true;

    public bool ApplyMigrations()
    {
        if (SchemaVersion >= CurrentSchemaVersion)
            return false;

        if (SchemaVersion < 1)
            OutputFormat = "txt";
        if (SchemaVersion < 2)
            ShowTechnicalLog = true;
        SchemaVersion = CurrentSchemaVersion;
        return true;
    }
}

namespace GarminDataExport.Launcher.Models;

internal sealed class JournalDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<JournalEntry> Entries { get; set; } = [];
}

internal sealed class JournalEntry
{
    public string EntryId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Date { get; set; } = DateTime.Today;
    public string ActivityId { get; set; } = "";
    public string ActivityDisplayName { get; set; } = "";
    public string IntendedPurpose { get; set; } = "";
    public int? PainScore0To10 { get; set; }
    public string PainLocation { get; set; } = "";
    public int? PerceivedEffort1To10 { get; set; }
    public int? Fatigue1To5 { get; set; }
    public int? Motivation1To5 { get; set; }
    public int? LifeStress1To10 { get; set; }
    public int? CarbohydratesGramsPerHour { get; set; }
    public int? FluidMillilitresPerHour { get; set; }
    public int? SodiumMilligramsPerHour { get; set; }
    public string GastrointestinalTolerance { get; set; } = "";
    public string PrivateComment { get; set; } = "";
    public bool IncludeCommentInExport { get; set; }
    public string DataSource { get; set; } = "user_provided";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

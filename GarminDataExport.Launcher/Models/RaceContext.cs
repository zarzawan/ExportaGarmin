namespace GarminDataExport.Launcher.Models;

internal sealed class RaceContext
{
    public int SchemaVersion { get; set; } = 1;
    public string RaceName { get; set; } = "";
    public string RaceType { get; set; } = "marathon";
    public double? DistanceKm { get; set; } = 42.195;
    public DateTime? RaceDate { get; set; }
    public string GoalType { get; set; } = "finish";
    public int? TargetTimeSeconds { get; set; }
    public string Experience { get; set; } = "";
    public int? AvailableDaysPerWeek { get; set; }
    public string LongRunDay { get; set; } = "";
    public int? StrengthDaysPerWeek { get; set; }
    public int? AvailableMinutesPerWeek { get; set; }
    public string Terrain { get; set; } = "";
    public string ExpectedClimate { get; set; } = "";
    public string RecentPerformance { get; set; } = "";
    public string TrainingConstraints { get; set; } = "";
    public string DataSource { get; set; } = "user_provided";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

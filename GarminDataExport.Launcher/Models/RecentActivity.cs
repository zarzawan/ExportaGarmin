namespace GarminDataExport.Launcher.Models;

internal sealed class RecentActivity
{
    public string Id { get; init; } = "";
    public string Date { get; init; } = "";
    public string Sport { get; init; } = "";
    public string Label { get; init; } = "";
    public double? DistanceMeters { get; init; }
    public double? DurationSeconds { get; init; }

    public override string ToString()
    {
        var distance = DistanceMeters is > 0
            ? $"{DistanceMeters.Value / 1000d:0.0} km"
            : "";
        var duration = DurationSeconds is > 0
            ? FormatDuration(DurationSeconds.Value)
            : "";
        var parts = new[] { Date, Sport, distance, duration, Label }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        var visible = string.Join(" · ", parts);
        return string.IsNullOrWhiteSpace(visible) ? $"Actividad {Id}" : visible;
    }

    private static string FormatDuration(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours} h {span.Minutes} min"
            : $"{Math.Max(1, span.Minutes)} min";
    }
}

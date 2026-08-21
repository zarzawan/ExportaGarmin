using System.Globalization;

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
        var summary = ToShortString();
        var visibleDate = FormatDateWithWeekday(Date);
        return string.IsNullOrWhiteSpace(visibleDate)
            ? summary
            : string.IsNullOrWhiteSpace(summary)
                ? visibleDate
                : $"{visibleDate} · {summary}";
    }

    public string ToShortString()
    {
        var distance = DistanceMeters is > 0
            ? $"{DistanceMeters.Value / 1000d:0.0} km"
            : "";
        var duration = DurationSeconds is > 0
            ? FormatDuration(DurationSeconds.Value)
            : "";
        var activityName = string.IsNullOrWhiteSpace(Label)
            ? Sport
            : Label;
        var parts = new[] { activityName, distance, duration }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        var visible = string.Join(" · ", parts);
        return string.IsNullOrWhiteSpace(visible) ? "Actividad reciente" : visible;
    }

    private static string FormatDuration(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours} h {span.Minutes} min"
            : $"{Math.Max(1, span.Minutes)} min";
    }

    private static string FormatDateWithWeekday(string value)
    {
        if (!DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var parsed))
        {
            return value;
        }

        var culture = CultureInfo.GetCultureInfo("es-ES");
        var weekday = culture.TextInfo.ToTitleCase(parsed.ToString("dddd", culture));
        return $"{weekday} {parsed:dd/MM/yyyy}";
    }
}

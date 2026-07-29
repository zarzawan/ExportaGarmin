using System.Text.Json;
using System.Text.RegularExpressions;
using GarminDataExport.Launcher.Models;

namespace GarminDataExport.Launcher.Services;

internal static class RecentActivityReader
{
    public static IReadOnlyList<RecentActivity> Read(string path)
    {
        if (!File.Exists(path))
            return [];

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var array = root.ValueKind == JsonValueKind.Array
            ? root
            : FindArray(root, "activities", "items", "data");
        if (array.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<RecentActivity>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var id = ReadString(
                item,
                "activity_ref",
                "activityRef",
                "activity_id",
                "activityId",
                "id");
            if (!Regex.IsMatch(id.Trim(), @"\Aactivity_[0-9a-f]{12}\z"))
                continue;
            result.Add(new RecentActivity
            {
                Id = id,
                Date = NormaliseDate(ReadString(
                    item,
                    "date",
                    "start_time_local",
                    "startTimeLocal",
                    "startTimeGMT")),
                Sport = TranslateSport(ReadSport(item)),
                Label = ReadString(item, "label", "name", "activity_name"),
                DistanceMeters = ReadNumber(item, "distance_m", "distanceMeters"),
                DurationSeconds = ReadNumber(item, "duration_s", "durationSeconds"),
            });
        }
        return result;
    }

    private static JsonElement FindArray(JsonElement item, params string[] names)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return default;
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.Array)
            {
                return value;
            }
        }
        return default;
    }

    private static string ReadString(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (!item.TryGetProperty(name, out var value))
                continue;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Number => value.GetRawText(),
                _ => "",
            };
        }
        return "";
    }

    private static string ReadSport(JsonElement item)
    {
        foreach (var name in new[] { "sport", "sport_type", "activity_type", "activityType" })
        {
            if (!item.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";
            if (value.ValueKind == JsonValueKind.Object)
                return ReadString(value, "typeKey", "type_key", "key", "name");
        }
        return "";
    }

    private static double? ReadNumber(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out var number))
            {
                return number;
            }
        }
        return null;
    }

    private static string NormaliseDate(string value)
    {
        if (value.Length == 10 &&
            DateOnly.TryParse(value, out var dateOnly))
        {
            return dateOnly.ToString("yyyy-MM-dd");
        }
        if (DateTimeOffset.TryParse(value, out var parsed))
            return parsed.ToString("yyyy-MM-dd HH:mm");
        return value.Length > 16 ? value[..16] : value;
    }

    private static string TranslateSport(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "running" or "run" => "Carrera",
            "trail_running" => "Carrera de montaña",
            "treadmill_running" => "Carrera en cinta",
            "cycling" or "cycling_road" or "road_biking" => "Ciclismo",
            "indoor_cycling" => "Ciclismo interior",
            "swimming" or "lap_swimming" => "Natación",
            "strength_training" or "strength" => "Fuerza",
            "walking" => "Caminar",
            "hiking" => "Senderismo",
            "" => "",
            _ => value.Replace('_', ' '),
        };
}

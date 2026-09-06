using System.Text.Json;

namespace GarminDataExport.Launcher.Services;

internal sealed record BackendEvent(string Event, string Phase, int Completed, int Total, string Status)
{
    public const string Prefix = "EXPORT_EVENT ";
    public static bool TryParse(string line, out BackendEvent? value)
    {
        value = null;
        if (!line.StartsWith(Prefix, StringComparison.Ordinal) || line.Length > 1024) return false;
        try
        {
            using var document = JsonDocument.Parse(line[Prefix.Length..]);
            var root = document.RootElement;
            if (root.GetProperty("protocol_version").GetInt32() != 1) return false;
            var kind = root.GetProperty("event").GetString() ?? "";
            if (kind is not ("phase" or "progress" or "result" or "error")) return false;
            var phase = root.TryGetProperty("phase", out var p) ? p.GetString() ?? "" : "";
            if (phase.Length > 80 || phase.Any(c => !(char.IsAsciiLetter(c) || " '&_-".Contains(c)))) return false;
            var done = root.TryGetProperty("completed", out var d) ? d.GetInt32() : 0;
            var total = root.TryGetProperty("total", out var t) ? t.GetInt32() : 0;
            if (done < 0 || total < 0 || done > total) return false;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
            if (status is not ("" or "completed" or "partial" or "failed")) return false;
            value = new BackendEvent(kind, phase, done, total, status);
            return true;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            return false;
        }
    }
}

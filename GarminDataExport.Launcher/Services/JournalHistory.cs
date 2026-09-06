using System.Globalization;
using GarminDataExport.Launcher.Models;

namespace GarminDataExport.Launcher.Services;

internal sealed record JournalPage(IReadOnlyList<JournalEntry> Entries, int Index, int Pages, int Total);

internal static class JournalHistory
{
    public static JournalPage Query(IEnumerable<JournalEntry> entries, string search, int page, int pageSize = 100)
    {
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize));
        var query = search.Trim();
        var matches = entries.Where(entry => string.IsNullOrWhiteSpace(query) ||
            string.Join(" ", entry.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                entry.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                entry.ActivityDisplayName, entry.PrivateComment, entry.IntendedPurpose)
            .Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(entry => entry.Date).ThenByDescending(entry => entry.CreatedAtUtc).ToList();
        var pages = Math.Max(1, (matches.Count + pageSize - 1) / pageSize);
        var index = Math.Clamp(page, 0, pages - 1);
        return new JournalPage(matches.Skip(index * pageSize).Take(pageSize).ToList(), index, pages, matches.Count);
    }
}

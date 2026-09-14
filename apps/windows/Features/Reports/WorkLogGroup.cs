using System.Globalization;
using VolturaEarner.Features.Tracking;

namespace VolturaEarner.Features.Reports;

internal sealed record WorkLogGroup(DateOnly Day, string Task, WorkEntry[] Entries)
{
    public string Date => Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string Duration => ReportService.Duration(Entries.Sum(entry => entry.TotalTicks));
    public string Overtime => ReportService.Duration(Entries.Sum(entry => entry.OvertimeTicks));
    public string Amount => string.Join(" · ", Entries.GroupBy(entry => entry.Currency, StringComparer.Ordinal).Select(group => $"{group.Sum(entry => entry.Earned):N2} {group.Key}"));

    internal static WorkLogGroup[] Create(IEnumerable<WorkEntry> entries) => entries
        .GroupBy(entry => (entry.Date, entry.Task))
        .OrderByDescending(group => group.Key.Date)
        .ThenBy(group => group.Key.Task, StringComparer.CurrentCulture)
        .Select(group => new WorkLogGroup(group.Key.Date, group.Key.Task, group.ToArray()))
        .ToArray();
}

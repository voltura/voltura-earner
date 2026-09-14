using VolturaEarner.Features.Tracking;

namespace VolturaEarner.Features.Reports;

internal sealed record WorkLogTotals(string Duration, string Overtime, string Amount)
{
    internal static WorkLogTotals Create(WorkEntry[] entries) => new(
        ReportService.Duration(entries.Sum(entry => entry.TotalTicks)),
        ReportService.Duration(entries.Sum(entry => entry.OvertimeTicks)),
        entries.Length == 0
            ? "—"
            : string.Join(Environment.NewLine, entries
            .GroupBy(entry => entry.Currency, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Sum(entry => entry.Earned):N2} {group.Key}")));
}

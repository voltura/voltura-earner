using System.Globalization;
using MiniExcelLibs;
using VolturaEarner.Features.Tracking;
using VolturaEarner.Features.Localization;

namespace VolturaEarner.Features.Reports;

public enum ReportPeriod { Today, Week, Month, Year, All }

public static class ReportService
{
    public static bool Includes(DateOnly date, DateOnly today, ReportPeriod period, DayOfWeek firstDay)
    {
        var weekStart = today.DayNumber - ((7 + (int)today.DayOfWeek - (int)firstDay) % 7);

        return period switch
        {
            ReportPeriod.Today => date == today,
            ReportPeriod.Week => date.DayNumber >= weekStart && date.DayNumber < weekStart + 7,
            ReportPeriod.Month => date.Year == today.Year && date.Month == today.Month,
            ReportPeriod.Year => date.Year == today.Year,
            _ => true,
        };
    }

    public static string Duration(long ticks)
    {
        var seconds = ticks / TimeSpan.TicksPerSecond;

        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}");
    }

    public static WorkEntry[] Select(WorkData data, DateOnly today, ReportPeriod period, DayOfWeek firstDay) => data.Entries.Where(e => Includes(e.Date, today, period, firstDay)).OrderByDescending(e => e.Date).ThenBy(e => e.Task, StringComparer.CurrentCulture).ToArray();

    public static Task<string> ExportAsync(WorkData data, DateOnly today, ReportPeriod period, CultureInfo culture, string path, CancellationToken token = default) =>
        ExportAsync(data, today, period, culture, path, "en", token);

    public static Task<string> ExportAsync(WorkData data, DateOnly today, ReportPeriod period, CultureInfo culture, string path, string languageId, CancellationToken token = default) =>
        ExportAsync(data, today, period, culture, path, languageId, true, token);

    public static Task<string> ExportAsync(WorkData data, DateOnly today, ReportPeriod period, CultureInfo culture, string path, string languageId, bool overwrite, CancellationToken token = default) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();

        var language = LanguageCatalog.Resolve(languageId, culture);
        var text = language.Entries;
        var displayCulture = CultureInfo.GetCultureInfo(language.CultureName);
        var entries = Select(data, today, period, culture.DateTimeFormat.FirstDayOfWeek);
        var rows = entries.Select(e => new ExportRow
        {
            Task = e.Task,
            Date = e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Day = e.Date.ToString("dddd", displayCulture),
            Earned = Math.Round(e.Earned, 2, MidpointRounding.AwayFromZero),
            Currency = e.Currency,
            Time = Duration(e.TotalTicks),
            Hours = Math.Round((decimal)e.TotalTicks / TimeSpan.TicksPerHour, 4),
            Seconds = Math.Round((decimal)e.TotalTicks / TimeSpan.TicksPerSecond, 3),
            RegularHours = Math.Round((decimal)e.RegularTicks / TimeSpan.TicksPerHour, 4),
            OvertimeHours = Math.Round((decimal)e.OvertimeTicks / TimeSpan.TicksPerHour, 4),
            OvertimeEarned = Math.Round(e.OvertimeEarned, 2, MidpointRounding.AwayFromZero),
            HourlyRate = e.HourlyRate,
            OvertimeHourlyRate = e.EffectiveOvertimeRate,
        }).ToList();
        var days = data.Days.Where(d => Includes(d.Date, today, period, culture.DateTimeFormat.FirstDayOfWeek)).ToArray();

        foreach (var currency in entries.Select(e => e.Currency).Concat(days.Select(d => d.Currency)).Distinct(StringComparer.Ordinal))
        {
            var group = entries.Where(e => e.Currency == currency).ToArray();
            var gross = group.Sum(e => e.Earned);
            var cost = days.Where(d => d.Currency == currency).Sum(d => d.Cost);

            rows.Add(new ExportRow { Task = text["Total"], Currency = currency, Earned = Math.Round(gross, 2, MidpointRounding.AwayFromZero), DailyCost = cost, Net = Math.Round(gross - cost, 2, MidpointRounding.AwayFromZero), Time = Duration(group.Sum(e => e.TotalTicks)), Hours = Math.Round((decimal)group.Sum(e => e.TotalTicks) / TimeSpan.TicksPerHour, 4), Seconds = Math.Round((decimal)group.Sum(e => e.TotalTicks) / TimeSpan.TicksPerSecond, 3), RegularHours = Math.Round((decimal)group.Sum(e => e.RegularTicks) / TimeSpan.TicksPerHour, 4), OvertimeHours = Math.Round((decimal)group.Sum(e => e.OvertimeTicks) / TimeSpan.TicksPerHour, 4), OvertimeEarned = Math.Round(group.Sum(e => e.OvertimeEarned), 2, MidpointRounding.AwayFromZero) });
        }

        var directory = Path.GetDirectoryName(path)!;

        Directory.CreateDirectory(directory);

        var pending = Path.Combine(directory, ".earner-export-" + Guid.NewGuid().ToString("N") + ".xlsx");

        try
        {
            var localized = rows.Select(row => new Dictionary<string, object?>
            {
                [text["Task"]] = row.Task,
                [text["Date"]] = row.Date,
                [text["Day"]] = row.Day,
                [text["Earned"]] = row.Earned,
                [text["Currency"]] = row.Currency,
                [text["Time"]] = row.Time,
                [text["Hours"]] = row.Hours,
                [text["Seconds"]] = row.Seconds,
                [text["RegularHours"]] = row.RegularHours,
                [text["OvertimeHours"]] = row.OvertimeHours,
                [text["OvertimeEarnings"]] = row.OvertimeEarned,
                [text["DailyCost"]] = row.DailyCost,
                [text["Net"]] = row.Net,
                [text["HourlyRate"]] = row.HourlyRate,
                [text["OvertimeHourlyRate"]] = row.OvertimeHourlyRate,
            }).ToArray();

            await MiniExcel.SaveAsAsync(pending, localized, excelType: ExcelType.XLSX, cancellationToken: token);
            token.ThrowIfCancellationRequested();

            if (!overwrite)
            {
                var name = Path.GetFileNameWithoutExtension(path);

                for (var suffix = 2; File.Exists(path); suffix++)
                {
                    token.ThrowIfCancellationRequested();
                    path = Path.Combine(directory, $"{name}-{suffix}.xlsx");
                }
            }

            File.Move(pending, path, overwrite);

            return path;
        }
        finally
        {
            if (File.Exists(pending))
            {
                File.Delete(pending);
            }
        }
    }, token);
}

public sealed class ExportRow
{
    public string Task { get; init; } = "";
    public string Date { get; init; } = "";
    public string Day { get; init; } = "";
    public decimal Earned { get; init; }
    public string Currency { get; init; } = "";
    public string Time { get; init; } = "";
    public decimal Hours { get; init; }
    public decimal Seconds { get; init; }
    public decimal RegularHours { get; init; }
    public decimal OvertimeHours { get; init; }
    public decimal OvertimeEarned { get; init; }
    public decimal? DailyCost { get; init; }
    public decimal? Net { get; init; }
    public decimal? HourlyRate { get; init; }
    public decimal? OvertimeHourlyRate { get; init; }
}

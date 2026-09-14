using System.Globalization;
using MiniExcelLibs;
using VolturaEarner.Features.Reports;
using VolturaEarner.Features.Settings;
using VolturaEarner.Features.Tracking;

namespace VolturaEarner.Tests;

public sealed class StorageReportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "VolturaEarner-tests", Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task InvalidAndCanceledWritesPreservePreviousStore()
    {
        using var store = new JsonStore<AppSettings>(Path.Combine(_root, "settings.json"), s => s.Validate());
        var original = new AppSettings { Tasks = ["Ångström, consulting"], SelectedTask = "Ångström, consulting", PlaySounds = true };

        await store.SaveAsync(original, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(original with { DailyHours = 0 }, TestContext.Current.CancellationToken));

        using var cancel = new CancellationTokenSource();

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new(), cancel.Token));

        var result = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(original.SelectedTask, result!.SelectedTask);
        Assert.True(result.PlaySounds);
        Assert.Empty(Directory.GetFiles(_root, "*.pending"));
    }
    [Fact]
    public async Task CorruptDataIsNotReplaced()
    {
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, "work.json");

        await File.WriteAllTextAsync(path, "broken", TestContext.Current.CancellationToken);

        using var store = new JsonStore<WorkData>(path, d => d.Validate());

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => store.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Equal("broken", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }
    [Theory]
    [InlineData("2026-09-01", "2026-08-31")]
    [InlineData("2027-01-01", "2026-12-28")]
    [InlineData("2024-03-01", "2024-02-29")]
    public void WeekIncludesAdjacentMonthYearAndLeapDay(string reference, string record)
    {
        Assert.True(ReportService.Includes(DateOnly.Parse(record, CultureInfo.InvariantCulture), DateOnly.Parse(reference, CultureInfo.InvariantCulture), ReportPeriod.Week, DayOfWeek.Monday));
    }
    [Fact]
    public void LongDurationsUseTotalHours() => Assert.Equal("125:05:03", ReportService.Duration(new TimeSpan(5, 5, 5, 3).Ticks));
    [Fact]
    public async Task CustomOvertimeRateSurvivesStorageAndExcelExport()
    {
        var day = new DateOnly(2026, 9, 14);
        var entry = new WorkEntry(Guid.NewGuid(), day, "Overtime", "kr", 100, TimeSpan.TicksPerHour, TimeSpan.TicksPerHour, 275, 175, 175);
        using var store = new JsonStore<WorkData>(Path.Combine(_root, "work.json"), d => d.Validate());

        await store.SaveAsync(new(1, [entry], [new(day, 1, 0, "kr")]), TestContext.Current.CancellationToken);

        var restored = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(175, Assert.Single(restored!.Entries).EffectiveOvertimeRate);

        var path = Path.Combine(_root, "custom-rate.xlsx");

        await ReportService.ExportAsync(restored, day, ReportPeriod.All, CultureInfo.InvariantCulture, path, TestContext.Current.CancellationToken);

        var row = MiniExcel.Query(path, useHeaderRow: true).Cast<IDictionary<string, object>>().Single(r => (string)r["Task"] == "Overtime");

        Assert.Equal(175, Convert.ToDecimal(row["Overtime hourly rate"], CultureInfo.InvariantCulture));
        Assert.Equal(175, Convert.ToDecimal(row["Overtime earnings"], CultureInfo.InvariantCulture));
        Assert.Equal(275, Convert.ToDecimal(row["Earned"], CultureInfo.InvariantCulture));
    }
    [Theory]
    [InlineData(ReportPeriod.Today)]
    [InlineData(ReportPeriod.Week)]
    [InlineData(ReportPeriod.Month)]
    [InlineData(ReportPeriod.Year)]
    [InlineData(ReportPeriod.All)]
    public async Task ExcelRetainsUnicodeZeroRateCurrencyAndOvertime(ReportPeriod period)
    {
        var today = new DateOnly(2026, 9, 14);
        var entries = new[]
        {
            new WorkEntry(Guid.NewGuid(), today, "Ångström, R&D", "kr", 100, TimeSpan.TicksPerHour, TimeSpan.TicksPerHour, 200m, 100m),
            new WorkEntry(Guid.NewGuid(), today, "No charge", "EUR", 0, TimeSpan.TicksPerHour, 0, 0, 0),
        };
        var data = new WorkData(1, entries, [new(today, 1, 50, "kr")]);
        var path = Path.Combine(_root, "report.xlsx");

        await ReportService.ExportAsync(data, today, period, CultureInfo.GetCultureInfo("sv-SE"), path, TestContext.Current.CancellationToken);

        var rows = MiniExcel.Query(path, useHeaderRow: true).Cast<IDictionary<string, object>>().ToArray();

        Assert.Equal(4, rows.Length);
        Assert.Contains(rows, row => (string)row["Task"] == "Ångström, R&D" && Convert.ToDecimal(row["Overtime earnings"], CultureInfo.InvariantCulture) == 100m);
        Assert.Contains(rows, row => (string)row["Task"] == "No charge");
        Assert.Contains(rows, row => (string)row["Task"] == "Total" && (string)row["Currency"] == "kr" && Convert.ToDecimal(row["Net"], CultureInfo.InvariantCulture) == 150m);
    }
    [Fact]
    public async Task WorkHistoryRoundTripsAndRejectsDuplicateIds()
    {
        var date = new DateOnly(2026, 9, 14);
        var entry = new WorkEntry(Guid.NewGuid(), date, "A", "kr", 10, TimeSpan.TicksPerHour, 0, 10, 0);
        var data = new WorkData(1, [entry], [new(date, 8, 0, "kr")]);
        using var store = new JsonStore<WorkData>(Path.Combine(_root, "work.json"), d => d.Validate());

        await store.SaveAsync(data, TestContext.Current.CancellationToken);
        Assert.Equal(entry, Assert.Single((await store.LoadAsync(TestContext.Current.CancellationToken))!.Entries));
        Assert.Throws<InvalidDataException>(() => (data with { Entries = [entry, entry] }).Validate());
    }
    [Fact]
    public async Task CreateOnlySaveCannotReplaceAnExistingFile()
    {
        using var store = new JsonStore<WorkData>(Path.Combine(_root, "work.json"), data => data.Validate());
        var original = new WorkData(1, [], [new(new(2026, 9, 14), 8, 50, "USD")]);

        await store.SaveAsync(original, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(WorkData.Empty, false, TestContext.Current.CancellationToken));
        Assert.Equal(original.Days, (await store.LoadAsync(TestContext.Current.CancellationToken))!.Days);
        Assert.Empty(Directory.GetFiles(_root, "*.pending"));
    }

    [Fact]
    public async Task MissingLoadCannotOverwriteAFileThatAppearsBeforeSaving()
    {
        var path = Path.Combine(_root, "work.json");
        using var store = new JsonStore<WorkData>(path, data => data.Validate());

        Assert.Null(await store.LoadAsync(TestContext.Current.CancellationToken));

        using var other = new JsonStore<WorkData>(path, data => data.Validate());
        var original = new WorkData(1, [], [new(new(2026, 9, 14), 8, 50, "USD")]);

        await other.SaveAsync(original, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(WorkData.Empty, TestContext.Current.CancellationToken));
        Assert.Equal(original.Days, (await other.LoadAsync(TestContext.Current.CancellationToken))!.Days);
        Assert.Empty(Directory.GetFiles(_root, "*.pending"));
    }

    [Fact]
    public async Task NewStoreCanCreateThenUpdateItsOwnFile()
    {
        using var store = new JsonStore<WorkData>(Path.Combine(_root, "work.json"), data => data.Validate());

        Assert.Null(await store.LoadAsync(TestContext.Current.CancellationToken));
        await store.SaveAsync(WorkData.Empty, TestContext.Current.CancellationToken);

        var updated = new WorkData(1, [], [new(new(2026, 9, 14), 8, 50, "USD")]);

        await store.SaveAsync(updated, TestContext.Current.CancellationToken);
        Assert.Equal(updated.Days, (await store.LoadAsync(TestContext.Current.CancellationToken))!.Days);
    }

    [Fact]
    public async Task CancellationDuringExcelWritingKeepsThePreviousReportAndRemovesTemporaryFiles()
    {
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, "report.xlsx");

        await File.WriteAllTextAsync(path, "previous report", TestContext.Current.CancellationToken);

        var date = new DateOnly(2026, 9, 14);
        var entries = Enumerable.Range(0, 100000).Select(index => new WorkEntry(Guid.NewGuid(), date, "Task " + index, "USD", 100, TimeSpan.TicksPerMillisecond, 0, 0.001m, 0)).ToArray();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var export = ReportService.ExportAsync(new(1, entries, []), date, ReportPeriod.All, CultureInfo.InvariantCulture, path, cancellation.Token);

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);

            while (!export.IsCompleted && !Directory.EnumerateFiles(_root, ".earner-export-*.xlsx").Any())
            {
                Assert.True(DateTime.UtcNow < deadline);
                await Task.Delay(1, TestContext.Current.CancellationToken);
            }

            Assert.False(export.IsCompleted);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal("previous report", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Single(Directory.GetFiles(_root));
        }
        finally
        {
            await cancellation.CancelAsync();

            try
            {
                await export;
            }
            catch (OperationCanceledException) { }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}

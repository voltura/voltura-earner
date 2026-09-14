using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MiniExcelLibs;
using VolturaEarner.Features.Localization;
using VolturaEarner.Features.Reports;
using VolturaEarner.Features.Settings;
using VolturaEarner.Features.Tracking;

namespace VolturaEarner.Tests;

public sealed class LocalizationTests
{
    public static TheoryData<string> Languages => new(LanguageCatalog.All.Select(language => language.Id));
    private static readonly string[] ReportKeys = ["Task", "Date", "Day", "Earned", "Currency", "Time", "Hours", "Seconds", "RegularHours", "OvertimeHours", "OvertimeEarnings", "DailyCost", "Net", "HourlyRate", "OvertimeHourlyRate"];

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryLanguageHasCompleteTextValidPlaceholdersAndDistinctReportColumns(string id)
    {
        var english = LanguageCatalog.All[0].Entries;
        var language = LanguageCatalog.Resolve(id, CultureInfo.InvariantCulture);
        var entries = language.Entries;

        Assert.Equal(english.Keys.Order(), entries.Keys.Order());

        foreach (var (key, value) in entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(value), $"{id}/{key} is empty.");
            Assert.Equal(CompositeFormat.Parse(english[key]).MinimumArgumentCount, CompositeFormat.Parse(value).MinimumArgumentCount);
            Assert.Equal(Regex.Matches(english[key], @"\{\d+").Select(match => match.Value).Order(), Regex.Matches(value, @"\{\d+").Select(match => match.Value).Order());
        }

        Assert.Equal(ReportKeys.Length, ReportKeys.Select(key => entries[key]).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryLiteralUiResourceReferenceExists()
    {
        var english = LanguageCatalog.All[0].Entries;
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "LocalizationSource"), "*", SearchOption.AllDirectories);

        Assert.NotEmpty(sources);

        foreach (var path in sources)
        {
            var source = File.ReadAllText(path);
            var keys = Regex.Matches(source, "ui:Loc ([A-Za-z0-9]+)|Strings\\.Current(?:\\[|\\.Format\\()\"([A-Za-z0-9]+)\"")
                .Select(match => match.Groups[1].Success
                    ? match.Groups[1].Value
                    : match.Groups[2].Value);

            foreach (var key in keys)
            {
                Assert.True(key == "LanguageHeading" || english.ContainsKey(key), $"Missing {key} in {path}.");
            }
        }
    }

    [Fact]
    public void InstallerAndAppOfferTheSameLanguagesAndNativeNames()
    {
        var installer = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InstallerLanguages.nsh"));
        var languages = Regex.Matches(installer, "!insertmacro VolturaLanguage \"([^\"]+)\" \"[^\"]+\" \"([^\"]+)\"");

        Assert.Equal(LanguageCatalog.All.Select(language => (language.Id, language.NativeName)), languages.Select(match => (match.Groups[1].Value, match.Groups[2].Value)));
        Assert.Equal(21, languages.Count);
        Assert.Equal("system", new AppSettings().Language);
        Assert.Throws<InvalidDataException>(() => (new AppSettings { Language = "unknown" }).Validate());
    }

    [Theory]
    [InlineData("sv-FI", "sv")]
    [InlineData("zh-HK", "yue")]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("zh-MO", "zh-Hant")]
    [InlineData("zh-Hans", "zh-Hans")]
    [InlineData("zh-Hant", "zh-Hant")]
    [InlineData("nb-NO", "nb")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("ar-SA", "en")]
    public void FollowWindowsResolvesLikeWeekNumber(string culture, string expected)
    {
        Assert.Equal(expected, LanguageCatalog.Resolve("system", CultureInfo.GetCultureInfo(culture)).Id);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public async Task ReportsTranslateHeadingsDaysAndTotalsWithoutChangingUserData(string id)
    {
        var root = Path.Combine(Path.GetTempPath(), "VolturaEarner-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "report.xlsx");
        var day = new DateOnly(2026, 9, 14);
        var entry = new WorkEntry(Guid.NewGuid(), day, "Ångström 仕事", "kr", 100, TimeSpan.TicksPerHour, TimeSpan.TicksPerHour, 275, 175, 175);
        var data = new WorkData(1, [entry], [new(day, 1, 25, "kr")]);
        var language = LanguageCatalog.Resolve(id, CultureInfo.InvariantCulture);
        var text = language.Entries;

        try
        {
            await ReportService.ExportAsync(data, day, ReportPeriod.Today, CultureInfo.GetCultureInfo("sv-SE"), path, id, TestContext.Current.CancellationToken);

            var rows = MiniExcel.Query(path, useHeaderRow: true).Cast<IDictionary<string, object>>().ToArray();

            Assert.Equal(2, rows.Length);
            Assert.Equal(ReportKeys.Select(key => text[key]), rows[0].Keys);
            Assert.Equal(entry.Task, rows[0][text["Task"]]);
            Assert.Equal("kr", rows[0][text["Currency"]]);
            Assert.Equal(day.ToString("dddd", CultureInfo.GetCultureInfo(language.CultureName)), rows[0][text["Day"]]);
            Assert.Equal(175, Convert.ToDecimal(rows[0][text["OvertimeEarnings"]], CultureInfo.InvariantCulture));
            Assert.Equal(text["Total"], rows[1][text["Task"]]);
            Assert.Equal(250, Convert.ToDecimal(rows[1][text["Net"]], CultureInfo.InvariantCulture));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}

using VolturaEarner.Ui;
namespace VolturaEarner.Features.Settings;

public enum OvertimePolicy { SameRate, CustomRate, NotAllowed }

public sealed record AppSettings
{
    public string Language { get; init; } = "system";
    public decimal HourlyRate { get; init; } = 1000m;
    public decimal DailyCost { get; init; }
    public decimal DailyHours { get; init; } = 8m;
    public OvertimePolicy Overtime { get; init; } = OvertimePolicy.SameRate;
    public decimal CustomOvertimeRate { get; init; } = 1000m;
    [System.Text.Json.Serialization.JsonIgnore]
    public decimal OvertimeRate => Overtime == OvertimePolicy.CustomRate
        ? CustomOvertimeRate
        : HourlyRate;
    public string Currency { get; init; } = "USD";
    public string[] Tasks { get; init; } = ["Default task"];
    public string SelectedTask { get; init; } = "Default task";
    public bool SaveWorkLog { get; init; } = true;
    public bool AutoShowReport { get; init; }
    public bool StartWithWindows { get; init; }
    public bool MinimizeToTray { get; init; } = true;
    public bool AlwaysOnTop { get; init; }
    public bool ConfirmExit { get; init; } = true;
    public bool ShowProgress { get; init; } = true;
    public bool ShowTooltips { get; init; } = true;
    public bool PlaySounds { get; init; }
    public bool Logging { get; init; }
    public bool ShowErrors { get; init; } = true;
    public bool AutomaticUpdates { get; init; } = true;
    public bool FirstRun { get; init; } = true;
    public string Theme { get; init; } = "system";
    public string ExportDirectory { get; init; } = "";
    public string WorkDirectory { get; init; } = "";

    public void Validate()
    {
        if (!VolturaEarner.Features.Localization.LanguageCatalog.IsSupported(Language))
        {
            throw new InvalidDataException(Strings.Current["UnknownLanguage"]);
        }

        if (!Enum.IsDefined(Overtime) || CustomOvertimeRate < 0 || CustomOvertimeRate > 1000000000m)
        {
            throw new InvalidDataException(Strings.Current["ChooseAnOvertimeOptionAndARateOf0OrMore"]);
        }

        if (HourlyRate < 0 || HourlyRate > 1000000000m || DailyCost < 0 || DailyCost > 1000000000m || DailyHours <= 0 || DailyHours > 24)
        {
            throw new InvalidDataException(Strings.Current["EnterHoursAbove0Max24AndRatesCostsOf0OrMore"]);
        }

        if (Currency is null || Currency.Length > 3 || Currency.Any(char.IsControl) || Tasks is null || Tasks.Length is < 1 or > 40 || Tasks.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 120 || t.Any(char.IsControl)) || Tasks.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Tasks.Length || !Tasks.Contains(SelectedTask, StringComparer.Ordinal))
        {
            throw new InvalidDataException(Strings.Current["Use140UniqueTasksAndACurrencyLabelOfUpTo3Characters"]);
        }

        if (Theme is not ("system" or "light" or "dark"))
        {
            throw new InvalidDataException(Strings.Current["UnknownTheme"]);
        }

        foreach (var path in new[] { ExportDirectory, WorkDirectory })
        {
            if (path is null || (path.Length > 0 && !Path.IsPathFullyQualified(path)))
            {
                throw new InvalidDataException(Strings.Current["ChooseAnAbsoluteFolderPath"]);
            }
        }
    }
}

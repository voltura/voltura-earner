using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using VolturaEarner.Features.Settings;
using VolturaEarner.Features.Tracking;
using VolturaEarner.Features.Reports;
using VolturaEarner.Platform;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace VolturaEarner.Ui;

internal static class EditorDialogs
{
    private static Window Create(Window owner, string title, StackPanel content)
    {
        var window = new Window { Owner = owner, Title = title, Width = 440, MaxHeight = 760, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new ScrollViewer { Focusable = false, Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Background = (System.Windows.Media.Brush)owner.FindResource("WindowBrush") };

        WindowWorkAreaPlacement.ConstrainAndCenterOnFirstLoad(window);
        TooltipLifetime.Attach(window);

        return window;
    }
    private static TextBox Field(StackPanel panel, string label, string value)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 4) });

        var input = new TextBox { Text = value, MinHeight = 36, VerticalContentAlignment = VerticalAlignment.Center };

        System.Windows.Automation.AutomationProperties.SetName(input, label);
        panel.Children.Add(input);

        return input;
    }
    internal static AppSettings Tasks(Window owner, AppSettings settings)
    {
        var window = new TaskManagerWindow(settings) { Owner = owner };

        window.ShowDialog();

        return window.Result;
    }

    internal static WorkEntry? Entry(Window owner, AppSettings settings, WorkEntry? original)
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        var window = Create(owner, original is null
            ? Strings.Current["AddWorkEntry"]
            : Strings.Current["EditWorkEntry"], panel);
        var date = Field(panel, Strings.Current["DateYyyyMMDd"], (original?.Date ?? DateOnly.FromDateTime(DateTime.Today)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var task = Field(panel, Strings.Current["Task"], original?.Task ?? settings.SelectedTask);
        var regular = Field(panel, Strings.Current["RegularHours"], FormatHours(original?.RegularTicks ?? 0));
        var overtime = Field(panel, Strings.Current["OvertimeHours"], FormatHours(original?.OvertimeTicks ?? 0));
        var rate = Field(panel, Strings.Current["HourlyRate"], (original?.HourlyRate ?? settings.HourlyRate).ToString(CultureInfo.CurrentCulture));
        var extraRate = Field(panel, Strings.Current["OvertimeHourlyRate"], (original?.EffectiveOvertimeRate ?? settings.OvertimeRate).ToString(CultureInfo.CurrentCulture));
        var currency = Field(panel, Strings.Current["Currency"], original?.Currency ?? settings.Currency);
        var initialEarned = original?.Earned.ToString("0.##", CultureInfo.CurrentCulture) ?? "";
        var earned = Field(panel, Strings.Current["GrossEarnedBlankToCalculate"], initialEarned);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };

        panel.Children.Add(error);

        var buttons = new WrapPanel();

        panel.Children.Add(buttons);

        var save = new Button { Content = Strings.Current["SaveEntry"], Width = double.NaN, MinWidth = 160, IsDefault = true, Margin = new Thickness(0, 0, 10, 0) };
        var cancel = new Button { Content = Strings.Current["Cancel"], Width = double.NaN, MinWidth = 118, IsCancel = true };

        ButtonIcon.SetGlyph(save, "\uE74E");
        ButtonIcon.SetGlyph(cancel, "\uE711");
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        WorkEntry? result = null;

        save.Click += (_, _) =>
        {
            if (!DateOnly.TryParseExact(date.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) || !decimal.TryParse(regular.Text, out var hours) || !decimal.TryParse(overtime.Text, out var extra) || !decimal.TryParse(rate.Text, out var hourly) || hours is < 0 or > 24 || extra is < 0 or > 24 || hours + extra > 24 || hourly < 0 || hourly > 1000000000m)
            {
                error.Text = Strings.Current["CheckTheDateHours024TotalAndRate0OrMore"];

                return;
            }

            if (!decimal.TryParse(extraRate.Text, out var overtimeHourly) || overtimeHourly < 0 || overtimeHourly > 1000000000m)
            {
                error.Text = Strings.Current["EnterAnOvertimeRateOf0OrMore"];

                return;
            }

            var regularTicks = ParseHours(regular.Text, hours, original?.RegularTicks);
            var overtimeTicks = ParseHours(overtime.Text, extra, original?.OvertimeTicks);
            var regularAmount = (decimal)regularTicks / TimeSpan.TicksPerHour * hourly;
            var overtimeAmount = (decimal)overtimeTicks / TimeSpan.TicksPerHour * overtimeHourly;
            var calculated = regularAmount + overtimeAmount;
            var amount = calculated;

            if ((!string.IsNullOrWhiteSpace(earned.Text) && !decimal.TryParse(earned.Text, out amount)) || amount is < 0 or > 24000000000m)
            {
                error.Text = Strings.Current["EnterEarningsOrLeaveBlankToCalculate"];

                return;
            }

            try
            {
                if (original is not null && earned.Text == initialEarned)
                {
                    amount = original.Earned;
                }

                result = new(original?.Id ?? Guid.NewGuid(), day, task.Text.Trim(), currency.Text.Trim(), hourly, regularTicks, overtimeTicks, amount, calculated > 0
                    ? amount * overtimeAmount / calculated
                    : hours + extra > 0
                        ? amount * extra / (hours + extra)
                        : 0, overtimeHourly);

                if (original is not null && regularTicks == original.RegularTicks && overtimeTicks == original.OvertimeTicks && hourly == original.HourlyRate && overtimeHourly == original.EffectiveOvertimeRate && amount == original.Earned)
                {
                    result = result with { OvertimeEarned = original.OvertimeEarned };
                }

                result.Validate();
                window.DialogResult = true;
            }
            catch (InvalidDataException exception)
            {
                error.Text = exception.Message;
            }
        };

        return window.ShowDialog() == true
            ? result
            : null;
    }

    internal static string FormatHours(long ticks) => ((decimal)ticks / TimeSpan.TicksPerHour).ToString("0.####", CultureInfo.CurrentCulture);
    internal static long ParseHours(string text, decimal hours, long? original) => original.HasValue && text.Trim() == FormatHours(original.Value)
        ? original.Value
        : (long)(hours * TimeSpan.TicksPerHour);

    internal static WorkEntry? SelectEntry(Window owner, WorkLogGroup group, bool deleting)
    {
        var panel = new StackPanel { Margin = new(24) };
        var window = Create(owner, deleting
            ? Strings.Current["Delete"]
            : Strings.Current["EditWorkEntry"], panel);

        panel.Children.Add(new TextBlock { Text = group.Task, FontSize = 18, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = group.Date, Margin = new(0, 4, 0, 12) });

        var list = new ListBox { MinHeight = 120, MaxHeight = 320, SelectedIndex = 0 };

        foreach (var entry in group.Entries)
        {
            var text = $"{ReportService.Duration(entry.TotalTicks)} · {entry.Earned:N2} {entry.Currency}\n{Strings.Current["HourlyRate"]}: {entry.HourlyRate:0.##} · {Strings.Current["OvertimeHourlyRate"]}: {entry.EffectiveOvertimeRate:0.##}";

            list.Items.Add(new ListBoxItem { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, Tag = entry, Padding = new(10, 8, 10, 8) });
        }

        list.SelectedIndex = 0;
        panel.Children.Add(list);

        var actions = new WrapPanel { Margin = new(0, 16, 0, 0) };
        var confirm = new Button
        {
            Content = deleting
                ? Strings.Current["Delete"]
                : Strings.Current["EditMore"],
            MinWidth = 118,
            Margin = new(0, 0, 8, 8),
            IsDefault = true
        };
        var cancel = new Button { Content = Strings.Current["Cancel"], MinWidth = 118, IsCancel = true, Margin = new(0, 0, 0, 8) };

        ButtonIcon.SetGlyph(confirm, deleting
            ? "\uE74D"
            : "\uE70F");
        ButtonIcon.SetGlyph(cancel, "\uE711");
        confirm.Click += (_, _) =>
        {
            if (list.SelectedItem is not null)
            {
                window.DialogResult = true;
            }
        };

        actions.Children.Add(confirm);
        actions.Children.Add(cancel);
        panel.Children.Add(actions);

        return window.ShowDialog() == true
            ? (WorkEntry)((ListBoxItem)list.SelectedItem).Tag
            : null;
    }
}

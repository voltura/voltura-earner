using System.Windows;
using System.Windows.Controls;
using VolturaEarner.Features.Settings;
using VolturaEarner.Platform;

namespace VolturaEarner.Ui;

internal sealed class TaskManagerWindow : Window
{
    private readonly AppSettings _settings;
    private readonly List<string> _tasks;
    private string _activeTask;
    internal ListBox TasksList { get; } = new() { MinHeight = 150, MaxHeight = 260 };
    internal TextBox TaskName { get; } = new() { MaxLength = 120, MinHeight = 38 };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    internal AppSettings Result => _settings with { Tasks = _tasks.ToArray(), SelectedTask = _activeTask };

    internal TaskManagerWindow(AppSettings settings)
    {
        _settings = settings;
        _tasks = settings.Tasks.ToList();
        _activeTask = settings.SelectedTask;
        Title = Strings.Current["ManageTasks"];
        Width = 440;
        MaxHeight = 760;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBrush");

        var panel = new StackPanel { Margin = new(24) };

        Content = new ScrollViewer { Focusable = false, Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        TasksList.ItemsSource = _tasks;
        TasksList.SelectedItem = _activeTask;
        panel.Children.Add(TasksList);
        panel.Children.Add(new TextBlock { Text = Strings.Current["TaskName"], Margin = new(0, 12, 0, 4) });
        System.Windows.Automation.AutomationProperties.SetName(TaskName, Strings.Current["TaskName"]);
        panel.Children.Add(TaskName);
        panel.Children.Add(_error);

        var actions = new WrapPanel { Margin = new(0, 12, 0, 0) };

        panel.Children.Add(actions);

        foreach (var key in new[] { "Add", "Remove", "UseTask", "Close" })
        {
            var button = new Button { Content = Strings.Current[key], MinWidth = 118, Margin = new(0, 4, 6, 4), IsCancel = key == "Close" };

            ButtonIcon.SetGlyph(button, key switch { "Add" => "\uE710", "Remove" => "\uE74D", "UseTask" => "\uE73E", _ => "\uE711" });
            actions.Children.Add(button);
            button.Click += (_, _) =>
            {
                switch (key)
                {
                    case "Add":
                        AddTask(TaskName.Text);
                        break;
                    case "Remove":
                        RemoveSelected();
                        break;
                    case "UseTask":
                        UseSelected();
                        Close();
                        break;
                    case "Close":
                        Close();
                        break;
                }
            };
        }

        WindowWorkAreaPlacement.ConstrainAndCenterOnFirstLoad(this);
        TooltipLifetime.Attach(this);
    }

    internal bool AddTask(string name)
    {
        var task = name.Trim();

        if (task.Length is < 1 or > 120 || task.Any(char.IsControl) || _tasks.Count >= 40 || _tasks.Contains(task, StringComparer.OrdinalIgnoreCase))
        {
            _error.Text = Strings.Current["EnterAUniqueTask1120CharactersMaximum40Tasks"];

            return false;
        }

        _tasks.Add(task);
        TasksList.Items.Refresh();
        TasksList.SelectedItem = task;
        TaskName.Clear();
        _error.Text = "";

        return true;
    }

    internal void RemoveSelected()
    {
        if (_tasks.Count == 1)
        {
            _error.Text = Strings.Current["KeepAtLeastOneTask"];

            return;
        }

        if (TasksList.SelectedItem is not string selected)
        {
            return;
        }

        _tasks.Remove(selected);

        if (_activeTask == selected)
        {
            _activeTask = _tasks[0];
        }

        TasksList.Items.Refresh();
        TasksList.SelectedItem = _activeTask;
        _error.Text = "";
    }

    internal void UseSelected()
    {
        if (TasksList.SelectedItem is string selected)
        {
            _activeTask = selected;
        }
    }
}

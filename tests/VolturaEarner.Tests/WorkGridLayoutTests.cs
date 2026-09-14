using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using VolturaEarner.Features.Localization;
using VolturaEarner.Features.Reports;
using VolturaEarner.Features.Settings;
using VolturaEarner.Features.Tracking;
using VolturaEarner.Ui;

namespace VolturaEarner.Tests;

[Collection("WPF")]
public sealed class WorkGridLayoutTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("light", 708, 764)]
    [InlineData("dark", 708, 764)]
    [InlineData("light", 528, 484)]
    [InlineData("dark", 528, 484)]
    public void ColumnsAlignAndFitAndSelectionUsesAppColors(string theme, int width, int height)
    {
        fixture.Run(() =>
        {
            var window = new MainWindow();
            var day = new DateOnly(2026, 9, 14);
            var data = WorkLogGroup.Create([new WorkEntry(Guid.NewGuid(), day, "Product design", "SEK", 650, 3 * TimeSpan.TicksPerHour, 0, 1950, 0)]);

            try
            {
                ThemeManager.Apply(theme);
                Assert.False(window.WorkGrid.CanUserReorderColumns);
                Assert.False(window.WorkGrid.Columns[4].CanUserSort);
                Assert.True(window.WorkGrid.Columns[0].CanUserSort);

                foreach (var language in LanguageCatalog.All)
                {
                    Strings.Current.SetLanguage(language.Id);
                    window.Populate(new AppSettings { Language = language.Id });
                    window.Pages.SelectedIndex = 1;
                    window.WorkGrid.ItemsSource = data;
                    window.WorkTotals.DataContext = WorkLogTotals.Create(data.SelectMany(group => group.Entries).ToArray());
                    window.WorkGrid.SelectedIndex = 0;

                    var size = new Size(width, height);

                    window.RootSurface.Measure(size);
                    window.RootSurface.Arrange(new Rect(size));
                    window.RootSurface.UpdateLayout();
                    window.Dispatcher.Invoke(() => window.RootSurface.UpdateLayout(), DispatcherPriority.ContextIdle);

                    var scroll = Descendants<ScrollViewer>(window.WorkGrid).First();

                    Assert.True(scroll.ScrollableWidth < 1, $"{theme}/{language.Id}/{width}: overflow {scroll.ScrollableWidth}");

                    var row = (DataGridRow)window.WorkGrid.ItemContainerGenerator.ContainerFromIndex(0);
                    var headers = Descendants<DataGridColumnHeader>(window.WorkGrid).Where(header => header.Column is not null).ToArray();
                    var cells = Descendants<DataGridCell>(row).ToArray();

                    Assert.Equal(5, headers.Length);
                    Assert.Equal(5, cells.Length);

                    foreach (var cell in cells)
                    {
                        var header = headers.Single(header => header.Column == cell.Column);
                        var headerLeft = header.TranslatePoint(new(), window.WorkGrid).X;
                        var cellLeft = cell.TranslatePoint(new(), window.WorkGrid).X;

                        Assert.InRange(Math.Abs(headerLeft - cellLeft), 0, 1);
                        Assert.Same(Application.Current.FindResource("WindowBrush"), header.Background);
                        Assert.Same(Application.Current.FindResource("AccentBrush"), cell.Background);
                        Assert.Same(Application.Current.FindResource("AccentTextBrush"), cell.Foreground);
                    }

                    Assert.Same(Application.Current.FindResource("SurfaceBrush"), window.WorkGrid.Background);

                    foreach (var total in new[] { window.TotalTime, window.TotalOvertime, window.TotalEarned })
                    {
                        var column = Grid.GetColumn(total);
                        var header = headers.Single(header => header.Column == window.WorkGrid.Columns[column]);
                        var headerLeft = header.TranslatePoint(new(), window.RootSurface).X;
                        var totalLeft = total.TranslatePoint(new(), window.RootSurface).X - total.Margin.Left;

                        Assert.True(Math.Abs(headerLeft - totalLeft) <= 1, $"{language.Id}/{width}/column {column}: header {headerLeft}, total {totalLeft}");
                        Assert.True(total.TranslatePoint(new(0, total.ActualHeight), window.RootSurface).Y <= height);
                        Assert.Same(Application.Current.FindResource("TextBrush"), total.Foreground);
                    }
                }
            }
            finally
            {
                window.Close();
                Strings.Current.SetLanguage("en");
                ThemeManager.Apply("system");
            }
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}

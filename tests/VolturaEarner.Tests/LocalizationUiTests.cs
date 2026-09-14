using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using VolturaEarner.Features.Localization;
using VolturaEarner.Features.Settings;
using VolturaEarner.Ui;

namespace VolturaEarner.Tests;

[Collection("WPF")]
public sealed class LocalizationUiTests(WpfTestFixture fixture)
{
    [Fact]
    public async Task SavingLanguageRefreshesAboutAndPersistsTheChoice()
    {
        var root = Path.Combine(Path.GetTempPath(), "VolturaEarner-tests", Guid.NewGuid().ToString("N"));
        AppRuntime runtime = null!;
        Task operation = Task.CompletedTask;

        fixture.Run(() =>
        {
            runtime = new(new(root, false, true));
            operation = runtime.InitializeAsync();
        });
        await operation;

        try
        {
            fixture.Run(() =>
            {
                runtime.Window.LanguageChoice.SelectedValue = "sv";
                operation = runtime.ExecuteAsync("save-settings");
            });
            await operation;
            fixture.Run(() =>
            {
                runtime.Window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.Equal(Strings.Current["OpenDownloads"], runtime.Window.CheckUpdateButton.Content);
                Assert.Equal(Strings.Current["DownloadUpdatesManuallyForThisCopy"], runtime.Window.UpdateStatusText.Text);
                Assert.Equal("sv", runtime.Session.Settings.Language);
            });

            using var store = new JsonStore<AppSettings>(Path.Combine(root, "settings.json"), settings => settings.Validate());

            Assert.Equal("sv", (await store.LoadAsync(TestContext.Current.CancellationToken))!.Language);
        }
        finally
        {
            fixture.Run(() => operation = runtime.DisposeAsync().AsTask());
            await operation;
            fixture.Run(() =>
            {
                runtime.Window.Close();
                Strings.Current.SetLanguage("en");
            });

            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void SwitchingEveryLanguageUpdatesExistingControlsAndPreservesUserSettings()
    {
        fixture.Run(() =>
        {
            var window = new MainWindow();
            var settings = new AppSettings { Tasks = ["My custom task"], SelectedTask = "My custom task", Currency = "kr", HourlyRate = 123.45m };

            try
            {
                foreach (var language in LanguageCatalog.All)
                {
                    Strings.Current.SetLanguage(language.Id);
                    window.Populate(settings with { Language = language.Id });
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

                    var text = language.Entries;

                    Assert.Equal(text["Today"], ((TabItem)window.Pages.Items[0]).Header);
                    Assert.Equal(text["Preferences"], ((TabItem)window.Pages.Items[2]).Header);
                    Assert.Equal(text["SaveWorkHistory"], window.SaveLogCheck.Content);
                    Assert.Equal(text["ChooseACurrencyOrEnterUpTo3Characters"], window.CurrencyInput.ToolTip);
                    Assert.Equal(text["CurrencyLabel"], AutomationProperties.GetName(window.CurrencyInput));
                    Assert.Equal(text["Date"], window.WorkGrid.Columns[0].Header);
                    Assert.Equal(text["SwedishKrona"], window.CurrencyInput.Items.Cast<CurrencyChoice>().Single(currency => currency.Code == "SEK").Name);

                    var saved = window.ReadSettings(settings);

                    Assert.Equal(language.Id, saved.Language);
                    Assert.Equal(settings.Tasks, saved.Tasks);
                    Assert.Equal(settings.Currency, saved.Currency);
                    Assert.Equal(settings.HourlyRate, saved.HourlyRate);
                    window.CurrencyInput.ApplyTemplate();
                    window.CurrencyInput.SelectedItem = window.CurrencyInput.Items.Cast<CurrencyChoice>().Single(currency => currency.Code == "SEK");
                    Assert.Equal("SEK", window.ReadSettings(settings).Currency);
                }
            }
            finally
            {
                window.Close();
                Strings.Current.SetLanguage("en");
            }
        });
    }
}

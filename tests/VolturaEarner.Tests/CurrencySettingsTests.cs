using System.Windows;
using System.Windows.Controls;
using VolturaEarner.Features.Settings;

namespace VolturaEarner.Tests;

[Collection("WPF")]
public sealed class CurrencySettingsTests(WpfTestFixture fixture)
{
    [Fact]
    public void FreshSettingsUseUsdAndSelectingACurrencySavesOnlyItsCode()
    {
        fixture.Run(() =>
        {
            var window = new MainWindow();

            try
            {
                var settings = new AppSettings();

                window.Populate(settings);
                window.CurrencyInput.ApplyTemplate();
                Assert.Equal("USD", window.CurrencyInput.Text);
                window.CurrencyInput.SelectedItem = window.CurrencyInput.Items.Cast<CurrencyChoice>().Single(c => c.Code == "SEK");
                Assert.Equal("SEK", window.ReadSettings(settings).Currency);

                var editor = (TextBox)window.CurrencyInput.Template.FindName("PART_EditableTextBox", window.CurrencyInput);

                Assert.Equal(3, editor.MaxLength);
                Assert.Equal("SEK", editor.Text);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData("kr")]
    [InlineData("$")]
    [InlineData("")]
    public void ExistingAndTypedCustomLabelsRemainEditable(string label)
    {
        fixture.Run(() =>
        {
            var window = new MainWindow();

            try
            {
                var settings = new AppSettings { Currency = label };

                window.Populate(settings);
                window.CurrencyInput.ApplyTemplate();
                Assert.Equal(label, window.ReadSettings(settings).Currency);
                window.CurrencyInput.SelectedItem = window.CurrencyInput.Items.Cast<CurrencyChoice>().Single(c => c.Code == "USD");

                var editor = (TextBox)window.CurrencyInput.Template.FindName("PART_EditableTextBox", window.CurrencyInput);

                editor.Text = label;
                Assert.Equal(label, window.ReadSettings(settings).Currency);
                Assert.Equal(label, settings.Currency);
            }
            finally
            {
                window.Close();
            }
        });
    }
}

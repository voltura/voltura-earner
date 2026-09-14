using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using VolturaEarner.Features.Localization;

namespace VolturaEarner.Ui;

public sealed class Strings : INotifyPropertyChanged
{
    public static Strings Current { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    private LanguageDefinition _language = LanguageCatalog.All[0];
    public CultureInfo Culture => CultureInfo.GetCultureInfo(_language.CultureName);
    internal string LanguageId => _language.Id;
    public string this[string key] => key == "LanguageHeading"
        ? (_language.Id == "en"
            ? this["Language"]
            : this["Language"] + " / Language")
        : _language.Entries.TryGetValue(key, out var value)
            ? value
            : LanguageCatalog.All[0].Entries[key];
    public string Format(string key, params object?[] values) => string.Format(Culture, this[key], values);
    public void SetLanguage(string language)
    {
        _language = LanguageCatalog.Resolve(language, CultureInfo.CurrentUICulture);
        PropertyChanged?.Invoke(this, new("Item[]"));
    }
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{key}]") { Source = Strings.Current, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}

using System.Globalization;
using System.Collections.Frozen;
using System.Text.Json;

namespace VolturaEarner.Features.Localization;

internal sealed record LanguageDefinition(
    string Id,
    string NativeName,
    string CultureName
)
{
    private readonly Lazy<IReadOnlyDictionary<string, string>> _entries = new(() => LanguageCatalog.Load(Id));
    internal IReadOnlyDictionary<string, string> Entries => _entries.Value;
}

internal static class LanguageCatalog
{
    internal static IReadOnlyList<LanguageDefinition> All { get; } = Array.AsReadOnly<LanguageDefinition>(
    [
        new("en", "English", "en-GB"),
        new("sv", "Svenska", "sv-SE"),
        new("de", "Deutsch", "de-DE"),
        new("fr", "Français", "fr-FR"),
        new("da", "Dansk", "da-DK"),
        new("fi", "Suomi", "fi-FI"),
        new("is", "Íslenska", "is-IS"),
        new("nb", "Norsk bokmål", "nb-NO"),
        new("pl", "Polski", "pl-PL"),
        new("it", "Italiano", "it-IT"),
        new("es", "Español", "es-ES"),
        new("yue", "粵語", "zh-HK"),
        new("ja", "日本語", "ja-JP"),
        new("pt-BR", "Português (Brasil)", "pt-BR"),
        new("zh-Hans", "简体中文", "zh-CN"),
        new("zh-Hant", "繁體中文", "zh-TW"),
        new("nl", "Nederlands", "nl-NL"),
        new("ko", "한국어", "ko-KR"),
        new("ru", "Русский", "ru-RU"),
        new("tr", "Türkçe", "tr-TR"),
        new("id", "Bahasa Indonesia", "id-ID"),
    ]);

    internal static IReadOnlyDictionary<string, string> Load(string id)
    {
        using var stream = typeof(LanguageCatalog).Assembly.GetManifestResourceStream($"VolturaEarner.Features.Localization.Translations.{id}.json")
            ?? throw new InvalidOperationException($"Missing language resource: {id}");

        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!.ToFrozenDictionary(StringComparer.Ordinal);
    }

    internal static bool IsSupported(string? id) =>
        id == "system" || All.Any(language => language.Id == id);

    internal static LanguageDefinition Resolve(string id, CultureInfo windowsCulture)
    {
        if (id != "system")
        {
            return All.FirstOrDefault(language => language.Id == id) ?? All[0];
        }

        var parts = windowsCulture.Name.Split('-');
        var root = parts[0].ToLowerInvariant();

        bool Has(string part) => parts.Contains(part, StringComparer.OrdinalIgnoreCase);

        var resolved = root switch
        {
            "yue" => "yue",
            "zh" when Has("HK") => "yue",
            "zh" when Has("Hant") || Has("CHT") => "zh-Hant",
            "zh" when Has("Hans") || Has("CHS") => "zh-Hans",
            "zh" when Has("TW") || Has("MO") => "zh-Hant",
            "zh" => "zh-Hans",
            "no" or "nb" => "nb",
            "pt" => "pt-BR",
            _ => root,
        };

        for (var culture = windowsCulture; ; culture = culture.Parent)
        {
            var match = All.FirstOrDefault(language => language.Id == resolved);

            if (match is not null)
            {
                return match;
            }

            if (culture.Equals(CultureInfo.InvariantCulture))
            {
                return All[0];
            }

            resolved = culture.Parent.Name;
        }
    }
}

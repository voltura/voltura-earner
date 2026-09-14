namespace VolturaEarner.Features.Settings;

internal sealed record CurrencyChoice(string Code, string Name)
{
    internal static IReadOnlyDictionary<string, string> NameKeys { get; } = new Dictionary<string, string>
    {
        ["AUD"] = "AustralianDollar",
        ["BRL"] = "BrazilianReal",
        ["CAD"] = "CanadianDollar",
        ["CHF"] = "SwissFranc",
        ["CNY"] = "ChineseYuan",
        ["CZK"] = "CzechKoruna",
        ["DKK"] = "DanishKrone",
        ["EUR"] = "Euro",
        ["GBP"] = "BritishPound",
        ["HKD"] = "HongKongDollar",
        ["HUF"] = "HungarianForint",
        ["IDR"] = "IndonesianRupiah",
        ["INR"] = "IndianRupee",
        ["ISK"] = "IcelandicKrNa",
        ["JPY"] = "JapaneseYen",
        ["KRW"] = "SouthKoreanWon",
        ["NOK"] = "NorwegianKrone",
        ["NZD"] = "NewZealandDollar",
        ["PLN"] = "PolishZOty",
        ["RUB"] = "RussianRuble",
        ["SEK"] = "SwedishKrona",
        ["SGD"] = "SingaporeDollar",
        ["TRY"] = "TurkishLira",
        ["TWD"] = "NewTaiwanDollar",
        ["USD"] = "USDollar",
        ["ZAR"] = "SouthAfricanRand",
    };

    // Offline suggestions cover the installer languages and common currencies.
    // Names identify currencies, since one currency can be used in many countries.
    internal static IReadOnlyList<CurrencyChoice> All { get; } = Array.AsReadOnly<CurrencyChoice>([
        new("AUD", "Australian Dollar"),
        new("BRL", "Brazilian Real"),
        new("CAD", "Canadian Dollar"),
        new("CHF", "Swiss Franc"),
        new("CNY", "Chinese Yuan"),
        new("CZK", "Czech Koruna"),
        new("DKK", "Danish Krone"),
        new("EUR", "Euro"),
        new("GBP", "British Pound"),
        new("HKD", "Hong Kong Dollar"),
        new("HUF", "Hungarian Forint"),
        new("IDR", "Indonesian Rupiah"),
        new("INR", "Indian Rupee"),
        new("ISK", "Icelandic Króna"),
        new("JPY", "Japanese Yen"),
        new("KRW", "South Korean Won"),
        new("NOK", "Norwegian Krone"),
        new("NZD", "New Zealand Dollar"),
        new("PLN", "Polish Złoty"),
        new("RUB", "Russian Ruble"),
        new("SEK", "Swedish Krona"),
        new("SGD", "Singapore Dollar"),
        new("TRY", "Turkish Lira"),
        new("TWD", "New Taiwan Dollar"),
        new("USD", "U.S. Dollar"),
        new("ZAR", "South African Rand"),
    ]);
}

# Languages

Earner offers the same 21 languages as WeekNumber: English, Swedish, German, French, Danish, Finnish, Icelandic, Norwegian Bokmål, Polish, Italian, Spanish, Cantonese, Japanese, Brazilian Portuguese, Simplified Chinese, Traditional Chinese, Dutch, Korean, Russian, Turkish, and Indonesian.

Choose a language in Preferences and select Save preferences. Follow Windows is the default; unsupported Windows languages use English. Changes apply without restarting. The Language heading and tray Exit action retain an English label alongside the selected language, as in WeekNumber.

Translations cover the main and tray windows, task and work-entry editors, preferences, tooltips, accessibility labels, confirmations, validation messages, update states, currency names, and Excel headings, weekdays, and totals. User-created task names and currency labels stay unchanged. Numbers and input fields follow Windows regional formatting; dates in work records use yyyy-MM-dd. Native Windows file/folder picker controls follow Windows. Original license texts and technical log details retain their original language.

The installer offers the same language list, with NSIS wizard translations and localized Earner messages. Installer and app language choices are independent, as in WeekNumber.

## Maintenance

App strings live in `apps/windows/Features/Localization/Translations`. Keep every language's keys and numbered placeholders aligned with `en.json`. Currency names derive from Unicode CLDR 48.2.1; attribution is in `THIRD-PARTY-NOTICES.md`.

The catalog loads only requested languages, with English as a fallback. Language changes update existing WPF bindings; there is no translation service or background language polling.

`scripts/verify.ps1` checks key coverage, literal source references, placeholders, installer parity, language resolution, live settings changes, and Excel exports in every language. The isolated `--render-review` mode renders screens at normal and compact sizes in both themes without opening windows. It includes 100%, 150%, and 200% output scales.

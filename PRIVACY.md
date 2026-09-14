# Privacy

Voltura Earner is a Windows time and earnings tracker from Voltura AB. No account is required. Tracking and reports work offline. The app does not upload tasks, earnings, settings, or logs and has no analytics or automatic crash reporting.

## Local data

- Installed copies store settings, work history, optional logs, setup diagnostics, and downloaded updates under `%LOCALAPPDATA%\Voltura\Earner`.
- Portable copies use `Data` beside `VolturaEarner.exe`.
- `settings.json` stores earnings preferences, task names, appearance, startup, and update preferences. `work.json` stores dated work records, rates, currencies, regular time, overtime, earnings, and each day's cost and target.
- A custom work folder can be selected in Preferences. Changing it copies current history to an empty destination; the old copy remains. If saving the preference fails, the new copy is retained and can be reused on retry if it still matches the current history.
- Excel reports are saved in the chosen export folder or a location you select. Reports may contain private task names and earnings.
- Optional `application.log` files record operational errors and rotate at approximately 1 MiB. Setup diagnostics can contain local paths. Logging is disabled by default.

Starting with Windows creates an application-specific entry in the current user's registry. Installation adds per-user uninstall registration and a Start menu shortcut. Existing legacy Earner data is not imported or modified.

## Network requests

Installed copies can check GitHub for updates, download a signed manifest and installer, and verify them locally. Automatic checks are enabled by default and can be disabled in About. You choose when to install. Requests include the application user-agent and normal network information such as your IP address. Work records and settings are not attached. Portable copies use manual downloads.

The standard installer may download Microsoft's .NET Desktop Runtime. The offline installer and portable ZIP include it. Opening product, release, issue, or donation links opens your browser; the destination's privacy policy applies. The project website may load Shields.io badges.

## Removing data

The uninstaller offers permanent removal of the default local data folder, including work history, reports, settings, logs, and downloaded updates stored there. Files outside that folder remain where you saved them. To remove retained data, exit the app and remove its data folder and any custom copies you no longer need. For a portable copy, remove its extracted folder. Review logs and screenshots before sharing them publicly. Report vulnerabilities using [SECURITY.md](SECURITY.md).

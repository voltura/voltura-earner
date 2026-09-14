# Validation

Run on Windows with the SDK pinned in `global.json`, PowerShell 7, and NSIS for packaging.

Verification, installer packaging, signing, and GitHub Release publication run locally. GitHub Pages deploys the website separately from `main:/` using the existing repository Pages settings.

```powershell
./scripts/format.ps1 -Check
./scripts/verify.ps1
./scripts/package.ps1 -SkipTests
./scripts/test-installer.ps1
./scripts/test-installer.ps1 -ShellPath "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
```

The xUnit project covers accounting, overtime, date boundaries, storage failure preservation, report periods and currencies, UI-dispatcher responsiveness, popup pin/drag eligibility, timer lifetime, updater validation, and display placement. It also checks all 21 languages, task-manager saving, close-to-tray tracking, tooltip cleanup, compact sizing, period totals with separate currencies, and DataGrid/footer alignment, overflow, and selection colors in both themes. Release tests exercise signing failures and publication ordering. Installer tests use owned fixtures under `artifacts/installer-tests`, including rollback, interrupted maintenance, full offline installation, and rejection of unowned destinations.

Release regression coverage also holds a work write open while settings change or a stopping boundary is reached, preserves automatic reports across a reset and later exit, exercises export ownership/cancellation and workbook-launch failure, and checks session-ending save success and timeout recovery without ending the real Windows session. Verify actual sign-out/shutdown in a disposable Windows test session before release, including a failed or slow save that declines shutdown.

## Isolated UI review

The isolated option takes a folder argument; it is not a standalone switch. Render review draws controls to files without showing windows or taking focus.

```powershell
dotnet run --project apps/windows/VolturaEarner.csproj -- --isolated-test-mode "$PWD/artifacts/manual-review"
dotnet run --project apps/windows/VolturaEarner.csproj -- --isolated-test-mode "$PWD/artifacts/render-data" --render-review "$PWD/artifacts/review"
```

Isolation suppresses normal startup registration, automatic update eligibility, tray registration, and destructive confirmation dialogs. Review images render real WPF controls. They are not substitutes for physical multi-monitor and accessibility checks.

Before release, manually exercise live tray toggle, header drag, pinning, Escape, outside clicks, task selection, and app shutdown. Check both themes, keyboard focus, text entry, checkbox states, rounded tooltips, 100/150/200 percent scaling, work-area changes, and sleep/resume. Verify standard and full wizard appearance and cancel before installation when reviewing only visuals. Use a clean VM for missing-runtime elevation and full uninstall/data-removal acceptance.

## Signing and preparing

```powershell
./scripts/initialize-signing-key.ps1 -PrivateKeyPath 'C:\private\Earner\update-private.pem' -ProtectGeneratedPassphrase
./scripts/release.ps1 -KeyPath 'C:\private\Earner\update-private.pem' -Version 1.0.0 -PrepareOnly
```

Initialize a key only when establishing or intentionally rotating release trust. Keep the encrypted private key and its passphrase file outside the checkout, and back them up securely. Commit only the embedded public key. `VOLTURA_EARNER_UPDATE_SIGNING_PASSPHRASE` can supply an existing key's passphrase. A locally protected passphrase file is bound to its Windows user account.

Run `scripts/release.ps1` locally; omitting `-PrepareOnly` publishes the release after validation. Review source changes and release notes first. Do not use `-NoTests` for a release without a separately verified test run of the same source.

# Contributing

Thanks for helping improve Voltura Earner. Useful contributions include reproducible bug reports, time and earnings correctness, accessibility, UI polish, and documentation fixes.

## Before opening a pull request

1. Open an issue before making a large feature change.
2. Keep each pull request focused on one problem.
3. Describe the resulting behavior and update relevant documentation.
4. Run checks appropriate to the change and report what you verified. See the [validation guide](docs/validation.md).

Preserve work history and keep accounting independent of rendering. Keep disk and export work off the dispatcher. Read [architecture](docs/architecture.md) before changing service ownership, update handling, or installation.

## Build and run

Use Windows, the .NET 10 SDK selected by `global.json` (10.0.401 with patch roll-forward), and PowerShell 7.6. Installer packaging also requires NSIS available at the path used by `scripts/package.ps1`.

```powershell
git clone https://github.com/voltura/voltura-earner.git
cd voltura-earner
./scripts/build.ps1
dotnet run --project apps/windows/VolturaEarner.csproj
```

The build script restores locked dependencies, builds Release, and runs the xUnit suite. Update and commit the relevant NuGet lock files when intentionally changing package dependencies.

```powershell
./scripts/package.ps1
./scripts/test-installer.ps1
```

Packages are written to `artifacts/publish`. Use the isolated review modes in the validation guide when testing UI or diagnostics without changing your normal profile. Keep generated artifacts, local settings, and signing credentials out of commits.

## C# formatting

- Keep empty executable blocks compact as `{ }`, including empty lambdas and catch bodies. Blocks containing comments retain their layout.
- Use braces on separate lines for nonempty executable code blocks, including single-statement `if` bodies. Simple auto-properties stay compact, such as `public string Name { get; }` or `public bool Enabled { get; set; }`. The syntax-based formatter enforces this distinction.
- Separate control-flow statements (`if`, `foreach`, `for`, `while`, and other statement blocks) from preceding code with a blank line. Also separate a completed block from the code following it. Enclosing braces do not require extra blank lines; keep connected clauses such as `else`, `catch`, and `finally` together.
- Put a blank line before `return` when a code statement precedes it. Do not insert a blank line between an opening brace and its first `return`.
- Separate variable declarations from executable statements with a blank line in both directions. Keep consecutive declarations together without intervening blank lines.
- Always place ternary `?` and `:` branches on aligned continuation lines, regardless of length. Indent nested ternaries one level further. Preserve comments and literals.
- Run `./scripts/format.ps1` to apply these rules automatically. `./scripts/format.ps1 -Check` validates them without editing files; the build and verification scripts run this check.

PowerShell scripts use the same brace and block-spacing conventions. Assignment groups are separated from executable statements. `scripts/format.ps1` formats and validates PowerShell too, using PSScriptAnalyzer 1.25.0 (`Install-Module PSScriptAnalyzer -RequiredVersion 1.25.0 -Scope CurrentUser`).

## License and community

Contributions are licensed under the project's [MIT License](LICENSE); no separate contributor license agreement is required. Project activity follows the [Code of Conduct](CODE_OF_CONDUCT.md).

Report vulnerabilities privately using [SECURITY.md](SECURITY.md), rather than including exploit details in public issues or pull requests.

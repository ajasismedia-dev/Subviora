# Development Guide

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| Windows | 10 (build 19041+) or 11, x64 | WPF, Windows Forms (tray icon) and future Windows Graphics Capture require Windows. |
| .NET SDK | 10.0.100 or later (feature roll-forward) | Pinned in [`global.json`](../global.json). A per-user install (`dotnet-install.ps1`) works; no admin required. |
| Git | 2.40+ | Line endings are normalised by [`.gitattributes`](../.gitattributes). |
| IDE (optional) | Visual Studio 2026 / Rider / VS Code + C# Dev Kit | Must support .NET 10. |

Administrator privileges are **not** required to build, test, or run Subviora.

## Build, test, run

```powershell
dotnet restore Subviora.sln
dotnet build Subviora.sln -c Release
dotnet test --solution Subviora.sln -c Release
dotnet run --project src/Subviora.Desktop -c Release
```

- Warnings are treated as errors (`TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`,
  `EnforceCodeStyleInBuild`), so a successful build is a warning-free build.
- `dotnet test` runs on **Microsoft.Testing.Platform** (opted in via `global.json`). On the .NET 10 SDK
  the solution must be passed with `--solution`.
- The desktop executable is `Subviora.exe` (`AssemblyName`), entry point `Subviora.Desktop.App`.

## Publish (framework-dependent, single file)

```powershell
dotnet publish src/Subviora.Desktop/Subviora.Desktop.csproj -c Release -r win-x64 `
  --self-contained false -p:PublishSingleFile=true -o artifacts/publish
```

Output: `Subviora.exe`, `appsettings.json`, and the `profiles/` folder. The target machine needs the
.NET 10 Windows Desktop Runtime. Installer and code signing are planned for stage 7 (see
[ROADMAP.md](ROADMAP.md)).

## Smart App Control (SAC) on development machines

On Windows 11 machines with **Smart App Control** (SAC) enabled, Code Integrity enforces kernel-level
reputation verification via the cloud-based Microsoft Intelligent Security Graph (ISG) (Event Viewer →
*Microsoft-Windows-CodeIntegrity/Operational*, Event ID 3077 / 3033). Unsigned DLLs without cloud
reputation fail to load with `FileLoadException` (HRESULT `0x800711C7`).

Key findings and developer workflow (AG-01.1):

- **Deterministic Hash Caching:** Code Integrity caches reputation decisions by file SHA256 hash. When
  `<Deterministic>true</Deterministic>` is set, rebuilding without assembly metadata changes produces
  identical hashes, perpetuating any transient negative reputation decision. Incrementing the version
  or assembly metadata produces a fresh hash that clears the cached deny.
- **Automated Tests:** All unit and architecture tests execute cleanly locally (`dotnet test --solution Subviora.sln -c Release`).
- **Continuous Integration:** GitHub Actions (`.github/workflows/ci.yml`) runs on standard Windows
  runners (`windows-latest`), building and running all tests on clean virtual environments.
- **Runtime Execution:** Both multi-file builds and single-file publish builds run cleanly.
- **Commercial Release:** Code signing with an Authenticode certificate (EV Certificate or Microsoft Azure
  Trusted Signing) will be integrated for public and Steam releases (stage 7).
- Do **not** disable SAC, Defender, or system security policies.

## Configuration

Sources, lowest to highest precedence:

1. `appsettings.json` next to the executable (shipped defaults).
2. `%LOCALAPPDATA%\Subviora\Config\settings.json` (optional, user-specific).
3. Environment variables prefixed with `SUBVIORA_` (`__` separates sections).

Unknown keys are rejected and every value is validated at startup
([`SubvioraOptionsValidator`](../src/Subviora.Infrastructure/Configuration/SubvioraOptionsValidator.cs)).
Invalid configuration stops startup with a dialog listing the problems; it is never silently ignored.

Example `settings.json`:

```json
{
  "Subviora": {
    "Logging": { "MinimumLevel": "Debug" },
    "Shell": { "CloseToTray": true }
  }
}
```

Environment variable examples:

```powershell
$env:SUBVIORA_Subviora__Logging__MinimumLevel = 'Debug'
$env:SUBVIORA_DATA_DIR = 'D:\SubvioraDevData'   # relocates Logs/Config/Profiles/Cache
```

`SUBVIORA_DATA_DIR` is intended for development and tests; production uses `%LOCALAPPDATA%\Subviora`.

## Logs

Logs are written as compact JSON (CLEF), one event per line, to
`%LOCALAPPDATA%\Subviora\Logs\subviora-YYYYMMDD.log` with size- and count-based retention. Unhandled
exceptions on the UI thread, background threads and unobserved tasks are logged before any dialog is
shown.

## Game profiles

- Built-in profiles: [`profiles/builtin`](../profiles/builtin), copied to the output directory.
- Schema: [`profiles/schema/game-profile.v1.schema.json`](../profiles/schema/game-profile.v1.schema.json).
- User profiles/overrides: `%LOCALAPPDATA%\Subviora\Profiles\*.json`.

To add a built-in profile:

1. Create `profiles/builtin/<game-id>.json` with `"$schema": "../schema/game-profile.v1.schema.json"` and
   `"schemaVersion": 1`.
2. Use normalised coordinates (`0..1`, relative to the client area, origin top-left).
3. Set `calibration.status` to `uncalibrated` until the regions are verified against real gameplay.
4. Run the tests; `FileSystemGameProfileProviderTests` loads every shipped profile and fails on any
   validation issue.

User profiles in `%LOCALAPPDATA%\Subviora\Profiles\*.json` are layered overrides: built-in profiles
remain immutable baselines while user files specify only customized fields and region operations (`replace`,
`upsert`, `remove`).

## Screen Capture and OCR Diagnostics

AG-02 adds diagnostic tooling accessible directly from the Subviora desktop UI under the
**Yakalama & OCR** navigation tab:

- **Window Selection:** Enumerate and attach to open game windows (automatically highlights GTA V if detected).
- **Interactive Capture:** Capture individual frames on demand or run the continuous background pipeline.
- **Diagnostic Screenshots:** Save full-resolution frames locally to `%LOCALAPPDATA%\Subviora\Screenshots`
  only when explicitly triggered via the "Ekran Görüntüsü Kaydet" button.
- **OCR Engine Testing:** Switch between Windows OCR and Tesseract 5.2 on live or test frames.
- **Benchmark Suite:** Run automated evaluations directly from the UI or via unit tests (`BenchmarkCalculationTests`)
  to compute Character Error Rate (CER), Word Error Rate (WER), Bounding Box IoU, and latency percentiles.

## Code conventions

- C# latest, nullable reference types enabled, file-scoped namespaces, `sealed` by default.
- Style is enforced by [`.editorconfig`](../.editorconfig) during build.
- `Subviora.Core` has no package or project references; dependency rules are verified by
  `Architecture/DependencyRuleTests`.
- Logging uses `[LoggerMessage]` source-generated methods.
- Exceptions are never swallowed: they are handled, logged, or surfaced to the user.
- All user-facing strings are Turkish. Code identifiers, logs, and technical docs are English.
- Package versions are managed centrally in [`Directory.Packages.props`](../Directory.Packages.props).
  Only add dependencies with licenses compatible with commercial distribution (MIT, Apache-2.0, BSD).

## Branching

- `main`: released/stable history.
- `develop`: integration branch; each AG task ends with a commit and push to `develop`.
- Never force-push or rewrite published history.

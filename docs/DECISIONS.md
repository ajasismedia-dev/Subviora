# Architecture Decision Records

Short ADR-style log of decisions made so far. Each entry has a status: Accepted, Superseded or Proposed.

---

## ADR-001: C# / .NET 10 and WPF for the management UI

**Status:** Accepted (AG-01)

**Context:** The product is Windows-only, needs deep Win32/DirectX interop for capture and overlay, and
needs a polished desktop UI.

**Decision:** Use C# on .NET 10 (LTS), targeting Windows 10/11 x64. WPF is used for the management
interface only.

**Consequences:** Mature tooling and good interop through `LibraryImport`. The UI is Windows-only, which
fits the product's scope.

## ADR-002: The overlay is not built with WPF

**Status:** Accepted (AG-01; implementation in stage 5)

**Decision:** The in-game subtitle overlay will be a native Win32 layered window rendered with
Direct2D/DirectWrite, in `Subviora.Overlay`, behind `IOverlayRenderer`.

**Rationale:** The overlay needs minimal GPU/CPU cost, click-through behaviour, must never steal focus,
and needs predictable topmost behaviour over games. WPF's composition and airspace model adds overhead
and gives less control.

## ADR-003: Modular projects with a dependency-free core

**Status:** Accepted (AG-01)

**Decision:** `Subviora.Core` holds contracts and domain models and has no package or project
references. Feature modules depend on Core only. Infrastructure (configuration, logging, paths) is
separate, and only the Desktop composition root wires everything together. The rules are enforced by
`Architecture/DependencyRuleTests`.

**Consequences:** OCR, translation, capture and overlay providers can be replaced without touching the
domain. Empty module projects exist only as boundaries; they contain no fake implementations.

## ADR-004: Generic host, Microsoft.Extensions DI and options

**Status:** Accepted (AG-01)

**Decision:** Use `Host.CreateEmptyApplicationBuilder` with `ValidateOnBuild`/`ValidateScopes`. Options
are bound with unknown-key detection (`ErrorOnUnknownConfiguration`) and validated at startup by
`SubvioraOptionsValidator`.

**Consequences:** Misconfiguration fails fast with a clear message instead of being silently ignored.

## ADR-005: Serilog with compact JSON (CLEF) file logging

**Status:** Accepted (AG-01)

**Decision:** Application code logs through `Microsoft.Extensions.Logging` using `[LoggerMessage]`
source generators. Serilog is the backend, writing rolling, size-limited compact JSON files under
`%LOCALAPPDATA%\Subviora\Logs`.

**Consequences:** Logs are structured and machine-readable. Serilog stays an infrastructure detail.

## ADR-006: Central package management and warnings as errors

**Status:** Accepted (AG-01)

**Decision:** Package versions are defined in `Directory.Packages.props`. Shared build settings live in
`Directory.Build.props`: nullable, `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, and
`EnforceCodeStyleInBuild`.

## ADR-007: Classic `.sln` format

**Status:** Accepted (AG-01)

**Decision:** Keep the classic `Subviora.sln` rather than `.slnx`, to stay compatible with the widest
range of IDEs and CI tooling. This may be revisited later.

## ADR-008: No MVVM framework

**Status:** Accepted (AG-01)

**Decision:** A minimal `ObservableObject` / `RelayCommand` is implemented in-house
(`Mvvm/ObservableObject.cs`).

**Rationale:** The UI surface is small, and avoiding the dependency keeps the shell simple. Adopting
CommunityToolkit.Mvvm (MIT) can be reconsidered if the UI grows.

## ADR-009: Tray icon via Windows Forms `NotifyIcon`

**Status:** Accepted (AG-01)

**Decision:** Enable `UseWindowsForms` only for `NotifyIcon`, with global usings for
`System.Windows.Forms` and `System.Drawing` removed to avoid type clashes with WPF. The icon is rendered
from the vector brand mark at runtime.

**Rationale:** This is reliable and part of the .NET Windows Desktop runtime, so no third-party
dependency is needed.

## ADR-010: xUnit v3 on Microsoft.Testing.Platform

**Status:** Accepted (AG-01)

**Decision:** Tests use xUnit v3. `global.json` opts into Microsoft.Testing.Platform for `dotnet test`.
The command is `dotnet test --solution Subviora.sln`.

## ADR-011: Versioned JSON game profiles with normalized coordinates

**Status:** Accepted (AG-01)

**Decision:**

- Profiles are JSON documents with an explicit `schemaVersion` (currently 1) and a published JSON
  Schema.
- The serializer checks the version before deserializing and rejects unknown properties.
- Regions use normalized `0..1` coordinates relative to the game's client area, so they are independent
  of resolution.
- Profiles carry a `calibration.status`. The shipped GTA V profile is `uncalibrated`.

## ADR-012: A user profile replaces the built-in profile as a whole

**Status:** Accepted (AG-01)

**Decision:** Built-in profiles ship read-only next to the executable. User profiles live in
`%LOCALAPPDATA%\Subviora\Profiles`. A user profile with the same `gameId` replaces the built-in one
entirely; fields are not merged.

**Rationale:** Field-level merging of regions is ambiguous, and whole-profile replacement is predictable.
The trade-off is that a user copy does not receive built-in updates automatically. This will be
revisited along with the calibration tooling (stage 6).

## ADR-013: Per-user, no-admin operation

**Status:** Accepted (AG-01)

**Decision:** The manifest uses `asInvoker`. All writable state lives under `%LOCALAPPDATA%\Subviora`.
`SUBVIORA_DATA_DIR` can relocate this for development and tests.

**Rationale:** No feature has demonstrated a need for elevation.

## ADR-014: Turkish UI strings inline for now

**Status:** Accepted (AG-01); resource-based localization proposed for a later stage

**Decision:** User-facing strings are Turkish and currently written inline in XAML and view models.
Technical identifiers, log messages and docs are English. Configuration and profile validation messages
are English technical text shown inside Turkish dialogs.

**Consequences:** This is fast to iterate on. Strings move to `.resx` resources before any additional UI
language is added.

## ADR-015: Smart App Control and code signing

**Status:** Accepted (AG-01 finding)

**Context:** On the AG-01 development machine, Windows 11 Smart App Control blocked some unsigned,
reputation-less project DLLs (`0x800711C7`). This broke multi-file runs and some test runs, while the
single-file publish ran correctly.

**Decision:**

- The app detects this failure and shows an explanatory dialog.
- Developers use single-file publish for manual runtime testing.
- Release binaries must be Authenticode-signed (stage 7).
- SAC must not be disabled as a workaround.

## ADR-016: Dependency licensing

**Status:** Accepted (AG-01)

Only dependencies with licenses compatible with commercial, closed-source distribution are allowed.

| Package | Version | License | Scope |
|---|---|---|---|
| Microsoft.Extensions.Hosting | 10.0.12 | MIT | Desktop |
| Microsoft.Extensions.Logging.Abstractions | 10.0.12 | MIT | Modules |
| Microsoft.Extensions.Options.ConfigurationExtensions | 10.0.12 | MIT | Infrastructure |
| Microsoft.Extensions.Configuration.Json | 10.0.12 | MIT | Infrastructure |
| Microsoft.Extensions.Configuration.EnvironmentVariables | 10.0.12 | MIT | Infrastructure |
| Serilog | 4.4.0 | Apache-2.0 | Infrastructure |
| Serilog.Extensions.Logging | 10.0.0 | Apache-2.0 | Infrastructure |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | Infrastructure |
| Serilog.Formatting.Compact | 3.0.0 | Apache-2.0 | Infrastructure |
| Microsoft.NET.Test.Sdk | 18.10.1 | MIT | Tests only |
| xunit.v3 | 4.0.2 | Apache-2.0 | Tests only |
| xunit.runner.visualstudio | 4.0.1 | Apache-2.0 | Tests only |

OCR and translation models will be evaluated for license compatibility before they are integrated.
Model weights are never committed to the repository.

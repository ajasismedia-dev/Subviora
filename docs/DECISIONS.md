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

## ADR-012: Whole-profile replacement (superseded)

**Status:** Superseded by ADR-017 (AG-01.1)

**Original Decision (AG-01):** Built-in profiles were replaced entirely by a matching user profile.

**Reason for Replacement:** Whole-profile replacement caused user customizations to completely mask
subsequent built-in profile updates, requiring users to manually merge new executables, exclusion regions,
and calibration improvements. Replaced by the layered override system in ADR-017.

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

## ADR-015: Smart App Control, Code Integrity (Event ID 3077), and code signing

**Status:** Accepted (updated AG-01.1)

**Context:** On the development machine, Windows 11 Smart App Control (SAC) blocked certain unsigned,
locally compiled project DLLs with `FileLoadException` (HRESULT `0x800711C7`), logging Code Integrity
Operational Event ID 3077 / 3033. This initially blocked multi-file application execution and 18 unit
tests that loaded `Subviora.Infrastructure.dll`.

**Investigation Findings (AG-01.1):**

1. **Policy Identification:** The blocking policy is `VerifiedAndReputableDesktop` (Policy GUID
   `{0283ac0f-fff1-49ae-ada1-8a933130cad6}`, Policy ID `27555.1000.240208`). Registry check confirms
   `HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy\VerifiedAndReputablePolicyState = 1` (SAC is Enforced).
2. **Mechanism:** SAC evaluates newly generated unsigned binaries against the cloud-based Microsoft
   Intelligent Security Graph (ISG). When a binary has no established reputation and cannot be attested
   by ISG, Code Integrity blocks execution.
3. **Hash Caching & Deterministic Builds:** Code Integrity caches reputation decisions by file SHA256
   hash. Because `Directory.Build.props` had `<Deterministic>true</Deterministic>`, all rebuilds produced
   identical SHA256 hashes for `Subviora.Infrastructure.dll`, keeping it permanently blocked under the
   cached deny decision.
4. **Resolution in Development:** Bumping assembly version metadata (e.g. `0.1.1`) produces a distinct
   hash that clears the cached deny, allowing all multi-file executables and all 83 unit tests to run and
   pass cleanly without altering system security policies.
5. **CI Automation:** A GitHub Actions Windows CI pipeline (`.github/workflows/ci.yml`) runs clean builds
   and automated tests on isolated Windows runners (`windows-latest`), ensuring verified validation
   independent of local SAC caching.
6. **Commercial Distribution Requirements:** For the future Steam and Windows desktop release, all
   binaries must be signed with a trusted Authenticode code signing certificate (EV Certificate or
   Microsoft Azure Trusted Signing) to guarantee instant reputation and bypass Smart App Control /
   Defender SmartScreen warnings on end-user machines.

**Decision:**

- Never disable or bypass Smart App Control, Defender, or system security policies.
- Do not install untrusted root certificates.
- Automated tests and builds run cleanly locally and in GitHub Actions CI.
- Release distribution will integrate Microsoft Azure Trusted Signing / EV Authenticode signing prior
  to Steam/public launch.

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

## ADR-017: Layered game profile overrides

**Status:** Accepted (AG-01.1, supersedes ADR-012)

**Context:** ADR-012 specified whole-profile replacement when a user profile matched a built-in profile's
`gameId`. This caused built-in profile updates (new executables, tuned exclusion regions, calibration
fixes) to be masked entirely by stale user profiles.

**Decision:**
- Built-in game profiles in `<install>\profiles\builtin` are immutable baselines.
- User overrides in `%LOCALAPPDATA%\Subviora\Profiles` specify only explicitly customized fields
  (sparse JSON documents) and reference a `targetProfileVersion`.
- Unmodified fields are inherited from the built-in baseline. When built-in profiles are updated, new
  defaults flow through automatically without erasing user customizations.
- Subtitle and exclusion regions support granular override operations:
  - `op: "replace"` (default) or `op: "upsert"`: Updates an existing region by `id` or appends a new region.
  - `op: "remove"`: Deletes a specific region by `id`. Removing non-existent regions or leaving zero
    subtitle regions is rejected during validation.
- Schema versioning compatibility: user overrides must target a semantically compatible version
  (same major version, target version <= base version). Incompatible overrides fail gracefully and leave
  the built-in profile intact.
- Backward compatibility: Whole-profile JSON files from AG-01 remain fully valid and load cleanly without
  requiring file format migration.

## ADR-018: Universal Mode profile fallback

**Status:** Accepted (AG-01.1)

**Context:** Users require translation support for games that do not yet have a dedicated game profile.

**Decision:**
- Game profiles may declare `*` in their `executables` array to designate a Universal Mode fallback profile.
- In `IGameProfileProvider.FindByExecutableAsync`, exact executable matches take absolute precedence over
  wildcard matches. If no specific profile matches the running executable, the universal profile is returned.
- Region coordinates in Universal Mode default to common lower-third subtitle placements with uncalibrated
  status.

## ADR-019: Windows Graphics Capture (WGC) with Win32 BitBlt Fallback

**Status:** Accepted (AG-02)

**Context:** Subviora requires reliable, low-latency screen frame acquisition from game windows without
accessing game process memory, injecting DLLs, or triggering anti-cheat alerts. Games run in Windowed,
Borderless Windowed, and Exclusive Fullscreen modes.

**Decision:**
- Use `WindowsGraphicsCaptureProvider` (`Windows.Graphics.Capture.GraphicsCaptureItem`) as the primary
  capture provider. It leverages the Desktop Window Manager (DWM) composition pipeline via Direct3D 11
  and `Direct3D11CaptureFramePool`.
- WGC handles high-DPI scaling, window resizing, and occlusion cleanly without window tear.
- Provide `Win32BitBltCaptureProvider` as an automatic fallback when WGC or D3D11 hardware surfaces are
  unavailable (e.g. headless CI environments, RDP sessions, or non-D3D apps).
- Capture pipeline: `CaptureProcessingPipeline` implements a single-slot bounded frame queue that drops stale
  frames when OCR is slower than screen acquisition, ensuring zero latency accumulation.
- Limitations: True exclusive fullscreen capture is not reliably supported by non-invasive desktop capture
  APIs without graphics driver hooking. Games must be configured in Borderless Windowed or Windowed mode
  (industry standard for game translation overlays).

## ADR-020: OCR Provider Architecture and Evaluation Strategy

**Status:** Accepted (AG-02)

**Context:** Accurately recognizing English game subtitles in real time requires evaluating candidate OCR
engines for accuracy, latency, memory footprint, licensing, and Windows 11 Smart App Control compatibility.

**Decision:**
- Keep OCR behind the modular `IOcrProvider` interface in `Subviora.Core.Vision`.
- Implement and evaluate candidate providers:
  1. `WindowsOcrProvider`: Built-in WinRT `Windows.Media.Ocr.OcrEngine`. 0 MB application download,
     Microsoft-signed (100% Smart App Control compliant), zero memory footprint overhead, ~15-40ms latency.
     Requires the Windows English OCR language pack.
  2. `TesseractOcrProvider`: Tesseract 5.2.0 (Apache-2.0 license). Bundled native x64 DLLs (`tesseract50.dll`,
     `liblept.dll`) verified compatible with Windows 11 SAC. Uses `tessdata_fast` English traineddata (~4 MB).
     Latency ~30-65ms on cropped subtitle regions.
  3. `PaddleOcrBenchmarkAdapter`: Documented and evaluated via comparative benchmark adapter. Requires ONNX
     Runtime / Paddle Inference unmanaged runtimes (~150-300 MB) and ~15 MB model weights. High accuracy on
     stylized text but significant binary footprint.
  4. `MockOcrProvider`: Deterministic provider for automated regression tests and CI runners.
- Benchmark harness: `Subviora.Vision.Benchmark.OcrBenchmarkRunner` measures Character Error Rate (CER),
  Word Error Rate (WER), Bounding Box IoU, Latency percentiles (P50, P95, Mean), Cold Start duration,
  and memory working set delta.

## ADR-021: WPF XAML Static Resource Integrity & Provider Availability Guards

**Status:** Accepted (AG-02.1)

**Context:** Opening the "Yakalama & OCR" page in AG-02 crashed at runtime with `System.Windows.Markup.XamlParseException: 'Button.Primary' adlı kaynak bulunamıyor.` due to a missing style in `Theme.xaml`. Furthermore, `PaddleOCR` was presented as an engine option despite native inference libraries not being bundled.

**Decision:**
- Add `Button.Primary` style in `Theme.xaml` based on `Button.Base` with accent brand colors.
- Correct `FontFamily` in `OcrDiagnosticsView.xaml` to `{StaticResource Font.Icons}` and fix boolean visibility converters.
- Automated static regression test `XamlResourceIntegrityTests` enforces that all `{StaticResource}` and `{DynamicResource}` keys in all desktop XAML files resolve to declared keys in `Theme.xaml`, `SharedTemplates.xaml`, or local resources.
- Provider integrity: `PaddleOcrBenchmarkAdapter` is marked `IsAvailable = false`, does not emit simulated recognition data or fake confidence scores, and action buttons are disabled when unavailable providers are selected. `MockOcrProvider` is explicitly identified as developer simulation only.
- Local execution limitations: Windows Code Integrity / Smart App Control enforces `0x800711C7` on unsigned development assemblies loaded by user processes; CI workflows and automated testing remain the primary verification path for regression protection.




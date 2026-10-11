# Roadmap

Subviora is delivered in seven stages. Each stage is broken into AG tasks; every AG task ends with
a validated commit pushed to `develop`. The stage numbers here are the ones shown in the
application's placeholder pages (for example, "Yol haritası: Aşama 4" on the Translation page).

| Stage | Name | Status |
|---|---|---|
| 1 | Foundation | **Delivered (AG-01 & AG-01.1)** |
| 2 | Capture and OCR Benchmark | **Delivered (AG-02)** |
| 3 | Offline translation and Smart UI Detection | Planned (AG-03) |
| 4 | Translation cache and offline optimization | Planned |
| 5 | Native subtitle overlay | Planned |
| 6 | Calibration tooling and optional online providers | Planned |
| 7 | Performance, signing, installer and Steam release | Planned |

```mermaid
flowchart LR
  S1["1 Foundation"] --> S2["2 Capture + OCR Benchmark"] --> S3["3 Offline Translation"] --> S4["4 Cache + Optimization"] --> S5["5 Native Overlay"] --> S6["6 Calibration + Online"] --> S7["7 Hardening + Steam"]
```

## Stage 1: Foundation (AG-01 & AG-01.1)

Delivered in AG-01 & AG-01.1:

- Solution structure, module boundaries and dependency rules (verified by tests).
- WPF management shell: dark theme, Turkish UI, navigation, honest status for features that are not implemented.
- Lifecycle: single instance, dependency injection (generic host), configuration loading and validation,
  structured logging, global exception handling, per-user data directories, tray icon.
- Module contracts: capture, OCR, UI classification, translation, overlay renderer, game profile provider.
- Game profile schema v1, loader and validator, layered user overrides, Universal Mode fallback, and GTA V sample.
- GitHub Actions CI pipeline (`.github/workflows/ci.yml`).

## Stage 2: Capture and OCR Benchmark (AG-02)

Delivered in AG-02:

- Safe, non-invasive Windows game window discovery (`Win32WindowDiscoveryService`) filtering invalid, cloaked,
  minimized, and tool windows. Automatically associates game profiles (GTA V) and supports Universal Mode.
- Windows Graphics Capture provider (`WindowsGraphicsCaptureProvider`) via Direct3D 11 surface pools with
  dynamic DPI and window resize adaptation.
- Win32 GDI / BitBlt fallback capture provider (`Win32BitBltCaptureProvider`) for non-D3D or headless environments.
- Asynchronous, single-slot bounded processing pipeline (`CaptureProcessingPipeline`) with proactive stale
  frame dropping and `SemaphoreSlim(1,1)` OCR concurrency control.
- Telemetry & diagnostics tracking (FPS, resolution, acquisition latency, OCR latency, CPU/memory, frame dropping).
- Diagnostic on-demand screenshot export to `%LOCALAPPDATA%\Subviora\Screenshots`.
- Modular OCR provider architecture (`IOcrProvider`) with implementations for Windows OCR (WinRT built-in, 0 MB),
  Tesseract 5.2 (Apache-2.0, SAC-compliant native binaries), PaddleOCR benchmark adapter, and Mock OCR.
- OCR Benchmark Harness (`Subviora.Vision.Benchmark.OcrBenchmarkRunner`) computing Character Error Rate (CER),
  Word Error Rate (WER), Bounding Box IoU, Latency percentiles (P50, P95, Mean), and memory delta.
- Dedicated desktop diagnostic view (**Yakalama & OCR**) with live window selection, manual frame capture,
  bounding-box preview, and benchmark trigger.

## Stage 3: OCR and Smart UI Detection

- First `IOcrProvider` implementation (offline, with a license suitable for commercial use).
- Region cropping from profile subtitle regions; exclusion regions applied.
- `IUiClassificationProvider`: a hybrid of profile rules and heuristics (position, stability, timing,
  styling) to separate subtitles from HUD, menu and phone text.
- Subtitle change detection, so unchanged text is not re-processed.

## Stage 4: Offline translation and cache

- First offline `ITranslationProvider` (English to Turkish). Model weights are downloaded or installed
  separately and are never committed to the repository.
- SQLite translation cache in `%LOCALAPPDATA%\Subviora\Cache`.
- Translation page becomes functional (provider status, cache statistics).

## Stage 5: Native subtitle overlay

- `IOverlayRenderer` implemented with Win32 and Direct2D/DirectWrite: a layered, click-through window
  that does not take focus.
- Applies profile overlay preferences (position, font scale, background opacity).
- Overlay page becomes functional.

## Stage 6: Calibration tooling and optional online providers

- In-app region calibration and profile editor; user overrides written to the user profile directory.
- Settings editing in the UI.
- Optional, opt-in online translation providers. API keys are stored per user with Windows DPAPI, never
  in the repository.
- Calibrated, verified GTA V Story Mode profile.

## Stage 7: Performance, signing, installer and Steam release

- Performance hardening: CPU and GPU budget, latency targets, long-session stability.
- Authenticode code signing for all binaries (this is also required for Smart App Control compatibility).
- Installer and update strategy; Steamworks integration and Steam distribution.
- Broader game compatibility beyond GTA V.

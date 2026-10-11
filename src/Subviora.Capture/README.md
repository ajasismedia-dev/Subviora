# Subviora.Capture

**Status: not implemented (AG-01 contains no capture code).**

Planned responsibility: implementations of `Subviora.Core.Capture.IScreenCaptureProvider`.

- Primary backend: Windows Graphics Capture (`Windows.Graphics.Capture`) via Direct3D 11.
- Target: a single game window (HWND), not the full desktop.
- Requires Windows 10 version 1903 (build 18362) or later; the target framework will be raised to a
  versioned Windows TFM (e.g. `net10.0-windows10.0.19041.0`) when the implementation lands.
- Must not require administrator privileges.

Allowed dependencies: `Subviora.Core` only (plus Windows SDK projections when implemented).

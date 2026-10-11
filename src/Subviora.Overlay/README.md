# Subviora.Overlay

**Status: not implemented (AG-01 contains no overlay code).**

Planned responsibility: the implementation of `Subviora.Core.Overlay.IOverlayRenderer`.

- Native Win32 layered, click-through, topmost window (`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`).
- Text rendering through Direct2D and DirectWrite for crisp, low-overhead output.
- Works with borderless-windowed games. Exclusive fullscreen support must be evaluated separately.
- Must not inject into game processes or hook graphics APIs.

The overlay is deliberately **not** a WPF window: WPF's composition overhead and airspace limitations
are unsuitable for a per-frame game overlay.

Allowed dependencies: `Subviora.Core` only (plus the selected Direct2D interop layer when implemented).

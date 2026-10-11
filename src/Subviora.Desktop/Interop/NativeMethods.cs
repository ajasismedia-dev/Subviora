using System.Runtime.InteropServices;

namespace Subviora.Desktop.Interop;

internal static partial class NativeMethods
{
    /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 20H1+ / Windows 11).</summary>
    public const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>DWMWA_BORDER_COLOR (Windows 11 build 22000+).</summary>
    public const int DwmwaBorderColor = 34;

    /// <summary>DWMWA_CAPTION_COLOR (Windows 11 build 22000+).</summary>
    public const int DwmwaCaptionColor = 35;

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint hIcon);
}

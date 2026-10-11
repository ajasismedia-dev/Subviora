using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace Subviora.Capture.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public int Width => Math.Max(0, Right - Left);
    public int Height => Math.Max(0, Bottom - Top);
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth;
    public int biHeight;
    public ushort biPlanes;
    public ushort biBitCount;
    public uint biCompression;
    public uint biSizeImage;
    public int biXPelsPerMeter;
    public int biYPelsPerMeter;
    public uint biClrUsed;
    public uint biClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFO
{
    public BITMAPINFOHEADER bmiHeader;
    public uint bmiColors;
}

[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    [PreserveSig]
    int CreateForWindow(nint window, in Guid iid, out nint result);

    [PreserveSig]
    int CreateForMonitor(nint monitor, in Guid iid, out nint result);
}

internal static class CaptureInterop
{
    public const int DWMWA_CLOAKED = 14;
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_APPWINDOW = 0x00040000;
    public const uint PW_RENDERFULLCONTENT = 0x00000002;
    public const uint SRCCOPY = 0x00CC0020;
    public const uint BI_RGB = 0;
    public const uint DIB_RGB_COLORS = 0;

    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);
    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextW(nint hWnd, [Out] char[] lpString, int nMaxCount);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    internal static extern bool GetClientRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    internal static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    internal static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleBitmap(nint hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(nint hdc, nint h);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(nint ho);

    [DllImport("gdi32.dll")]
    internal static extern bool BitBlt(nint hdc, int x, int y, int cx, int cy, nint hdcSrc, int x1, int y1, uint rop);

    [DllImport("user32.dll")]
    internal static extern bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);

    [DllImport("gdi32.dll")]
    internal static extern int GetDIBits(nint hdc, nint hbm, uint start, uint cLines, [Out] byte[] lpBits, ref BITMAPINFO pbmi, uint usage);

    [DllImport("d3d11.dll", PreserveSig = true)]
    private static extern int D3D11CreateDevice(
        nint pAdapter,
        int driverType,
        nint software,
        uint flags,
        nint pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,
        out nint ppDevice,
        out int pFeatureLevel,
        out nint ppImmediateContext);

    [DllImport("d3d11.dll", PreserveSig = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        int length,
        out nint hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsDeleteString(nint hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoGetActivationFactory(
        nint activatableClassId,
        in Guid iid,
        out nint factory);

    private static readonly Guid GraphicsCaptureItemInteropIid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");

    internal static GraphicsCaptureItem CreateItemForWindow(nint hWnd)
    {
        if (hWnd == nint.Zero || !IsWindow(hWnd))
        {
            throw new ArgumentException($"Geçersiz veya kapatılmış pencere tanıtıcısı (HWND: 0x{hWnd:X8}).", nameof(hWnd));
        }

        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        int hr = WindowsCreateString(className, className.Length, out nint hstringClassId);
        Marshal.ThrowExceptionForHR(hr);

        nint factoryPtr = nint.Zero;
        nint rawItem = nint.Zero;
        try
        {
            var interopGuid = GraphicsCaptureItemInteropIid;
            hr = RoGetActivationFactory(hstringClassId, in interopGuid, out factoryPtr);
            Marshal.ThrowExceptionForHR(hr);

            unsafe
            {
                nint* vtable = *(nint**)factoryPtr;
                // IUnknown (0: QI, 1: AddRef, 2: Release) -> 3: CreateForWindow
                var createForWindow = (delegate* unmanaged[Stdcall]<nint, nint, in Guid, out nint, int>)vtable[3];

                var itemGuid = GraphicsCaptureItemIid;
                hr = createForWindow(factoryPtr, hWnd, in itemGuid, out rawItem);
                Marshal.ThrowExceptionForHR(hr);
            }

            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(rawItem);
        }
        finally
        {
            if (rawItem != nint.Zero)
            {
                Marshal.Release(rawItem);
            }

            if (factoryPtr != nint.Zero)
            {
                Marshal.Release(factoryPtr);
            }

            _ = WindowsDeleteString(hstringClassId);
        }
    }

    internal static GraphicsCaptureItem CreateItemForMonitor(nint hMonitor)
    {
        if (hMonitor == nint.Zero)
        {
            throw new ArgumentException("Monitör tanıtıcısı sıfır olamaz.", nameof(hMonitor));
        }

        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        int hr = WindowsCreateString(className, className.Length, out nint hstringClassId);
        Marshal.ThrowExceptionForHR(hr);

        nint factoryPtr = nint.Zero;
        nint rawItem = nint.Zero;
        try
        {
            var interopGuid = GraphicsCaptureItemInteropIid;
            hr = RoGetActivationFactory(hstringClassId, in interopGuid, out factoryPtr);
            Marshal.ThrowExceptionForHR(hr);

            unsafe
            {
                nint* vtable = *(nint**)factoryPtr;
                // IUnknown (0: QI, 1: AddRef, 2: Release) -> 3: CreateForWindow, 4: CreateForMonitor
                var createForMonitor = (delegate* unmanaged[Stdcall]<nint, nint, in Guid, out nint, int>)vtable[4];

                var itemGuid = GraphicsCaptureItemIid;
                hr = createForMonitor(factoryPtr, hMonitor, in itemGuid, out rawItem);
                Marshal.ThrowExceptionForHR(hr);
            }

            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(rawItem);
        }
        finally
        {
            if (rawItem != nint.Zero)
            {
                Marshal.Release(rawItem);
            }

            if (factoryPtr != nint.Zero)
            {
                Marshal.Release(factoryPtr);
            }

            _ = WindowsDeleteString(hstringClassId);
        }
    }

    internal static IDirect3DDevice CreateDirect3DDevice()
    {
        const int D3D_DRIVER_TYPE_HARDWARE = 1;
        const int D3D_DRIVER_TYPE_WARP = 5;
        const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
        const uint D3D11_SDK_VERSION = 7;

        int hr = D3D11CreateDevice(
            nint.Zero,
            D3D_DRIVER_TYPE_HARDWARE,
            nint.Zero,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            nint.Zero,
            0,
            D3D11_SDK_VERSION,
            out nint d3dDevice,
            out _,
            out nint immediateContext);

        if (hr < 0)
        {
            // Fallback to WARP software renderer (VMs / CI environments)
            hr = D3D11CreateDevice(
                nint.Zero,
                D3D_DRIVER_TYPE_WARP,
                nint.Zero,
                D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                nint.Zero,
                0,
                D3D11_SDK_VERSION,
                out d3dDevice,
                out _,
                out immediateContext);
        }

        Marshal.ThrowExceptionForHR(hr);

        try
        {
            hr = CreateDirect3D11DeviceFromDXGIDevice(d3dDevice, out nint winrtDevice);
            Marshal.ThrowExceptionForHR(hr);
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(winrtDevice);
        }
        finally
        {
            if (immediateContext != nint.Zero)
            {
                Marshal.Release(immediateContext);
            }

            if (d3dDevice != nint.Zero)
            {
                Marshal.Release(d3dDevice);
            }
        }
    }
}

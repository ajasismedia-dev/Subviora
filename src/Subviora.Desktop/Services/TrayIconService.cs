using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Subviora.Desktop.Interop;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Subviora.Desktop.Services;

/// <summary>
/// Notification-area (system tray) icon. Uses Windows Forms' NotifyIcon, which ships with the
/// Windows Desktop runtime, so no third-party dependency is required.
/// Must be created, used and disposed on the UI thread.
/// </summary>
public sealed partial class TrayIconService : IDisposable
{
    private const int IconSize = 32;

    private readonly ILogger<TrayIconService> _logger;
    private Forms.NotifyIcon? _notifyIcon;
    private Drawing.Icon? _icon;

    public TrayIconService(ILogger<TrayIconService> logger)
    {
        _logger = logger;
    }

    public bool IsVisible => _notifyIcon?.Visible == true;

    /// <summary>Creates and shows the tray icon.</summary>
    /// <param name="brandMark">Vector brand mark used to render the icon.</param>
    /// <param name="showWindow">Invoked when the user asks to show the main window.</param>
    /// <param name="exitApplication">Invoked when the user chooses to exit.</param>
    public void Show(ImageSource brandMark, Action showWindow, Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(brandMark);
        ArgumentNullException.ThrowIfNull(showWindow);
        ArgumentNullException.ThrowIfNull(exitApplication);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_notifyIcon is not null)
        {
            return;
        }

        _icon = CreateIcon(brandMark);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Subviora'yı göster", null, (_, _) => showWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => exitApplication());

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "Subviora",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => showWindow();
        LogTrayIconShown();
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _icon?.Dispose();
        _icon = null;
    }

    /// <summary>Renders the WPF vector brand mark into a GDI icon so tray and window share one design.</summary>
    private static Drawing.Icon CreateIcon(ImageSource source)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(source, new Rect(0, 0, IconSize, IconSize));
        }

        var bitmap = new RenderTargetBitmap(IconSize, IconSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;

        using var gdiBitmap = new Drawing.Bitmap(stream);
        var handle = gdiBitmap.GetHicon();
        try
        {
            using var borrowed = Drawing.Icon.FromHandle(handle);

            // Clone so the returned icon owns its own handle; the temporary handle is destroyed below.
            return (Drawing.Icon)borrowed.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tray icon shown")]
    private partial void LogTrayIconShown();
}

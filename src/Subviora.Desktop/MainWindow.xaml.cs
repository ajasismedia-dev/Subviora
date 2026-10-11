using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Subviora.Desktop.Interop;
using Subviora.Desktop.Services;
using Subviora.Desktop.ViewModels;
using Subviora.Infrastructure.Configuration;

namespace Subviora.Desktop;

public sealed partial class MainWindow : Window
{
    // COLORREF values (0x00BBGGRR) matching Color.Background / Color.Border in Theme.xaml.
    private const int CaptionColor = 0x0012100F;
    private const int BorderColor = 0x00302A26;

    private readonly ShellSettings _shell;
    private readonly TrayIconService _tray;
    private readonly ILogger<MainWindow> _logger;
    private bool _exitRequested;

    public MainWindow(MainViewModel viewModel, IOptions<SubvioraOptions> options, TrayIconService tray, ILogger<MainWindow> logger)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(options);
        _shell = options.Value.Shell;
        _tray = tray;
        _logger = logger;

        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => ApplyWindowFrameColors();
    }

    /// <summary>Closes the window for real (bypassing close-to-tray), which ends the application.</summary>
    public void CloseForExit()
    {
        _exitRequested = true;
        Close();
    }

    /// <summary>Restores and activates the window (e.g. from the tray icon).</summary>
    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!_exitRequested && _shell.CloseToTray && _tray.IsVisible)
        {
            e.Cancel = true;
            Hide();
            LogHiddenToTray();
        }

        base.OnClosing(e);
    }

    private void ApplyWindowFrameColors()
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        // Dark title bar is supported on Windows 10 20H1+; caption/border colours on Windows 11 only.
        // Failure HRESULTs on older builds are expected and leave the default frame in place.
        var dark = 1;
        var hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        LogDwmResult("UseImmersiveDarkMode", hr);

        var caption = CaptionColor;
        hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaCaptionColor, ref caption, sizeof(int));
        LogDwmResult("CaptionColor", hr);

        var border = BorderColor;
        hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaBorderColor, ref border, sizeof(int));
        LogDwmResult("BorderColor", hr);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "DwmSetWindowAttribute {Attribute} returned HRESULT 0x{HResult:X8}")]
    private partial void LogDwmResult(string attribute, int hResult);

    [LoggerMessage(Level = LogLevel.Information, Message = "Main window hidden to tray")]
    private partial void LogHiddenToTray();
}

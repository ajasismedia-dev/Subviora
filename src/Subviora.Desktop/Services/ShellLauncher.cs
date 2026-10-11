using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace Subviora.Desktop.Services;

/// <summary>Opens folders in Windows Explorer and reports failures to the user.</summary>
public sealed partial class ShellLauncher
{
    private readonly ILogger<ShellLauncher> _logger;

    public ShellLauncher(ILogger<ShellLauncher> logger)
    {
        _logger = logger;
    }

    public void OpenFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            Directory.CreateDirectory(path);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                ArgumentList = { path },
                UseShellExecute = false,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            LogOpenFailed(ex, path);
            MessageBox.Show(
                $"Klasör açılamadı:\n{path}\n\n{ex.Message}",
                "Subviora",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to open folder {Path}")]
    private partial void LogOpenFailed(Exception exception, string path);
}

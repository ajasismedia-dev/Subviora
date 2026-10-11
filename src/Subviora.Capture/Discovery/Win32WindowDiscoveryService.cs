using System.Diagnostics;
using Subviora.Capture.Interop;
using Subviora.Core.Capture;
using Subviora.Core.Profiles;

namespace Subviora.Capture.Discovery;

/// <summary>
/// Discovers top-level application and game windows using Win32 window management APIs.
/// Safe, non-invasive: does not read game process memory or inject code.
/// </summary>
public sealed class Win32WindowDiscoveryService : IWindowDiscoveryService
{
    private readonly IGameProfileProvider? _profileProvider;
    private readonly Func<IReadOnlyList<RawWindowCandidate>>? _rawWindowEnumerator;
    private static readonly HashSet<string> ExcludedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer",
        "subviora",
        "textinputhost",
        "applicationframehost",
        "shellexperiencehost",
        "taskhostw",
        "systemsettings",
        "dwm",
        "devenv",
        "searchhost",
        "startmenuexperiencehost"
    };

    public Win32WindowDiscoveryService(
        IGameProfileProvider? profileProvider = null,
        Func<IReadOnlyList<RawWindowCandidate>>? rawWindowEnumerator = null)
    {
        _profileProvider = profileProvider;
        _rawWindowEnumerator = rawWindowEnumerator;
    }

    public IReadOnlyList<WindowInfo> EnumerateEligibleWindows()
    {
        var rawCandidates = _rawWindowEnumerator is not null
            ? _rawWindowEnumerator()
            : CollectSystemWindows();

        int currentPid = Environment.ProcessId;
        var results = new List<WindowInfo>();

        foreach (var raw in rawCandidates)
        {
            if (!raw.IsVisible || raw.IsIconic || raw.IsCloaked || raw.IsToolWindow)
            {
                continue;
            }

            if (raw.Width < 100 || raw.Height < 100)
            {
                continue;
            }

            if (raw.ProcessId <= 0 || raw.ProcessId == currentPid)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(raw.Title))
            {
                continue;
            }

            if (ExcludedProcessNames.Contains(raw.ProcessName))
            {
                continue;
            }

            // Resolve game profile match
            bool isLikelyGame = false;
            string? matchedGameId = null;
            bool isUniversal = false;

            if (_profileProvider is not null)
            {
                try
                {
                    var profile = _profileProvider.FindByExecutableAsync(raw.ExecutableName, CancellationToken.None).GetAwaiter().GetResult();
                    if (profile is not null)
                    {
                        if (profile.IsUniversal)
                        {
                            isUniversal = true;
                            matchedGameId = profile.GameId;
                        }
                        else
                        {
                            isLikelyGame = true;
                            matchedGameId = profile.GameId;
                        }
                    }
                }
                catch
                {
                    // Fallback to heuristic checks on profile provider failure
                }
            }

            if (!isLikelyGame && !isUniversal)
            {
                // Heuristic check for common gaming executables
                string lowerExe = raw.ExecutableName.ToLowerInvariant();
                if (lowerExe.Contains("gta5") || lowerExe.Contains("game") || lowerExe.Contains("steam_app"))
                {
                    isLikelyGame = true;
                }
            }

            results.Add(new WindowInfo(
                Handle: raw.Handle,
                ProcessId: raw.ProcessId,
                ProcessName: raw.ProcessName,
                ExecutableName: raw.ExecutableName,
                Title: raw.Title,
                X: raw.X,
                Y: raw.Y,
                Width: raw.Width,
                Height: raw.Height,
                IsLikelyGame: isLikelyGame,
                MatchedGameId: matchedGameId,
                IsUniversalFallback: isUniversal));
        }

        // Sort: games first, then by title
        return results
            .OrderByDescending(w => w.IsLikelyGame)
            .ThenBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public WindowInfo? FindWindowByHandle(nint handle)
    {
        if (handle == nint.Zero)
        {
            return null;
        }

        return EnumerateEligibleWindows().FirstOrDefault(w => w.Handle == handle);
    }

    private static List<RawWindowCandidate> CollectSystemWindows()
    {
        var list = new List<RawWindowCandidate>();

        CaptureInterop.EnumWindows((hWnd, lParam) =>
        {
            if (!CaptureInterop.IsWindow(hWnd) || !CaptureInterop.IsWindowVisible(hWnd))
            {
                return true;
            }

            bool isIconic = CaptureInterop.IsIconic(hWnd);

            int cloaked = 0;
            _ = CaptureInterop.DwmGetWindowAttribute(hWnd, CaptureInterop.DWMWA_CLOAKED, out cloaked, sizeof(int));
            bool isCloaked = cloaked != 0;

            nint exStyle = CaptureInterop.GetWindowLongPtr(hWnd, CaptureInterop.GWL_EXSTYLE);
            bool isToolWindow = (exStyle & CaptureInterop.WS_EX_TOOLWINDOW) != 0
                && (exStyle & CaptureInterop.WS_EX_APPWINDOW) == 0;

            CaptureInterop.GetClientRect(hWnd, out var clientRect);
            var origin = new POINT { X = clientRect.Left, Y = clientRect.Top };
            CaptureInterop.ClientToScreen(hWnd, ref origin);

            var titleChars = new char[256];
            int titleLen = CaptureInterop.GetWindowTextW(hWnd, titleChars, titleChars.Length);
            string title = titleLen > 0 ? new string(titleChars, 0, titleLen) : string.Empty;

            _ = CaptureInterop.GetWindowThreadProcessId(hWnd, out uint pid);
            string procName = "unknown";
            string exeName = "unknown.exe";

            if (pid > 0)
            {
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    procName = proc.ProcessName;
                    exeName = procName + ".exe";
                }
                catch
                {
                    // Process may exit or deny query
                }
            }

            list.Add(new RawWindowCandidate(
                Handle: hWnd,
                ProcessId: (int)pid,
                ProcessName: procName,
                ExecutableName: exeName,
                Title: title,
                X: origin.X,
                Y: origin.Y,
                Width: clientRect.Width,
                Height: clientRect.Height,
                IsVisible: true,
                IsIconic: isIconic,
                IsCloaked: isCloaked,
                IsToolWindow: isToolWindow));

            return true;
        }, nint.Zero);

        return list;
    }
}

/// <summary>Raw window information before filtering.</summary>
public sealed record RawWindowCandidate(
    nint Handle,
    int ProcessId,
    string ProcessName,
    string ExecutableName,
    string Title,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsVisible,
    bool IsIconic,
    bool IsCloaked,
    bool IsToolWindow);

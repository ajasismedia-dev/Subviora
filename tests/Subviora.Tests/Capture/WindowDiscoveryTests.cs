using Subviora.Capture.Discovery;
using Subviora.Core.Capture;
using Subviora.Core.Geometry;
using Subviora.Core.Profiles;

namespace Subviora.Tests.Capture;

public sealed class WindowDiscoveryTests
{
    private sealed class MockGameProfileProvider : IGameProfileProvider
    {
        public Task<IReadOnlyList<GameProfile>> GetProfilesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GameProfile>>([]);

        public Task<GameProfile?> GetByIdAsync(string gameId, CancellationToken cancellationToken = default) =>
            Task.FromResult<GameProfile?>(null);

        public Task<GameProfile?> FindByExecutableAsync(string executablePathOrName, CancellationToken cancellationToken = default)
        {
            if (string.Equals(executablePathOrName, "GTA5.exe", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<GameProfile?>(new GameProfile
                {
                    SchemaVersion = 1,
                    ProfileVersion = "1.0.0",
                    GameId = "gtav",
                    DisplayName = "Grand Theft Auto V",
                    Executables = ["GTA5.exe"],
                    SourceLanguage = "en",
                    TargetLanguage = "tr",
                    CoordinateSpace = new CoordinateSpace(1920, 1080),
                    Calibration = CalibrationStatus.Verified,
                    SubtitleRegions = [new ProfileRegion("main", new NormalizedRect(0.1, 0.75, 0.8, 0.2))],
                    ExclusionRegions = [],
                    Overlay = OverlayPreferences.Default
                });
            }

            if (string.Equals(executablePathOrName, "Universal.exe", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<GameProfile?>(new GameProfile
                {
                    SchemaVersion = 1,
                    ProfileVersion = "1.0.0",
                    GameId = "universal",
                    DisplayName = "Universal Profile",
                    Executables = ["*"],
                    SourceLanguage = "en",
                    TargetLanguage = "tr",
                    CoordinateSpace = new CoordinateSpace(1920, 1080),
                    Calibration = CalibrationStatus.Uncalibrated,
                    SubtitleRegions = [new ProfileRegion("main", new NormalizedRect(0.1, 0.8, 0.8, 0.15))],
                    ExclusionRegions = [],
                    Overlay = OverlayPreferences.Default
                });
            }

            return Task.FromResult<GameProfile?>(null);
        }
    }

    [Fact]
    public void EnumerateEligibleWindows_FiltersOutInvalidAndCloakedWindows()
    {
        var candidates = new List<RawWindowCandidate>
        {
            // Valid game window
            new(new nint(101), 1001, "GTA5", "GTA5.exe", "Grand Theft Auto V", 0, 0, 1920, 1080, true, false, false, false),
            // Cloaked window (e.g., hidden UWP)
            new(new nint(102), 1002, "SomeApp", "SomeApp.exe", "Hidden App", 0, 0, 800, 600, true, false, true, false),
            // Iconic (minimized)
            new(new nint(103), 1003, "Game2", "Game2.exe", "Minimized Game", 0, 0, 1920, 1080, true, true, false, false),
            // Invisible
            new(new nint(104), 1004, "Game3", "Game3.exe", "Invisible Window", 0, 0, 1920, 1080, false, false, false, false),
            // Tool window
            new(new nint(105), 1005, "Tool", "Tool.exe", "Tool Floating Window", 100, 100, 300, 200, true, false, false, true),
            // Tiny window (< 100x100)
            new(new nint(106), 1006, "Tiny", "Tiny.exe", "Tiny Tray", 0, 0, 50, 50, true, false, false, false),
            // Empty title
            new(new nint(107), 1007, "Empty", "Empty.exe", "   ", 0, 0, 800, 600, true, false, false, false),
            // Excluded system process
            new(new nint(108), 1008, "explorer", "explorer.exe", "File Explorer", 0, 0, 1024, 768, true, false, false, false),
            // Subviora itself
            new(new nint(109), 1009, "subviora", "subviora.exe", "Subviora Desktop", 0, 0, 1200, 800, true, false, false, false)
        };

        var service = new Win32WindowDiscoveryService(new MockGameProfileProvider(), () => candidates);
        var eligible = service.EnumerateEligibleWindows();

        Assert.Single(eligible);
        var win = eligible[0];
        Assert.Equal(new nint(101), win.Handle);
        Assert.Equal("Grand Theft Auto V", win.Title);
        Assert.True(win.IsLikelyGame);
        Assert.Equal("gtav", win.MatchedGameId);
        Assert.False(win.IsUniversalFallback);
    }

    [Fact]
    public void EnumerateEligibleWindows_DetectsUniversalAndHeuristicGames()
    {
        var candidates = new List<RawWindowCandidate>
        {
            // GTA V matched by profile
            new(new nint(201), 2001, "GTA5", "GTA5.exe", "Grand Theft Auto V", 0, 0, 1920, 1080, true, false, false, false),
            // Universal fallback
            new(new nint(202), 2002, "Universal", "Universal.exe", "Universal Game", 0, 0, 1280, 720, true, false, false, false),
            // Game detected via heuristic ("game" in exe name)
            new(new nint(203), 2003, "MyCustomGame", "MyCustomGame.exe", "Adventure Game 2026", 0, 0, 1920, 1080, true, false, false, false),
            // Regular non-game app (e.g., Notepad)
            new(new nint(204), 2004, "notepad", "notepad.exe", "Untitled - Notepad", 100, 100, 800, 600, true, false, false, false)
        };

        var service = new Win32WindowDiscoveryService(new MockGameProfileProvider(), () => candidates);
        var eligible = service.EnumerateEligibleWindows();

        Assert.Equal(4, eligible.Count);

        var gtav = eligible.First(w => w.Handle == new nint(201));
        Assert.True(gtav.IsLikelyGame);
        Assert.Equal("gtav", gtav.MatchedGameId);
        Assert.False(gtav.IsUniversalFallback);

        var universal = eligible.First(w => w.Handle == new nint(202));
        Assert.False(universal.IsLikelyGame);
        Assert.Equal("universal", universal.MatchedGameId);
        Assert.True(universal.IsUniversalFallback);

        var heuristic = eligible.First(w => w.Handle == new nint(203));
        Assert.True(heuristic.IsLikelyGame);

        var notepad = eligible.First(w => w.Handle == new nint(204));
        Assert.False(notepad.IsLikelyGame);
        Assert.False(notepad.IsUniversalFallback);
    }

    [Fact]
    public void WindowInfo_ToCaptureTarget_PreservesWindowMetadata()
    {
        var window = new WindowInfo(
            Handle: new nint(555),
            ProcessId: 4444,
            ProcessName: "GTA5",
            ExecutableName: "GTA5.exe",
            Title: "Grand Theft Auto V",
            X: 10,
            Y: 20,
            Width: 1920,
            Height: 1080,
            IsLikelyGame: true,
            MatchedGameId: "gtav",
            IsUniversalFallback: false);

        var target = window.ToCaptureTarget();

        Assert.Equal(window.Handle, target.WindowHandle);
        Assert.Equal(window.ProcessId, target.ProcessId);
    }

    [Fact]
    public void FindWindowByHandle_ReturnsCorrectWindowOrNull()
    {
        var candidates = new List<RawWindowCandidate>
        {
            new(new nint(301), 3001, "App1", "App1.exe", "Application 1", 0, 0, 800, 600, true, false, false, false),
            new(new nint(302), 3002, "App2", "App2.exe", "Application 2", 0, 0, 800, 600, true, false, false, false)
        };

        var service = new Win32WindowDiscoveryService(null, () => candidates);

        var found = service.FindWindowByHandle(new nint(302));
        Assert.NotNull(found);
        Assert.Equal("Application 2", found.Title);

        var notFound = service.FindWindowByHandle(new nint(999));
        Assert.Null(notFound);

        var zero = service.FindWindowByHandle(nint.Zero);
        Assert.Null(zero);
    }
}

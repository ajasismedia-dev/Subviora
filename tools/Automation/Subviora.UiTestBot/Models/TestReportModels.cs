namespace Subviora.UiTestBot.Models;

public enum TestOutcome
{
    Pass,
    Fail,
    Blocked
}

public sealed record TestCaseResult(
    string Name,
    TestOutcome Outcome,
    double DurationMs,
    string? Message = null,
    string? Details = null,
    string? Exception = null,
    string? ScreenshotPath = null);

public sealed record TestEnvironmentInfo(
    string OperatingSystem,
    string DotNetVersion,
    string GitCommitSha,
    string MachineName,
    bool IsInteractiveSession,
    string ApplicationPath);

public sealed record TelemetrySnapshot(
    double Fps,
    double CaptureLatencyMs,
    double OcrLatencyMs,
    double MemoryMb,
    double CpuPercent,
    string? Status);

public sealed record OcrComparisonResult(
    string RecognizedText,
    string ExpectedText,
    double? CharacterErrorRate,
    double? WordErrorRate);

public sealed record TestSuiteReport(
    TestEnvironmentInfo Environment,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    double TotalDurationMs,
    TestOutcome OverallOutcome,
    int PassedCount,
    int FailedCount,
    int BlockedCount,
    IReadOnlyList<TestCaseResult> TestCases,
    TelemetrySnapshot? Telemetry = null,
    OcrComparisonResult? OcrComparison = null,
    string? BlockedReason = null);

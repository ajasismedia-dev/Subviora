using System.IO;
using Subviora.UiTestBot.Models;
using Subviora.UiTestBot.Runner;

namespace Subviora.UiTestBot;

internal static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=================================================");
        Console.WriteLine("  Subviora Automated UI Test Bot (AG-02.3)");
        Console.WriteLine("=================================================");
        Console.WriteLine();

        string repoRoot = FindRepositoryRoot();
        Console.WriteLine($"Repository: {repoRoot}");

        var runner = new UiTestRunner(repoRoot);
        var report = await runner.RunSuiteAsync().ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine(UiTestRunner.GenerateMarkdownSummary(report));

        if (report.OverallOutcome == TestOutcome.Fail)
        {
            Console.WriteLine("[TEST SUITE FAILED] One or more scenarios failed.");
            return 1;
        }

        if (report.OverallOutcome == TestOutcome.Blocked)
        {
            Console.WriteLine("[TEST SUITE BLOCKED] UI tests were blocked by the environment (e.g. headless CI or Smart App Control).");
            return 0;
        }

        Console.WriteLine("[TEST SUITE PASSED] All scenarios completed successfully.");
        return 0;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Subviora.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}

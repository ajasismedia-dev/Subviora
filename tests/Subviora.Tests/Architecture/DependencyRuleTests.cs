using System.Reflection;
using Subviora.Core.Profiles;
using Subviora.GameProfiles.Storage;
using Subviora.Infrastructure.Paths;

namespace Subviora.Tests.Architecture;

/// <summary>Guards the dependency direction described in docs/ARCHITECTURE.md.</summary>
public sealed class DependencyRuleTests
{
    private static string[] ReferencedAssemblyNames(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(a => a.Name!)];

    [Fact]
    public void Core_HasNoDependencyOnOtherSubvioraOrThirdPartyAssemblies()
    {
        var references = ReferencedAssemblyNames(typeof(GameProfile).Assembly);

        Assert.DoesNotContain(references, r => r.StartsWith("Subviora.", StringComparison.Ordinal));
        Assert.DoesNotContain(references, r => r.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal));
        Assert.DoesNotContain(references, r => r.StartsWith("Serilog", StringComparison.Ordinal));
    }

    [Fact]
    public void GameProfiles_DependsOnCoreButNotInfrastructure()
    {
        var references = ReferencedAssemblyNames(typeof(FileSystemGameProfileProvider).Assembly);

        Assert.Contains("Subviora.Core", references);
        Assert.DoesNotContain("Subviora.Infrastructure", references);
        Assert.DoesNotContain(references, r => r.StartsWith("Serilog", StringComparison.Ordinal));
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnFeatureModules()
    {
        var references = ReferencedAssemblyNames(typeof(AppPaths).Assembly);

        Assert.DoesNotContain("Subviora.GameProfiles", references);
        Assert.DoesNotContain("Subviora.Desktop", references);
    }
}

using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace SonicRuntime.Tests;

public class VersionTests
{
    private static string GetAssemblyVersion()
    {
        return typeof(SonicRuntime.Engine.RuntimeState).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";
    }

    [Fact]
    public void Version_is_valid_semver()
    {
        var version = GetAssemblyVersion();
        Assert.Matches(@"^\d+\.\d+\.\d+", version);
    }

    [Fact]
    public void Version_is_at_least_1_0_0()
    {
        var version = GetAssemblyVersion();
        var major = int.Parse(version.Split('.')[0]);
        Assert.True(major >= 1, $"Version {version} must be >= 1.0.0");
    }

    [Fact]
    public void Changelog_mentions_current_version()
    {
        var version = GetAssemblyVersion().Split('+')[0]; // strip build metadata
        var changelog = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "CHANGELOG.md"));
        Assert.Contains($"v{version}", changelog);
    }
}

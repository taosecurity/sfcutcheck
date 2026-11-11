// AGENTS.md and the README must name every option the program accepts (CLAUDE.md rule for public tools: AGENTS.md
// changes in the same commit as any change to the tool's options).
using System.Text.RegularExpressions;
using Xunit;

namespace SfCutCheck.Tests;

public sealed class DocsTests
{
    static string RepoFile(string name)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, name)) && Directory.Exists(Path.Combine(d.FullName, "src")))
                return File.ReadAllText(Path.Combine(d.FullName, name));
        throw new FileNotFoundException($"{name} not found above {AppContext.BaseDirectory}");
    }

    /// <summary>Every --option the parser handles (the case labels in Program.cs).</summary>
    static IEnumerable<string> Options() =>
        Regex.Matches(RepoFile(Path.Combine("src", "Program.cs")), "case \"(--[a-z-]+)\"").Select(m => m.Groups[1].Value).Distinct();

    [Fact]
    public void AgentsNamesEveryOption()
    {
        var agents = RepoFile("AGENTS.md");
        var missing = Options().Where(o => !agents.Contains(o)).ToList();
        Assert.True(missing.Count == 0, $"AGENTS.md doesn't mention: {string.Join(", ", missing)}");
    }

    [Fact]
    public void ReadmeNamesEveryOption()
    {
        var readme = RepoFile("README.md");
        var missing = Options().Where(o => !readme.Contains(o)).ToList();
        Assert.True(missing.Count == 0, $"README.md doesn't mention: {string.Join(", ", missing)}");
    }

    [Fact]
    public void DocsGiveTheCurrentVersion()
    {
        var version = Regex.Match(RepoFile(Path.Combine("src", "Program.cs")), "const string Version = \"([^\"]+)\"").Groups[1].Value;
        Assert.Contains($"sfcutcheck {version}", RepoFile("AGENTS.md"));
        Assert.Contains($"sfcutcheck-{version}-win-x64.zip", RepoFile("README.md"));
    }
}

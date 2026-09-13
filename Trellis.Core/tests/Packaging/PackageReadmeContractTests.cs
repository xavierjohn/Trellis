namespace Trellis.Core.Tests.Packaging;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

/// <summary>
/// Keeps repository and NuGet package landing pages complete, distinct, and discoverable.
/// </summary>
public partial class PackageReadmeContractTests
{
    private static readonly string[] CommonSections =
    [
        "## Installation",
        "## Quick Example",
        "## Key Features",
        "## Documentation",
    ];

    [Fact]
    public void Every_package_has_both_readme_roles()
    {
        var missing = Packages()
            .SelectMany(package => new[]
            {
                (Path: package.RepositoryReadmePath, Role: "repository"),
                (Path: package.NuGetReadmePath, Role: "NuGet"),
            }
            .Where(readme => !File.Exists(readme.Path))
            .Select(readme => $"{package.Name}: missing {readme.Role} README"))
            .ToList();

        missing.Should().BeEmpty(
            "every shipping package needs a repository README and a separately curated NuGet listing");
    }

    [Fact]
    public void Every_repository_readme_has_a_consistent_reader_path()
    {
        var failures = Packages()
            .Where(package => File.Exists(package.RepositoryReadmePath))
            .SelectMany(RepositoryReadmeFailures)
            .ToList();

        failures.Should().BeEmpty(
            "a repository README should orient, install, demonstrate, summarize, link, and support contributors");
    }

    [Fact]
    public void Every_nuget_readme_has_a_consistent_consumer_path()
    {
        var failures = Packages()
            .Where(package => File.Exists(package.NuGetReadmePath))
            .SelectMany(NuGetReadmeFailures)
            .ToList();

        failures.Should().BeEmpty(
            "a NuGet listing should provide a self-contained consumer path without repository-only content");
    }

    [Fact]
    public void Root_readme_lists_every_shipping_package()
    {
        var rootReadme = File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md"));
        var missing = Packages()
            .Where(package => !rootReadme.Contains(
                $"[{package.Name}](https://www.nuget.org/packages/{package.Name})",
                StringComparison.Ordinal))
            .Select(package => package.Name)
            .ToList();

        missing.Should().BeEmpty("the root package chooser must expose every shipping package");
    }

    [Fact]
    public void Package_readmes_stay_focused_on_onboarding()
    {
        const int repositoryReadmeWordLimit = 800;
        const int nugetReadmeWordLimit = 600;

        var failures = Packages()
            .SelectMany(package => new[]
            {
                WordLimitFailure(package.Name, package.RepositoryReadmePath, repositoryReadmeWordLimit),
                WordLimitFailure(package.Name, package.NuGetReadmePath, nugetReadmeWordLimit),
            })
            .Where(failure => failure is not null)
            .ToList();

        failures.Should().BeEmpty(
            "package landing pages should provide orientation and a first-success path, "
            + "while exhaustive behavior and migration material belongs in the API reference");
    }

    [Fact]
    public void Readmes_use_supported_microsoft_testing_platform_options()
    {
        var failures = Directory
            .EnumerateFiles(RepositoryRoot(), "*README*.md", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Line: line, Number: index + 1))
                .Where(item => UnsupportedDotnetTestOption().IsMatch(item.Line))
                .Select(item => $"{Path.GetRelativePath(RepositoryRoot(), path)}:{item.Number}: {item.Line.Trim()}"))
            .ToList();

        failures.Should().BeEmpty(
            "Microsoft.Testing.Platform does not support VSTest's --filter or --nologo switches, "
            + "and the solution path is passed positionally rather than with --solution");
    }

    private static IEnumerable<string> RepositoryReadmeFailures(PackageReadmes package)
    {
        var text = File.ReadAllText(package.RepositoryReadmePath);

        foreach (var failure in CommonFailures(package, text, CommonSections.Append("## Development")))
            yield return $"README {failure}";

        var reference = $"../docs/docfx_project/api_reference/trellis-api-{package.ApiReferenceName}.md";
        if (!text.Contains(reference, StringComparison.Ordinal))
            yield return $"README {package.Name}: missing package-specific API reference link {reference}";
    }

    private static IEnumerable<string> NuGetReadmeFailures(PackageReadmes package)
    {
        var text = File.ReadAllText(package.NuGetReadmePath);

        foreach (var failure in CommonFailures(package, text, CommonSections))
            yield return $"NUGET_README {failure}";

        if (text.Contains("## Development", StringComparison.Ordinal))
            yield return $"NUGET_README {package.Name}: contains repository-only Development section";

        if (RepositoryRelativeMarkdownLink().IsMatch(text))
            yield return $"NUGET_README {package.Name}: contains a repository-relative Markdown link";

        var reference =
            $"https://xavierjohn.github.io/Trellis/api_reference/trellis-api-{package.ApiReferenceName}.html";
        if (!text.Contains(reference, StringComparison.Ordinal))
            yield return $"NUGET_README {package.Name}: missing published package-specific API reference link";
    }

    private static IEnumerable<string> CommonFailures(
        PackageReadmes package,
        string text,
        IEnumerable<string> requiredSections)
    {
        if (!text.TrimStart('\uFEFF').StartsWith($"# {package.Name}", StringComparison.Ordinal))
            yield return $"{package.Name}: first heading must be '# {package.Name}'";

        var previousIndex = -1;
        foreach (var section in requiredSections)
        {
            var currentIndex = text.IndexOf(section, StringComparison.Ordinal);
            if (currentIndex < 0)
            {
                yield return $"{package.Name}: missing {section}";
                continue;
            }

            if (currentIndex < previousIndex)
                yield return $"{package.Name}: {section} is out of order";

            previousIndex = currentIndex;
        }
    }

    private static List<PackageReadmes> Packages()
    {
        var root = RepositoryRoot();

        return Directory
            .EnumerateDirectories(root, "Trellis.*", SearchOption.TopDirectoryOnly)
            .Select(directory => new DirectoryInfo(directory))
            .Select(directory => new
            {
                Directory = directory,
                ProjectPath = Path.Combine(
                    directory.FullName,
                    "src",
                    $"{directory.Name}.csproj"),
            })
            .Where(package => File.Exists(package.ProjectPath))
            .Select(package =>
            {
                var project = XDocument.Load(package.ProjectPath);
                var apiReferenceName = project.Descendants("TrellisApiRefName").Single().Value.Trim();

                return new PackageReadmes(
                    package.Directory.Name,
                    apiReferenceName,
                    Path.Combine(package.Directory.FullName, "README.md"),
                    Path.Combine(package.Directory.FullName, "NUGET_README.md"));
            })
            .OrderBy(package => package.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string? WordLimitFailure(string packageName, string path, int limit)
    {
        if (!File.Exists(path))
            return null;

        var wordCount = MarkdownWord().Count(File.ReadAllText(path));
        return wordCount > limit
            ? $"{packageName} {Path.GetFileName(path)}: {wordCount} words (limit {limit})"
            : null;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Trellis.slnx")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the test must be able to locate the repository root");
        return directory!.FullName;
    }

    [GeneratedRegex(@"(?im)^\s*dotnet\s+test\b[^\r\n]*(?:\s--(?:filter|nologo|solution)(?:\s|=|$))")]
    private static partial Regex UnsupportedDotnetTestOption();

    [GeneratedRegex(@"\]\(\.\.?[\\/]", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryRelativeMarkdownLink();

    [GeneratedRegex(@"\b[\p{L}\p{N}_-]+\b", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownWord();

    private sealed record PackageReadmes(
        string Name,
        string ApiReferenceName,
        string RepositoryReadmePath,
        string NuGetReadmePath);
}

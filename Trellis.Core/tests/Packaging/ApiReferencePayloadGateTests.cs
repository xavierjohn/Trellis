namespace Trellis.Core.Tests.Packaging;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

/// <summary>
/// Every packable Trellis package must deliver the API reference set to whoever installs it.
/// </summary>
/// <remarks>
/// <para>
/// The references are what an agent reads before writing Trellis code. A package that ships
/// without them is not merely undocumented: an agent working in a project that installed it has
/// no signatures to work from and invents plausible ones instead, which is the failure the whole
/// reference set exists to prevent.
/// </para>
/// <para>
/// Delivery is opt-in per csproj, and opt-in is exactly how a package comes to ship with none —
/// <c>Trellis.ResourceNaming.Azure</c> published that way, and nothing failed. Enumerating the
/// projects rather than hand-listing them is the point: a hand-listed set stops covering the next
/// package added, which is the same miss one generation later. This is the same class of gate as
/// <c>RegistrationSurfaceTests</c>, which made the <c>AddXxx</c>/<c>UseXxx</c> slot rule
/// enforceable rather than merely documented.
/// </para>
/// </remarks>
public class ApiReferencePayloadGateTests
{
    [Fact]
    public void Every_packable_package_delivers_the_api_reference_set()
    {
        var projects = PackableProjects();

        projects.Should().NotBeEmpty("the enumeration must actually find the repository's projects");

        var undelivered = projects
            .Where(p => !DeliversReferenceSet(p, projects))
            .Select(p => p.Name)
            .ToList();

        undelivered.Should().BeEmpty(
            "a packable package must ship the reference set, ship its own reference, or depend on "
            + "a package that does — otherwise an agent in a consuming project has no signatures to "
            + "work from and will invent them");
    }

    [Fact]
    public void Every_declared_reference_name_resolves_to_a_file()
    {
        var root = RepositoryRoot();

        var dangling = PackableProjects()
            .Where(p => !string.IsNullOrEmpty(p.ApiRefName))
            .Where(p => !File.Exists(Path.Combine(
                root, "docs", "docfx_project", "api_reference", $"trellis-api-{p.ApiRefName}.md")))
            .Select(p => $"{p.Name} -> trellis-api-{p.ApiRefName}.md")
            .ToList();

        // A typo here is silent: the MSBuild None/Include simply matches nothing, so the package
        // packs successfully and ships no reference. The audits key off the same property, so they
        // go quiet at the same moment rather than catching it.
        dangling.Should().BeEmpty("every TrellisApiRefName must name a reference file that exists");
    }

    [Fact]
    public void No_shipping_package_hides_outside_the_src_convention()
    {
        // The gate above finds packages by the repo convention that every shipping package lives at
        // <Package>/src/<Package>.csproj. That filter is load-bearing rather than incidental: it is
        // the only reason test and example projects, whose csproj files do not declare IsPackable
        // themselves, stay out of the enumeration.
        //
        // So a packable project added anywhere else would be silently skipped -- the enumeration
        // quietly stops covering it, which is the same "hand-listed set goes stale" failure the
        // enumeration exists to avoid, one level up. This asserts the convention itself, so such a
        // project fails here instead of shipping without docs.
        var stray = OutsideSrcProjects()
            .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith(".Tests", StringComparison.Ordinal))
            .Where(path => !DeclaresNonPackable(path))
            .Select(path => Path.GetRelativePath(RepositoryRoot(), path))
            .ToList();

        stray.Should().BeEmpty(
            "a project outside */src/ that is not marked IsPackable=false is invisible to the "
            + "payload gate — move it under src/ so it is covered, or mark it non-packable");
    }

    /// <summary>
    /// True when the project, or any <c>Directory.Build.props</c> above it, opts out of packing.
    /// The walk matters: the example projects declare nothing themselves and inherit the opt-out.
    /// </summary>
    private static bool DeclaresNonPackable(string projectPath)
    {
        if (SaysNotPackable(projectPath))
            return true;

        var root = RepositoryRoot();
        var directory = new DirectoryInfo(Path.GetDirectoryName(projectPath)!);

        while (directory is not null)
        {
            var props = Path.Combine(directory.FullName, "Directory.Build.props");
            if (File.Exists(props) && SaysNotPackable(props))
                return true;

            if (string.Equals(directory.FullName, root, StringComparison.OrdinalIgnoreCase))
                break;

            directory = directory.Parent;
        }

        return false;
    }

    private static bool SaysNotPackable(string path)
    {
        // Namespace-agnostic: Directory.Build.props declares the legacy MSBuild namespace while the
        // csproj files do not, so matching on local name is what makes one reader serve both.
        var document = XDocument.Load(path);
        return document.Descendants()
            .Where(e => e.Name.LocalName == "IsPackable")
            .Any(e => string.Equals(e.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> OutsideSrcProjects()
    {
        var separator = Path.DirectorySeparatorChar;

        return Directory.EnumerateFiles(RepositoryRoot(), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}src{separator}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal));
    }

    /// <summary>
    /// A package delivers the set when it ships it, ships its own reference, or reaches — through
    /// first-party project references — some package that does.
    /// </summary>
    private static bool DeliversReferenceSet(ProjectInfo project, IReadOnlyList<ProjectInfo> all)
    {
        var byPath = all.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<ProjectInfo>();
        queue.Enqueue(project);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current.Path))
                continue;

            if (current.ShipsReferenceSet || (current.ShipsOwnReference && !string.IsNullOrEmpty(current.ApiRefName)))
                return true;

            foreach (var reference in current.ProjectReferences)
            {
                if (byPath.TryGetValue(reference, out var next))
                    queue.Enqueue(next);
            }
        }

        return false;
    }

    private static List<ProjectInfo> PackableProjects()
    {
        var root = RepositoryRoot();
        var separator = Path.DirectorySeparatorChar;

        return Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{separator}src{separator}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal))
            .Select(Read)
            .Where(p => p.IsPackable)
            .ToList();
    }

    private static ProjectInfo Read(string path)
    {
        var document = XDocument.Load(path);
        var directory = Path.GetDirectoryName(path)!;

        string Property(string name) =>
            document.Descendants(name).FirstOrDefault()?.Value.Trim() ?? string.Empty;

        var references = document.Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => Path.GetFullPath(Path.Combine(directory, v!.Replace('\\', Path.DirectorySeparatorChar))))
            .ToList();

        return new ProjectInfo(
            Name: Path.GetFileNameWithoutExtension(path),
            Path: Path.GetFullPath(path),
            IsPackable: !string.Equals(Property("IsPackable"), "false", StringComparison.OrdinalIgnoreCase),
            ShipsReferenceSet: string.Equals(Property("TrellisShipsApiReferenceSet"), "true", StringComparison.OrdinalIgnoreCase),
            ShipsOwnReference: string.Equals(Property("TrellisShipsOwnApiReference"), "true", StringComparison.OrdinalIgnoreCase),
            ApiRefName: Property("TrellisApiRefName"),
            ProjectReferences: references);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Trellis.slnx")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the test must be able to locate the repository root (Trellis.slnx)");
        return directory!.FullName;
    }

    private sealed record ProjectInfo(
        string Name,
        string Path,
        bool IsPackable,
        bool ShipsReferenceSet,
        bool ShipsOwnReference,
        string ApiRefName,
        List<string> ProjectReferences);
}

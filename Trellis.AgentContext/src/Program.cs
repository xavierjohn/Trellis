namespace Trellis.AgentContext;

using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trellis.Guidance.Reader;

/// <summary>Entry point for the repository-pinned Trellis agent-context tool.</summary>
public static class Program
{
    /// <summary>Runs the agent-context command.</summary>
    public static int Main(string[] args) => AgentContextCommand.Run(args, Console.Error, GuidanceReader.Discover);
}

internal sealed record Source(string Package, string PackageVersion, string PackagePath, string Sha256);
internal sealed record OwnedFile(string Path, string CanonicalSha256, Source[] Sources);
internal sealed record InstructionEntry(string InstructionFile, string CanonicalSha256);
internal sealed record GraphPackage(string Id, string Version, string? ContentHash);
internal sealed record GraphProject(string Project, string Framework, string? Runtime, GraphPackage[] Packages);
internal sealed record GraphInput(string Path, string RestoreSpecSha256);
internal sealed record GraphState(GraphInput[] EntryPoints, GraphProject[] Projects);
internal sealed record ContextState(int SchemaVersion, string Scope, string TextHashFormat, string GeneratedBy,
    string[] SourceRoots, GraphState Graph, InstructionEntry[] InstructionEntries, OwnedFile[] ToolOwnedFiles,
    OwnedFile[] References, string[] EntryPoints, string[] ExplicitSourceRoots);

internal sealed record Change(string Path, byte[]? Before, byte[]? After, string Description)
{
    public byte[]? Snapshot { get; init; } = Before is null ? null : SHA256.HashData(Before);
}

/// <summary>Executes graph discovery and repository-scoped lifecycle operations.</summary>
public static class AgentContextCommand
{
    private const string Start = "<!-- trellis-agent-context:start -->";
    private const string End = "<!-- trellis-agent-context:end -->";
    private static readonly UTF8Encoding Strict = new(false, true);
    private static readonly UTF8Encoding Bom = new(true, true);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly StringComparer Portable = StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparer Physical = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly StringComparison PhysicalComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static readonly string[] LegacyTargetNames =
        ["_CopyTrellisApiReference", "TrellisSyncApiReference", "_WarnTrellisApiReferenceCopyLogicMissing"];
    private static readonly string[] ExcludedSourceDirectories = [".trellis", ".git", "bin", "obj"];

    /// <summary>Runs a command using a read-only package-guidance discovery adapter.</summary>
    public static int Run(string[] args, TextWriter output, Func<IEnumerable<string>, GuidanceDiscovery> discover)
    {
        try
        {
            return Execute(args, output, discover);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
            or JsonException or DecoderFallbackException)
        {
            output.WriteLine("trellis agent: " + e.Message);
            return 1;
        }
    }

    private static int Execute(string[] args, TextWriter output, Func<IEnumerable<string>, GuidanceDiscovery> discover)
    {
        if (args.Length < 2 || args[0] != "agent" ||
            args[1] is not ("init" or "sync" or "check" or "remove"))
        {
            output.WriteLine("Usage: trellis agent init|sync|check|remove [--scope DIR] [--source-root DIR] [--dry-run] [--force] [--restore] [PROJECT|SOLUTION]");
            output.WriteLine("Avoid external edits to affected files during mutation; an edit after the final snapshot check may be lost.");
            return 2;
        }

        var verb = args[1];
        string? scopeArg = null;
        var dryRun = false;
        var force = false;
        var restore = false;
        var entryPoints = new List<string>();
        var sourceRootArgs = new List<string>();
        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scope":
                    if (++i == args.Length) throw new ArgumentException("--scope requires a directory.");
                    scopeArg = args[i];
                    break;
                case "--source-root":
                    if (++i == args.Length) throw new ArgumentException("--source-root requires a directory.");
                    sourceRootArgs.Add(args[i]);
                    break;
                case "--dry-run": dryRun = true; break;
                case "--force": force = true; break;
                case "--restore": restore = true; break;
                default:
                    if (args[i].StartsWith('-'))
                        throw new ArgumentException($"Unknown option '{args[i]}'.");
                    entryPoints.Add(args[i]);
                    break;
            }
        }

        if ((verb == "check" && (dryRun || force || restore)) ||
            (verb == "remove" && restore) || (dryRun && restore) ||
            (verb != "init" && sourceRootArgs.Count != 0) ||
            (verb != "init" && entryPoints.Count != 0))
            throw new ArgumentException("Invalid options or entry points for this verb.");
        if (verb == "init" && entryPoints.Count == 0)
            throw new ArgumentException("init requires an explicit project or solution.");
        var cwd = Path.GetFullPath(Environment.CurrentDirectory);
        var root = GitRoot(cwd);
        RejectExcluded(root, cwd);
        var scope = scopeArg is null ? root : Path.GetFullPath(scopeArg, cwd);
        Inside(root, scope);
        RejectExcluded(root, scope);
        EnsureDirectoriesSafe(root, scope);
        var relativeScope = Rel(root, scope);
        var manifestPath = Path.Combine(scope, ".trellis", "agent-context.json");
        CheckDestination(root, scope, manifestPath);
        var previous = File.Exists(manifestPath) ? LoadState(manifestPath) : null;
        if (previous is not null && previous.Scope != relativeScope)
            throw new InvalidOperationException("Recorded context scope does not match the selected scope.");
        if (verb != "init" && previous is null)
            throw new InvalidOperationException("No initialized context at the selected scope.");

        var version = ToolVersion();
        ValidateTool(cwd, scope, version);
        if (verb == "init" && previous is not null && entryPoints.Count != 0 &&
            !previous.Graph.EntryPoints.Select(x => x.Path).OrderBy(x => x).SequenceEqual(
                entryPoints.Select(x => Rel(scope, Path.GetFullPath(x, cwd))).OrderBy(x => x)))
            throw new InvalidOperationException("Existing context has different graph entry points; remove it before reinitializing.");

        using var writerLock = verb is "check" || dryRun ? null : Lock(root);
        var graphEntries = verb == "init" ? entryPoints.Select(p => Path.GetFullPath(p, cwd)).ToArray()
            : previous?.Graph.EntryPoints.Select(p => Full(scope, p.Path)).ToArray() ?? [];
        if (restore)
        {
            output.WriteLine("Explicit restore may update obj/, the NuGet cache, and configured lock files.");
            foreach (var entry in graphEntries)
            {
                CheckDestination(root, scope, entry);
                Restore(entry);
            }
        }

        var explicitRoots = verb == "init" ? sourceRootArgs.Select(p => Rel(scope, Path.GetFullPath(p, cwd))).ToArray()
            : previous?.ExplicitSourceRoots ?? [];
        (ContextState State, Dictionary<string, string> Content)? discovery = verb == "remove" ? null : BuildState(root, scope,
            graphEntries, explicitRoots, version, discover);
        var changes = Plan(root, scope, previous, discovery?.State, discovery?.Content, manifestPath, force);
        foreach (var change in changes)
            output.WriteLine($"{(dryRun ? "Would " : "")}{change.Description}: {Rel(root, change.Path)}");
        if (verb == "check")
        {
            if (changes.Count == 0)
                return 0;
            output.WriteLine("Context differs from the restored graph.");
            return 1;
        }

        if (dryRun || changes.Count == 0)
            return 0;
        ProbeAtomic(root, changes.Select(change => change.Path));
        foreach (var change in changes)
        {
            EnsureDirectoriesSafe(root, Path.GetDirectoryName(change.Path)!);
            var current = File.Exists(change.Path) ? File.ReadAllBytes(change.Path) : null;
            if (!Equal(current is null ? null : SHA256.HashData(current), change.Snapshot))
                throw new InvalidOperationException($"Concurrent change detected: {change.Path}");
            if (change.After is null)
            {
                var staged = Path.Combine(Path.GetDirectoryName(change.Path)!, "." + Guid.NewGuid().ToString("N") + ".remove");
                File.Move(change.Path, staged);
                File.Delete(staged);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(change.Path)!);
            var temp = Path.Combine(Path.GetDirectoryName(change.Path)!, "." + Guid.NewGuid().ToString("N") + ".stage");
            try
            {
                File.WriteAllBytes(temp, change.After);
                File.Move(temp, change.Path, overwrite: current is not null);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        return 0;
    }

    private static (ContextState State, Dictionary<string, string> Content) BuildState(string root, string scope, string[] entries,
        string[] explicitRoots, string version, Func<IEnumerable<string>, GuidanceDiscovery> discover)
    {
        var projects = entries.SelectMany(entry => Projects(entry, root)).Distinct(Portable)
            .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (projects.Length == 0)
            throw new InvalidOperationException("No selected projects.");
        foreach (var project in projects)
        {
            CheckDestination(root, scope, project);
            if (!Portable.Equals(GitRoot(Path.GetDirectoryName(project)!), root))
                throw new InvalidOperationException($"Selected project crosses a Git working tree boundary: {project}");
            for (var directory = Path.GetDirectoryName(project); directory is not null &&
                Within(scope, directory) && !Portable.Equals(directory, scope); directory = Path.GetDirectoryName(directory))
            {
                var nestedManifest = Path.Combine(directory, ".config", "dotnet-tools.json");
                CheckDestination(root, scope, nestedManifest);
                if (File.Exists(nestedManifest))
                    throw new InvalidOperationException(
                        $"Project has a nested tool manifest at {nestedManifest}; run from that directory with --scope .");
            }
        }

        var assets = projects.Select(p => Path.Combine(Path.GetDirectoryName(p)!, "obj", "project.assets.json")).ToArray();
        foreach (var asset in assets)
            CheckDestination(root, scope, asset);
        var guidance = discover(assets);
        var incompatibilities = new List<string>();
        if (!guidance.IsSuccessful)
            incompatibilities.Add("Package guidance discovery failed: " + string.Join("; ",
                guidance.Diagnostics.Concat(guidance.Packages.Where(p => p.Diagnostic is not null).Select(p => p.Diagnostic))));
        foreach (var package in guidance.Packages.OrderBy(p => p.PackageId, StringComparer.Ordinal)
            .ThenBy(p => p.Version, StringComparer.Ordinal))
        {
            if (package.PackageRoot is null || !Directory.Exists(package.PackageRoot))
                incompatibilities.Add($"Package assets are missing: {package.PackageId}/{package.Version}");
            if (package.PackageId.StartsWith("Trellis.", StringComparison.OrdinalIgnoreCase) &&
                package.Status != GuidanceStatus.Valid)
                incompatibilities.Add($"Incompatible Trellis package {package.PackageId}/{package.Version}: missing current guidance manifest.");
            if (package.PackageRoot is not null && Directory.Exists(package.PackageRoot))
                CheckLegacyTargets(package, incompatibilities);
        }

        foreach (var family in guidance.Packages.GroupBy(p => p.PackageId, StringComparer.OrdinalIgnoreCase))
            if (family.Select(p => p.Version).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
                incompatibilities.Add($"Mixed versions of {family.Key}: " +
                    string.Join(", ", family.Select(p => p.Version).Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(v => v, StringComparer.Ordinal)) + "; initialize independent scopes.");
        var core = guidance.Packages.Where(p => p.PackageId.Equals("Trellis.Core", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Version).Distinct().ToArray();
        if (core.Length > 1)
            incompatibilities.Add("Incompatible Core versions: " + string.Join(", ", core) + "; use independent scopes.");
        if (core.Length == 1 && core[0] != version)
            incompatibilities.Add($"Pinned tool version {version} does not match Trellis.Core {core[0]}.");
        foreach (var c in guidance.Packages.Where(p => p.PackageId.Equals("Trellis.Core", StringComparison.OrdinalIgnoreCase) &&
            p.Contribution is not null))
        {
            if (!c.Contribution!.Documents.Any(d => d.Identity.PackagePath == "trellis/trellis-start-here.md") ||
                !c.Contribution.Documents.Any(d => d.Identity.PackagePath == "trellis/trellis-api-cookbook.md") ||
                !c.Contribution.EntryPoints.Contains("trellis/trellis-start-here.md", StringComparer.Ordinal))
                incompatibilities.Add(
                    $"Trellis.Core/{c.Version} must declare the router and cookbook, with trellis-start-here.md as an entry point.");
            if (!c.Contribution.PublisherMetadata.TryGetValue("org.trellis", out var metadata) ||
                metadata.ValueKind != JsonValueKind.Object || !metadata.TryGetProperty("lockstepCohort", out var cohort) ||
                cohort.ValueKind != JsonValueKind.Array)
            {
                incompatibilities.Add($"Trellis.Core/{c.Version} must declare its lockstep cohort.");
                continue;
            }

            var cohortIds = cohort.EnumerateArray().Select(id => id.ValueKind == JsonValueKind.String ? id.GetString() : null)
                .ToArray();
            if (cohortIds.Length == 0 || cohortIds.Any(string.IsNullOrWhiteSpace) ||
                cohortIds.Distinct(Portable).Count() != cohortIds.Length || !cohortIds.Contains("Trellis.Core", Portable))
            {
                incompatibilities.Add($"Invalid Trellis.Core/{c.Version} lockstep cohort.");
                continue;
            }

            foreach (var sibling in guidance.Packages.Where(p => cohortIds.Contains(p.PackageId, Portable)))
                if (sibling.Version != c.Version || sibling.Status != GuidanceStatus.Valid)
                    incompatibilities.Add($"Incompatible cohort package {sibling.PackageId}/{sibling.Version}.");
        }

        if (incompatibilities.Count != 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, incompatibilities));

        foreach (var sourceRoot in explicitRoots)
        {
            var directory = Full(scope, sourceRoot);
            CheckDestination(root, scope, directory);
            if (sourceRoot.Split('/').Any(p => ExcludedSourceDirectories.Contains(p, Portable)) ||
                !Directory.Exists(directory) || !Portable.Equals(GitRoot(directory), root))
                throw new InvalidOperationException($"Invalid explicit source root: {sourceRoot}");
        }

        var roots = projects.Select(p => Rel(scope, Path.GetDirectoryName(p)!)).Concat(explicitRoots)
            .Distinct(Portable).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var generatedDirectories = GeneratedDirectories(projects);
        foreach (var source in roots)
            if (generatedDirectories.Any(directory => Within(directory, Full(scope, source))))
                throw new InvalidOperationException($"Source root is inside an evaluated generated directory: {source}");
        var instructionFiles = InstructionFiles(root, scope, roots, generatedDirectories).ToArray();
        var mapped = new Dictionary<string, (string Text, List<Source> Sources)>(Portable);
        var entryPoints = new HashSet<string>(Portable);
        foreach (var package in guidance.Packages.Where(p => p.Contribution is not null))
        foreach (var document in package.Contribution!.Documents)
        {
            if (!document.Identity.PackagePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Non-Markdown guidance: {document.Identity.PackagePath}");
            var trellis = package.PackageId.StartsWith("Trellis.", StringComparison.OrdinalIgnoreCase) &&
                document.Identity.PackagePath.StartsWith("trellis/", StringComparison.Ordinal) &&
                !document.Identity.PackagePath["trellis/".Length..].Contains('/');
            var relative = trellis
                ? "api-reference/" + Path.GetFileName(document.Identity.PackagePath)
                : "packages/" + SafeComponent(package.PackageId) + "/" + SafeComponent(package.Version) + "/" +
                  document.Identity.PackagePath.Replace('\\', '/');
            ValidateRelative(relative);
            if (package.Contribution.EntryPoints.Contains(document.Identity.PackagePath, StringComparer.Ordinal))
                entryPoints.Add(relative);
            var portablePath = relative.Normalize(NormalizationForm.FormC);
            var bytes = File.ReadAllBytes(document.LocalPath);
            if (Hash(bytes) != document.Sha256)
                throw new InvalidOperationException($"Package hash mismatch: {package.PackageId}/{document.Identity.PackagePath}");
            var text = Canonical(bytes);
            var source = new Source(package.PackageId, package.Version, document.Identity.PackagePath, document.Sha256);
            var existingPath = mapped.Keys.FirstOrDefault(p => Portable.Equals(p.Normalize(NormalizationForm.FormC), portablePath));
            if (existingPath is not null)
            {
                var existing = mapped[existingPath];
                if (existing.Text != text || !string.Equals(relative, existingPath, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Conflicting contributions to {relative}: {existing.Sources[0].Package}, {package.PackageId}");
                existing.Sources.Add(source);
            }
            else
                mapped.Add(relative, (text, [source]));
        }

        var references = mapped.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new OwnedFile(pair.Key, HashText(pair.Value.Text),
                pair.Value.Sources.Distinct().OrderBy(s => s.Package, StringComparer.Ordinal)
                    .ThenBy(s => s.PackagePath, StringComparer.Ordinal).ToArray())).ToArray();
        var content = mapped.ToDictionary(pair => pair.Key, pair => pair.Value.Text, Portable);
        var graph = BuildGraph(scope, entries, guidance);
        var state = new ContextState(1, Rel(root, scope), "utf8-lf-no-bom-v1", version, roots, graph,
            instructionFiles.Select(p => new InstructionEntry(Rel(root, p), HashText(Entry(scope, p)))).ToArray(),
            [], references, entryPoints.OrderBy(p => p, StringComparer.Ordinal).ToArray(),
            explicitRoots.OrderBy(p => p, StringComparer.Ordinal).ToArray());
        var indexText = Index(state);
        content["README.md"] = indexText;
        return (state with { ToolOwnedFiles = [new OwnedFile("README.md", HashText(indexText), [])] }, content);
    }

    private static void CheckLegacyTargets(GuidancePackage package, List<string> incompatibilities)
    {
        foreach (var folder in new[] { "build", "buildTransitive" })
        {
            var directory = Path.Combine(package.PackageRoot!, folder);
            if (Directory.Exists(directory) && File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
            {
                incompatibilities.Add($"Linked package target directory in {package.PackageId}/{package.Version}.");
                continue;
            }

            var names = new[] { package.PackageId + ".targets", "Trellis.ApiReference.targets",
                "Trellis.ApiReference.Payload.targets" };
            foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var path = Path.Combine(directory, name);
                if (!File.Exists(path))
                    continue;
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                {
                    incompatibilities.Add($"Linked package target in {package.PackageId}/{package.Version}.");
                    continue;
                }

                var text = File.ReadAllText(path);
                if (name.Equals("Trellis.ApiReference.Payload.targets", StringComparison.OrdinalIgnoreCase) ||
                    LegacyTargetNames.Any(marker => text.Contains(marker, StringComparison.Ordinal)))
                    incompatibilities.Add(
                        $"Legacy copy/payload target {folder}/{name} in {package.PackageId}/{package.Version}; upgrade before installing.");
            }
        }
    }

    private static GraphState BuildGraph(string scope, string[] entries, GuidanceDiscovery discovery)
    {
        var graphEntries = entries.Select(p => new GraphInput(Rel(scope, p), RestoreSpec(p)))
            .OrderBy(p => p.Path, StringComparer.Ordinal).ToArray();
        var projects = discovery.Packages.GroupBy(p => new { p.Scope.ProjectPath, p.Scope.TargetFramework, p.Scope.RuntimeIdentifier })
            .Select(g => new GraphProject(Rel(scope, g.Key.ProjectPath), g.Key.TargetFramework, g.Key.RuntimeIdentifier,
                g.Select(p => new GraphPackage(p.PackageId, p.Version, p.NuGetContentHash))
                    .Distinct().OrderBy(p => p.Id, StringComparer.Ordinal).ToArray()))
            .OrderBy(p => p.Project, StringComparer.Ordinal).ThenBy(p => p.Framework, StringComparer.Ordinal).ToArray();
        return new GraphState(graphEntries, projects);
    }

    private static string RestoreSpec(string entry)
    {
        if (entry.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var assets = Path.Combine(Path.GetDirectoryName(entry)!, "obj", "project.assets.json");
            using var json = JsonDocument.Parse(File.ReadAllText(assets));
            if (json.RootElement.TryGetProperty("project", out var project) &&
                project.TryGetProperty("restore", out var restore) &&
                restore.TryGetProperty("originalTargetFrameworks", out var restoredFrameworks) &&
                restoredFrameworks.ValueKind == JsonValueKind.Array &&
                restoredFrameworks.GetArrayLength() > 0 &&
                project.TryGetProperty("frameworks", out var assetFrameworks) &&
                assetFrameworks.ValueKind == JsonValueKind.Object &&
                assetFrameworks.EnumerateObject().Any())
            {
                using var evaluated = Evaluate(entry);
                var properties = evaluated.RootElement.GetProperty("Properties");
                var actualFrameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in new[] { "TargetFramework", "TargetFrameworks" })
                    foreach (var framework in properties.GetProperty(name).GetString()!.Split(';', StringSplitOptions.RemoveEmptyEntries))
                        actualFrameworks.Add(framework);
                if (!actualFrameworks.SetEquals(restoredFrameworks.EnumerateArray().Select(f => f.GetString()!)) ||
                    !actualFrameworks.SetEquals(assetFrameworks.EnumerateObject().Select(f => f.Name)))
                    throw new InvalidOperationException($"Stale assets for {entry}: target frameworks changed; run dotnet restore.");
                var runtime = properties.GetProperty("RuntimeIdentifier").GetString();
                if (!string.IsNullOrEmpty(runtime) &&
                    (!restore.TryGetProperty("runtimes", out var runtimes) ||
                        runtimes.ValueKind != JsonValueKind.Array ||
                        !runtimes.EnumerateArray().Any(r => r.ValueKind == JsonValueKind.String &&
                            string.Equals(r.GetString(), runtime, StringComparison.OrdinalIgnoreCase))))
                    throw new InvalidOperationException($"Stale assets for {entry}: runtime changed; run dotnet restore.");
                var targetImports = new List<string>();
                foreach (var assetFramework in assetFrameworks.EnumerateObject())
                {
                    using var target = Evaluate(entry, assetFramework.Name);
                    ValidateRequestedPackages(entry, assetFramework.Name, assetFramework.Value,
                        target.RootElement.GetProperty("Items"));
                    ValidateProjectReferences(entry, assetFramework.Name, restore,
                        target.RootElement.GetProperty("Items"));
                    var targetProperties = target.RootElement.GetProperty("Properties");
                    if (targetProperties.TryGetProperty("MSBuildAllProjects", out var targetProjects))
                        targetImports.AddRange(targetProjects.GetString()!.Split(';', StringSplitOptions.RemoveEmptyEntries));
                }

                var clone = new SortedDictionary<string, object?>(StringComparer.Ordinal);
                if (restore.TryGetProperty("originalTargetFrameworks", out var restored))
                    clone["targetFrameworks"] = restored.EnumerateArray().Select(f => f.GetString()).OrderBy(f => f).ToArray();
                if (project.TryGetProperty("frameworks", out var frameworks))
                    clone["projectFrameworks"] = frameworks.EnumerateObject()
                        .OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => new
                        {
                            framework = f.Name,
                            dependencies = f.Value.TryGetProperty("dependencies", out var dependencies)
                                ? dependencies.EnumerateObject().OrderBy(d => d.Name, StringComparer.Ordinal)
                                    .Select(d => new { id = d.Name, value = d.Value.GetRawText() }).ToArray()
                                : [],
                            centralVersions = f.Value.TryGetProperty("centralPackageVersions", out var central)
                                ? central.EnumerateObject().OrderBy(d => d.Name, StringComparer.Ordinal)
                                    .Select(d => new { id = d.Name, value = d.Value.GetRawText() }).ToArray()
                                : []
                        }).ToArray();
                var projectRoot = GitRoot(Path.GetDirectoryName(entry)!);
                var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { entry };
                for (var directory = Path.GetDirectoryName(entry); directory is not null && Within(projectRoot, directory);
                    directory = Path.GetDirectoryName(directory))
                    foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "NuGet.Config" })
                    {
                        var path = Path.Combine(directory, name);
                        if (File.Exists(path))
                            sources.Add(path);
                    }

                if (properties.TryGetProperty("MSBuildAllProjects", out var imports))
                    targetImports.AddRange(imports.GetString()!.Split(';', StringSplitOptions.RemoveEmptyEntries));
                foreach (var path in targetImports)
                    if (Path.IsPathFullyQualified(path) && Within(projectRoot, path) &&
                        !Rel(projectRoot, path).Split('/').Any(p => p.Equals(".github", StringComparison.OrdinalIgnoreCase)) &&
                        File.Exists(path))
                        sources.Add(path);
                var sourceDigest = string.Join("\n", sources.OrderBy(p => Rel(projectRoot, p), StringComparer.Ordinal)
                    .Select(p => Rel(projectRoot, p) + ":" + HashText(Canonical(File.ReadAllBytes(p)))));
                return HashText(JsonSerializer.Serialize(clone) + "\n" + sourceDigest);
            }

            throw new InvalidOperationException($"Stale or incomplete assets for {entry}; run dotnet restore.");
        }

        return HashText(string.Join("|", Projects(entry, GitRoot(Path.GetDirectoryName(entry)!))
            .OrderBy(p => p).Select(RestoreSpec)));
    }

    private static JsonDocument Evaluate(string project, string? framework = null)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(project)!
        };
        foreach (var argument in new[] { "msbuild", project, "-getProperty:TargetFramework",
            "-getProperty:TargetFrameworks", "-getProperty:RuntimeIdentifier", "-getProperty:MSBuildAllProjects",
            "-getProperty:BaseIntermediateOutputPath", "-getProperty:IntermediateOutputPath",
            "-getProperty:BaseOutputPath", "-getProperty:OutputPath", "-getProperty:TargetDir",
            "-getProperty:PublishDir", "-getProperty:CompilerGeneratedFilesOutputPath",
            "-getProperty:GeneratedFilesOutputPath",
            "-getItem:PackageReference", "-getItem:PackageVersion", "-getItem:ProjectReference" })
            start.ArgumentList.Add(argument);
        if (framework is not null)
            start.ArgumentList.Add("-p:TargetFramework=" + framework);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start dotnet msbuild.");
        var output = process.StandardOutput.ReadToEnd();
        var errors = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(120000) || process.ExitCode != 0)
            throw new InvalidOperationException($"Cannot evaluate current restore specification: {errors}");
        return JsonDocument.Parse(output);
    }

    private static void Restore(string entry)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(entry)!
        };
        start.ArgumentList.Add("restore");
        start.ArgumentList.Add(entry);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start dotnet restore.");
        var output = process.StandardOutput.ReadToEnd();
        var errors = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(120000) || process.ExitCode != 0)
            throw new InvalidOperationException($"dotnet restore failed: {output}\n{errors}");
    }

    private static void ValidateRequestedPackages(string entry, string framework, JsonElement assets, JsonElement items)
    {
        var versions = items.GetProperty("PackageVersion").EnumerateArray()
            .Where(p => p.TryGetProperty("Version", out _))
            .ToDictionary(p => p.GetProperty("Identity").GetString()!, p => p.GetProperty("Version").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        var dependencies = assets.TryGetProperty("dependencies", out var declared)
            ? declared.EnumerateObject().ToDictionary(d => d.Name, d => d.Value, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in items.GetProperty("PackageReference").EnumerateArray())
        {
            var id = reference.GetProperty("Identity").GetString()!;
            requested.Add(id);
            var version = reference.TryGetProperty("VersionOverride", out var overrideValue) &&
                !string.IsNullOrWhiteSpace(overrideValue.GetString()) ? overrideValue.GetString() :
                reference.TryGetProperty("Version", out var directValue) &&
                !string.IsNullOrWhiteSpace(directValue.GetString()) ? directValue.GetString() :
                versions.GetValueOrDefault(id);
            if (!dependencies.TryGetValue(id, out var dependency) ||
                !dependency.TryGetProperty("version", out var restoredVersion) ||
                (version is not null && restoredVersion.GetString() is { } resolved &&
                    resolved != version && resolved != $"[{version}]" &&
                    !resolved.StartsWith($"[{version}, ", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    $"Stale assets for {entry} ({framework}): PackageReference {id} changed; run dotnet restore.");
        }

        foreach (var id in dependencies.Keys)
            if (!requested.Contains(id))
                throw new InvalidOperationException(
                    $"Stale assets for {entry} ({framework}): PackageReference {id} was removed; run dotnet restore.");
    }

    private static void ValidateProjectReferences(string entry, string framework, JsonElement restore, JsonElement items)
    {
        var evaluated = items.GetProperty("ProjectReference").EnumerateArray()
            .Select(reference => Path.GetFullPath(reference.GetProperty("FullPath").GetString()!))
            .ToHashSet(Physical);
        var restored = new HashSet<string>(Physical);
        if (restore.TryGetProperty("frameworks", out var frameworks) &&
            frameworks.ValueKind == JsonValueKind.Object &&
            frameworks.TryGetProperty(framework, out var restoredFramework) &&
            restoredFramework.ValueKind == JsonValueKind.Object &&
            restoredFramework.TryGetProperty("projectReferences", out var references))
        {
            if (references.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"Stale or incomplete assets for {entry} ({framework}); run dotnet restore.");
            foreach (var reference in references.EnumerateObject())
                restored.Add(Path.GetFullPath(reference.Name, Path.GetDirectoryName(entry)!));
        }

        if (!evaluated.SetEquals(restored))
            throw new InvalidOperationException(
                $"Stale assets for {entry} ({framework}): ProjectReference changed; run dotnet restore.");
    }

    private static List<Change> Plan(string root, string scope, ContextState? old, ContextState? next,
        Dictionary<string, string>? content,
        string manifestPath, bool force)
    {
        var changes = new List<Change>();
        var previousFiles = old is null ? [] : old.References.Concat(old.ToolOwnedFiles).ToArray();
        var desiredFiles = next is null ? [] : next.References.Concat(next.ToolOwnedFiles).ToArray();
        foreach (var file in previousFiles.Concat(desiredFiles).Select(f => f.Path).Distinct(Portable).OrderBy(p => p))
        {
            ValidateRelative(file);
            var destination = Full(Path.Combine(scope, ".trellis"), file);
            CheckDestination(root, scope, destination);
            var before = File.Exists(destination) ? File.ReadAllBytes(destination) : null;
            var owned = previousFiles.SingleOrDefault(f => Portable.Equals(f.Path, file));
            var expected = desiredFiles.SingleOrDefault(f => Portable.Equals(f.Path, file));
            if (before is not null && owned is null && !force)
                throw new InvalidOperationException($"Unowned destination {destination}; review before --force adoption.");
            if (owned is not null && (before is null || HashText(Canonical(before)) != owned.CanonicalSha256) && !force)
                throw new InvalidOperationException($"Modified owned file {destination}; review before --force.");
            var text = expected is null ? null : content![expected.Path];
            var after = text is null ? null : Bom.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
            Add(changes, destination, before, after, expected is null ? "Remove" : before is null ? "Create" : "Update");
        }

        var instructionPaths = (old?.InstructionEntries ?? []).Concat(next?.InstructionEntries ?? [])
            .Select(e => e.InstructionFile).Distinct(Portable).OrderBy(p => p).ToArray();
        foreach (var file in instructionPaths)
        {
            ValidateRelative(file);
            if (file.Split('/').Any(p => p.Equals(".github", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Excluded instruction path: " + file);
            var path = Full(root, file);
            if ((old?.InstructionEntries.Any(e => Portable.Equals(e.InstructionFile, file)) == true &&
                 !GovernsSource(root, scope, old.SourceRoots, path)) ||
                (next?.InstructionEntries.Any(e => Portable.Equals(e.InstructionFile, file)) == true &&
                 !GovernsSource(root, scope, next.SourceRoots, path)))
                throw new InvalidOperationException($"Instruction path does not govern recorded source: {file}");
            CheckDestination(root, root, path);
            var before = File.Exists(path) ? File.ReadAllBytes(path) : null;
            var original = before is null ? "" : DecodeInstruction(before);
            var owned = old?.InstructionEntries.SingleOrDefault(e => Portable.Equals(e.InstructionFile, file));
            var target = next?.InstructionEntries.SingleOrDefault(e => Portable.Equals(e.InstructionFile, file));
            var updated = Merge(original, scope, path, owned, target is not null, force);
            byte[]? after = updated is null ? null : before is null ? Bom.GetPreamble().Concat(Encoding.UTF8.GetBytes(updated)).ToArray()
                : EncodeInstruction(updated, before);
            Add(changes, path, before, after,
                (target is null ? "Remove pointer " : "Update pointer ") + ScopeKey(scope));
        }

        var priorManifest = File.Exists(manifestPath) ? File.ReadAllBytes(manifestPath) : null;
        var newManifest = next is null ? null : Bom.GetPreamble().Concat(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(next, Json) + "\n")).ToArray();
        Add(changes, manifestPath, priorManifest, newManifest, next is null ? "Remove manifest" : "Update manifest");
        return changes;
    }

    private static bool GovernsSource(string root, string scope, string[] sources, string instruction)
    {
        if (!Path.GetFileName(instruction).Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase))
            return false;
        var directory = Path.GetDirectoryName(instruction)!;
        return sources.Any(source =>
        {
            var selected = Full(scope, source);
            return Within(root, selected) && (Within(directory, selected) || Within(selected, directory));
        });
    }

    private static string Index(ContextState state)
    {
        var index = new StringBuilder("# Trellis agent context\n\n");
        if (state.EntryPoints.Contains("api-reference/trellis-start-here.md"))
            index.AppendLine("Start with [Trellis API router](api-reference/trellis-start-here.md).").AppendLine();
        index.AppendLine("## Start here").AppendLine();
        foreach (var entry in state.EntryPoints)
            index.Append("- [").Append(EscapeLabel(entry)).Append("](").Append(string.Join('/',
                    entry.Split('/').Select(Uri.EscapeDataString))).AppendLine(")");
        index.AppendLine().AppendLine("## All contributed documents").AppendLine();
        foreach (var reference in state.References)
            index.Append("- [").Append(EscapeLabel(reference.Path)).Append("](").Append(string.Join('/',
                    reference.Path.Split('/').Select(Uri.EscapeDataString)))
                .Append(") — ").AppendLine(string.Join(", ", reference.Sources.Select(s =>
                    EscapeLabel(s.Package) + " " + EscapeLabel(s.PackageVersion))));
        return index.ToString().Replace("\r\n", "\n");
    }

    private static string EscapeLabel(string value) => value.Replace("\\", "\\\\")
        .Replace("\r", " ").Replace("\n", " ").Replace("[", "\\[").Replace("]", "\\]")
        .Replace("`", "\\`").Replace("*", "\\*").Replace("_", "\\_");

    private static string? Merge(string content, string scope, string file, InstructionEntry? owned, bool include, bool force)
    {
        var first = content.IndexOf(Start, StringComparison.Ordinal);
        var last = content.IndexOf(End, StringComparison.Ordinal);
        if ((first < 0) != (last < 0) || (first >= 0 &&
            (content.IndexOf(Start, first + Start.Length, StringComparison.Ordinal) >= 0 ||
             content.IndexOf(End, last + End.Length, StringComparison.Ordinal) >= 0 || last < first)))
            throw new InvalidOperationException($"Incomplete or duplicate marker in {file}.");
        var path = Path.GetRelativePath(Path.GetDirectoryName(file)!, Path.Combine(scope, ".trellis", "README.md"))
            .Replace('\\', '/');
        var key = ScopeKey(scope);
        var line = $"For files under `{Path.GetRelativePath(Path.GetDirectoryName(file)!, scope).Replace('\\', '/')}/`, read `{path}`.";
        var replacement = key + "\n" + line + "\n";
        var nl = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (first < 0)
        {
            if (!include)
                return content.Length == 0 ? null : content;
            var block = Start + "\n## Trellis API usage\n\n" + replacement + End + "\n\n";
            var heading = content.IndexOf("\n# ", StringComparison.Ordinal);
            var pos = heading >= 0 ? content.IndexOf('\n', heading + 1) + 1 : 0;
            if (content.StartsWith("# ", StringComparison.Ordinal))
                pos = content.IndexOf('\n') + 1;
            if (content.StartsWith("---\n", StringComparison.Ordinal) || content.StartsWith("---\r\n", StringComparison.Ordinal))
            {
                var endFront = content.IndexOf("\n---", 4, StringComparison.Ordinal);
                if (endFront >= 0)
                {
                    var following = content.IndexOf('\n', endFront + 1);
                    pos = following < 0 ? content.Length : following + 1;
                    if (content.AsSpan(pos).StartsWith("# ", StringComparison.Ordinal))
                    {
                        var headingEnd = content.IndexOf('\n', pos);
                        pos = headingEnd < 0 ? content.Length : headingEnd + 1;
                    }
                }
            }

            return content.Insert(pos, block.Replace("\n", nl));
        }

        var blockText = content[(first + Start.Length)..last];
        var keys = new HashSet<string>(StringComparer.Ordinal);
        const string keyPrefix = "<!-- trellis-scope:";
        for (var cursor = blockText.IndexOf(keyPrefix, StringComparison.Ordinal);
            cursor >= 0; cursor = blockText.IndexOf(keyPrefix, cursor + keyPrefix.Length, StringComparison.Ordinal))
        {
            var closing = blockText.IndexOf(" -->", cursor + keyPrefix.Length, StringComparison.Ordinal);
            var token = closing < 0 ? "" : blockText[cursor..(closing + 4)];
            if (token.Length != keyPrefix.Length + 16 + 4 ||
                !token.AsSpan(keyPrefix.Length, 16).ToString().All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')) ||
                !keys.Add(token))
                throw new InvalidOperationException($"Malformed or duplicate scoped pointer in {file}.");
        }

        var entryStart = blockText.IndexOf(key, StringComparison.Ordinal);
        if (entryStart >= 0)
        {
            var nextEntry = blockText.IndexOf("<!-- trellis-scope:", entryStart + key.Length, StringComparison.Ordinal);
            var oldEntry = blockText[entryStart..(nextEntry >= 0 ? nextEntry : blockText.Length)].TrimEnd('\r', '\n');
            if (owned is null && !force)
                throw new InvalidOperationException($"Unowned pointer entry in {file}; review --force adoption.");
            if (owned is not null && HashText(oldEntry.Replace("\r\n", "\n") + "\n") != owned.CanonicalSha256 && !force)
                throw new InvalidOperationException($"Modified pointer entry in {file}; review --force.");
            if (include && string.Equals(oldEntry.Replace("\r\n", "\n") + "\n", replacement, StringComparison.Ordinal))
                return content;
            blockText = blockText[..entryStart] + (nextEntry >= 0 ? blockText[nextEntry..] : "");
        }
        else if (owned is not null && !force)
            throw new InvalidOperationException($"Owned pointer missing from {file}.");
        if (include)
            blockText += replacement;
        if (!blockText.Contains("<!-- trellis-scope:", StringComparison.Ordinal))
        {
            var suffix = content[(last + End.Length)..];
            if (suffix.StartsWith(nl + nl, StringComparison.Ordinal))
                suffix = suffix[(nl.Length * 2)..];
            return content[..first] + suffix;
        }

        return content[..(first + Start.Length)] + blockText.Replace("\r\n", "\n").Replace("\n", nl) +
            content[last..];
    }

    private static string Entry(string scope, string file)
    {
        var key = ScopeKey(scope);
        var path = Path.GetRelativePath(Path.GetDirectoryName(file)!, Path.Combine(scope, ".trellis", "README.md"))
            .Replace('\\', '/');
        return key + "\nFor files under `" + Path.GetRelativePath(Path.GetDirectoryName(file)!, scope).Replace('\\', '/') +
            "/`, read `" + path + "`.\n";
    }

    private static string DecodeInstruction(byte[] bytes)
    {
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            throw new InvalidOperationException("Only UTF-8 AGENTS.md is supported.");
        return Strict.GetString(bytes.AsSpan(bytes.AsSpan().StartsWith(UTF8Encoding.UTF8.GetPreamble()) ? 3 : 0));
    }

    private static byte[] EncodeInstruction(string text, byte[] previous)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return previous.AsSpan().StartsWith(UTF8Encoding.UTF8.GetPreamble())
            ? Bom.GetPreamble().Concat(bytes).ToArray() : bytes;
    }

    private static void Add(List<Change> changes, string path, byte[]? before, byte[]? after, string description)
    {
        if (!Equal(before, after) && !(before is not null && after is not null &&
            HashText(Canonical(before)) == HashText(Canonical(after)) && description is "Update" or "Update manifest"))
            changes.Add(new Change(path, before, after, description));
    }

    private static bool Equal(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    private static string HashText(string text) => Hash(Encoding.UTF8.GetBytes(text));
    private static string ToolVersion() =>
        typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "TrellisToolPackageVersion")?.Value ??
        throw new InvalidOperationException("The running tool has no stamped NuGet package version.");
    private static string ScopeKey(string scope) => "<!-- trellis-scope:" +
        HashText(Rel(GitRoot(scope), scope))[..16] + " -->";
    private static string Canonical(byte[] bytes) => Strict.GetString(bytes.AsSpan(bytes.AsSpan().StartsWith(UTF8Encoding.UTF8.GetPreamble()) ? 3 : 0))
        .Replace("\r\n", "\n").Replace('\r', '\n');
    private static string SafeComponent(string part) => part.ToLowerInvariant().Replace(' ', '-');

    private static ContextState LoadState(string path)
    {
        var state = JsonSerializer.Deserialize<ContextState>(File.ReadAllText(path), Json)
            ?? throw new InvalidOperationException("Empty context manifest.");
        if (state.SchemaVersion != 1 || state.TextHashFormat != "utf8-lf-no-bom-v1" ||
            state.Graph is null || state.InstructionEntries is null || state.References is null ||
            state.ToolOwnedFiles is null || state.EntryPoints is null || state.SourceRoots is null ||
            state.ExplicitSourceRoots is null)
            throw new InvalidOperationException("Unsupported or invalid context manifest.");
        foreach (var root in state.SourceRoots.Concat(state.ExplicitSourceRoots))
            if (root != ".")
                ValidateRelative(root);
        foreach (var item in state.References.Concat(state.ToolOwnedFiles))
        {
            ValidateRelative(item.Path);
            if (item.CanonicalSha256.Length != 64 || !item.CanonicalSha256.All(Uri.IsHexDigit))
                throw new InvalidOperationException("Invalid owned-file hash.");
        }

        foreach (var reference in state.References)
            if (!reference.Path.StartsWith("api-reference/", StringComparison.Ordinal) &&
                !reference.Path.StartsWith("packages/", StringComparison.Ordinal))
                throw new InvalidOperationException($"Reference path is outside the guidance namespaces: {reference.Path}");

        var ownedPaths = state.References.Concat(state.ToolOwnedFiles).Select(f => f.Path).ToArray();
        if (ownedPaths.Distinct(Portable).Count() != ownedPaths.Length ||
            state.ToolOwnedFiles.Length != 1 || state.ToolOwnedFiles[0].Path != "README.md")
            throw new InvalidOperationException("Conflicting or invalid tool-owned paths.");
        foreach (var entry in state.EntryPoints)
        {
            ValidateRelative(entry);
            if (!state.References.Any(r => r.Path == entry))
                throw new InvalidOperationException($"Unknown guidance entry point: {entry}");
        }

        foreach (var item in state.InstructionEntries)
        {
            ValidateRelative(item.InstructionFile);
            if (!Path.GetFileName(item.InstructionFile).Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase) ||
                item.CanonicalSha256.Length != 64 || !item.CanonicalSha256.All(Uri.IsHexDigit))
                throw new InvalidOperationException("Invalid instruction ownership entry.");
        }

        if (state.InstructionEntries.Select(e => e.InstructionFile).Distinct(Portable).Count() != state.InstructionEntries.Length)
            throw new InvalidOperationException("Duplicate instruction ownership entry.");
        return state;
    }

    private static string[] Projects(string entry, string root)
    {
        Inside(root, entry);
        RejectExcluded(root, entry);
        EnsureDirectoriesSafe(root, entry);
        if (!File.Exists(entry))
            throw new InvalidOperationException($"Entry point not found: {entry}");
        if (entry.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return [entry];
        if (entry.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            var xml = System.Xml.Linq.XDocument.Load(entry);
            return xml.Descendants().Where(x => x.Name.LocalName == "Project")
                .Select(x => Path.GetFullPath((string?)x.Attribute("Path") ??
                    throw new InvalidOperationException("Solution project without Path."), Path.GetDirectoryName(entry)!))
                .Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        if (entry.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            return File.ReadAllLines(entry).Where(l => l.StartsWith("Project(", StringComparison.Ordinal))
                .Select(l => l.Split(',').ElementAtOrDefault(1)?.Trim().Trim('"'))
                .Where(l => l?.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) == true)
                .Select(l => Path.GetFullPath(l!, Path.GetDirectoryName(entry)!)).ToArray();
        throw new InvalidOperationException("Expected a .csproj, .slnx, or .sln entry point.");
    }

    private static HashSet<string> GeneratedDirectories(string[] projects)
    {
        var generated = new HashSet<string>(Physical);
        foreach (var project in projects)
        {
            using var evaluated = Evaluate(project);
            var properties = evaluated.RootElement.GetProperty("Properties");
            var frameworks = properties.GetProperty("TargetFrameworks").GetString()!
                .Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (frameworks.Length == 0)
                frameworks = [properties.GetProperty("TargetFramework").GetString()!];
            foreach (var framework in frameworks)
            {
                using var target = Evaluate(project, framework);
                var targetProperties = target.RootElement.GetProperty("Properties");
                foreach (var name in new[] { "BaseIntermediateOutputPath", "IntermediateOutputPath",
                    "BaseOutputPath", "OutputPath", "TargetDir", "PublishDir",
                    "CompilerGeneratedFilesOutputPath", "GeneratedFilesOutputPath" })
                    if (targetProperties.TryGetProperty(name, out var value) &&
                        value.GetString() is { Length: > 0 } path)
                        generated.Add(Path.TrimEndingDirectorySeparator(
                            Path.GetFullPath(path, Path.GetDirectoryName(project)!)));
            }
        }

        return generated;
    }

    private static IEnumerable<string> InstructionFiles(string root, string scope, string[] sourceRoots,
        HashSet<string> generatedDirectories)
    {
        var files = new HashSet<string>(Portable);
        foreach (var source in sourceRoots)
        {
            var directory = Full(scope, source);
            Inside(scope, directory);
            for (var parent = directory; parent is not null && Within(root, parent); parent = Path.GetDirectoryName(parent))
            {
                var candidate = Path.Combine(parent, "AGENTS.md");
                if (File.Exists(candidate))
                {
                    files.Add(candidate);
                    break;
                }
            }

            if (!files.Any(f => Within(f is null ? "" : Path.GetDirectoryName(f)!, directory)))
                files.Add(Path.Combine(root, "AGENTS.md"));
            Walk(directory, files, generatedDirectories);
        }

        return files.OrderBy(p => p, StringComparer.Ordinal);

        static void Walk(string dir, HashSet<string> found, HashSet<string> generatedDirectories)
        {
            if (!Directory.Exists(dir))
                return;
            foreach (var child in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(child);
                if (generatedDirectories.Any(directory => Within(directory, child)) ||
                    name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(".trellis", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(".github", StringComparison.OrdinalIgnoreCase) ||
                    File.Exists(Path.Combine(child, ".git")) || Directory.Exists(Path.Combine(child, ".git")) ||
                    File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint))
                    continue;
                var instruction = Path.Combine(child, "AGENTS.md");
                if (File.Exists(instruction))
                    found.Add(instruction);
                Walk(child, found, generatedDirectories);
            }
        }
    }

    private static void ValidateTool(string cwd, string scope, string version)
    {
        string? selected = null;
        var root = GitRoot(cwd);
        for (var dir = cwd; dir is not null && Within(root, dir); dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, ".config", "dotnet-tools.json");
            CheckDestination(root, root, candidate);
            if (!File.Exists(candidate))
                continue;
            using var json = JsonDocument.Parse(File.ReadAllText(candidate));
            var manifestRoot = json.RootElement;
            var isRoot = manifestRoot.TryGetProperty("isRoot", out var r) && r.ValueKind == JsonValueKind.True;
            if (manifestRoot.TryGetProperty("tools", out var tools))
                foreach (var tool in tools.EnumerateObject())
                    if (tool.Value.TryGetProperty("commands", out var commands) &&
                        commands.EnumerateArray().Any(c => c.GetString() == "trellis"))
                        selected = candidate;
            if (selected is not null || isRoot)
                break;
        }

        var expected = Path.Combine(scope, ".config", "dotnet-tools.json");
        if (!string.Equals(selected, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Run from scope '{scope}' with pinned trellis in {expected}.");
        using var manifest = JsonDocument.Parse(File.ReadAllText(expected));
        if (!manifest.RootElement.TryGetProperty("isRoot", out var flag) || flag.ValueKind != JsonValueKind.True ||
            !manifest.RootElement.TryGetProperty("tools", out var declared))
            throw new InvalidOperationException($"Scoped manifest must declare isRoot true: {expected}");
        var matches = declared.EnumerateObject().Where(t => t.Value.TryGetProperty("commands", out var commands) &&
            commands.EnumerateArray().Any(c => c.GetString() == "trellis")).ToArray();
        if (matches.Length != 1 || !matches[0].Value.TryGetProperty("version", out var pin) || pin.GetString() != version)
            throw new InvalidOperationException($"Scoped manifest must pin running tool version {version}: {expected}");
    }

    private static FileStream Lock(string root)
    {
        var dotGit = Path.Combine(root, ".git");
        var gitDirectory = Directory.Exists(dotGit) ? dotGit : ReadGitDirectory(root, dotGit);
        var path = Path.Combine(gitDirectory, "trellis-agent-context.lock");
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static string ReadGitDirectory(string root, string dotGit)
    {
        var value = File.ReadAllText(dotGit).Trim();
        if (!value.StartsWith("gitdir: ", StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid linked worktree metadata.");
        return Path.GetFullPath(value[8..], root);
    }

    private static string[] AtomicProbeDirectories(IEnumerable<string> destinations) =>
        destinations.Select(destination =>
        {
            for (var parent = Path.GetDirectoryName(destination); parent is not null; parent = Path.GetDirectoryName(parent))
                if (Directory.Exists(parent))
                    return parent;
            throw new InvalidOperationException($"No existing destination parent for {destination}.");
        }).Distinct(Physical).OrderBy(directory => directory, StringComparer.Ordinal).ToArray();

    private static void ProbeAtomic(string root, IEnumerable<string> destinations)
    {
        foreach (var directory in AtomicProbeDirectories(destinations))
        {
            CheckDestination(root, root, directory);
            ProbeAtomicDirectory(directory);
        }
    }

    private static void ProbeAtomicDirectory(string directory)
    {
        var left = Path.Combine(directory, "trellis-atomic-" + Guid.NewGuid().ToString("N"));
        var right = left + "-target";
        try
        {
            using (var source = new FileStream(left, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                source.WriteByte(1);
            using (var target = new FileStream(right, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                target.WriteByte(2);
            File.Move(left, right, true);
            if (File.ReadAllBytes(right).Length != 1)
                throw new IOException("Atomic replacement is unsupported.");
        }
        finally
        {
            if (File.Exists(left)) File.Delete(left);
            if (File.Exists(right)) File.Delete(right);
        }
    }

    private static string GitRoot(string directory)
    {
        for (var current = directory; current is not null; current = Path.GetDirectoryName(current))
            if (Directory.Exists(Path.Combine(current, ".git")) || File.Exists(Path.Combine(current, ".git")))
                return current;
        throw new InvalidOperationException("No Git working tree found.");
    }

    private static void CheckDestination(string root, string boundary, string file)
    {
        Inside(boundary, file);
        RejectExcluded(root, file);
        EnsureDirectoriesSafe(root, file);
        var directory = root;
        foreach (var component in Rel(root, file).Split('/'))
        {
            if (!Directory.Exists(directory))
                break;
            var aliases = Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName)
                .Where(name => Portable.Equals(name!.Normalize(NormalizationForm.FormC), component.Normalize(NormalizationForm.FormC)))
                .ToArray();
            if (aliases.Length > 1 || (aliases.Length == 1 && aliases[0] != component))
                throw new InvalidOperationException($"Portable path alias at {Path.Combine(directory, component)}.");
            directory = Path.Combine(directory, component);
        }
    }

    private static void EnsureDirectoriesSafe(string root, string path)
    {
        Inside(root, path);
        var relative = Rel(root, path);
        var current = root;
        if (File.Exists(root) && File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("Linked root is unsupported.");
        foreach (var component in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException($"Link/reparse point at {current}.");
        }
    }

    private static void RejectExcluded(string root, string path)
    {
        if (Rel(root, path).Split('/').Any(p => p.Equals(".github", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(".github is excluded from agent-context operations.");
    }

    private static void ValidateRelative(string value)
    {
        if (string.IsNullOrEmpty(value) || Path.IsPathRooted(value) || value.Contains('\\') ||
            value.Split('/').Any(p => p is "" or "." or ".." || p.Contains(':') ||
                p.Equals(".github", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Unsafe manifest path: {value}");
    }

    private static string Full(string root, string relative)
    {
        if (relative == ".") return root;
        ValidateRelative(relative);
        var result = Path.GetFullPath(relative.Replace('/', Path.DirectorySeparatorChar), root);
        Inside(root, result);
        return result;
    }

    private static string Rel(string root, string path)
    {
        Inside(root, path);
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private static bool Within(string root, string path)
    {
        if (!Path.IsPathFullyQualified(root) || !Path.IsPathFullyQualified(path))
            return false;
        var absoluteRoot = Path.GetFullPath(root);
        var absolutePath = Path.GetFullPath(path);
        var prefix = Path.EndsInDirectorySeparator(absoluteRoot) ? absoluteRoot : absoluteRoot + Path.DirectorySeparatorChar;
        return absolutePath.Equals(absoluteRoot, PhysicalComparison) ||
            absolutePath.StartsWith(prefix, PhysicalComparison);
    }

    private static void Inside(string root, string path)
    {
        if (!Within(root, path))
            throw new InvalidOperationException($"Path escapes scope: {path}");
    }
}

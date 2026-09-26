namespace Trellis.Guidance.Reader.Tests;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;

public sealed class GuidanceReaderTests
{
    [Theory]
    [InlineData("valid", GuidanceStatus.Valid)]
    [InlineData("metadata-only", GuidanceStatus.Valid)]
    [InlineData("unsupported", GuidanceStatus.UnsupportedSchema)]
    [InlineData("invalid-entrypoint", GuidanceStatus.InvalidManifest)]
    [InlineData("invalid-alias", GuidanceStatus.InvalidManifest)]
    [InlineData("invalid-traversal", GuidanceStatus.InvalidManifest)]
    [InlineData("invalid-hash", GuidanceStatus.InvalidManifest)]
    [InlineData("invalid-nfc", GuidanceStatus.InvalidManifest)]
    public void Discover_Fixture_Returns_expected_status(string fixture, GuidanceStatus status)
    {
        using var graph = new Graph();
        graph.Package("Other.Publisher", "2.7.3", fixture);
        var result = GuidanceReader.Discover([graph.Assets]);
        result.Packages.Should().ContainSingle().Which.Status.Should().Be(status);
        result.IsSuccessful.Should().Be(status == GuidanceStatus.Valid);
        if (fixture == "valid")
        {
            var contribution = result.Packages.Single().Contribution!;
            contribution.EntryPoints.Should().ContainSingle().Which.Should().Be("guide/intro.md");
            contribution.Documents.Single().Sha256.Should().Be(
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(contribution.Documents.Single().LocalPath))).ToLowerInvariant());
            contribution.Documents.Single().Identity.PackageId.Should().Be("Other.Publisher");
            contribution.PublisherMetadata["org.example"].GetProperty("edition").GetString().Should().Be("free");
        }

        if (fixture == "invalid-hash")
            result.Packages.Single().Diagnostic.Should().Contain("SHA-256");

        if (fixture == "invalid-nfc")
            result.Packages.Single().Diagnostic.Should().Contain("alias");
    }

    [Fact]
    public void Discover_Unknown_large_integer_schema_is_unsupported()
    {
        using var graph = new Graph();
        graph.Package("Other", "1.0.0", manifest: """{"schemaVersion":2147483648,"documents":[],"entryPoints":[]}""");
        GuidanceReader.Discover([graph.Assets]).Packages.Single().Status.Should().Be(GuidanceStatus.UnsupportedSchema);
    }

    [Fact]
    public void Discover_Namespaced_publisher_profile_is_opaque_to_generic_reader()
    {
        using var graph = new Graph();
        graph.Package("Trellis.Core", "8.2.1", manifest:
            """{"schemaVersion":1,"documents":[],"entryPoints":[],"publisherMetadata":{"org.trellis":{"lockstepCohort":["Trellis.Core","Trellis.Asp"]}}}""");
        var result = GuidanceReader.Discover([graph.Assets]);
        result.IsSuccessful.Should().BeTrue();
        result.Packages.Single().Contribution!.PublisherMetadata["org.trellis"]
            .GetProperty("lockstepCohort").EnumerateArray().Select(p => p.GetString())
            .Should().Equal("Trellis.Core", "Trellis.Asp");
    }

    [Fact]
    public void Discover_Mixed_graph_preserves_outcomes_and_is_unsuccessful()
    {
        using var graph = new Graph();
        graph.Package("Publisher.One", "1.0.0", "valid");
        graph.Package("Publisher.Two", "99.0.0", "unsupported");
        graph.Package("Publisher.Three", "3.0.0");
        graph.Package("Publisher.Four", "4.0.0", createDirectory: false);
        var result = GuidanceReader.Discover([graph.Assets]);
        result.IsSuccessful.Should().BeFalse();
        result.Packages.Select(p => p.Status).Should().BeEquivalentTo(
            [GuidanceStatus.Valid, GuidanceStatus.UnsupportedSchema, GuidanceStatus.NoManifest, GuidanceStatus.MissingAssets]);
        result.Packages.Single(p => p.PackageId == "Publisher.One").Contribution.Should().NotBeNull();
        result.Packages.Single(p => p.PackageId == "Publisher.Four").Diagnostic.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Discover_Separate_scopes_and_same_filenames_retain_provenance()
    {
        using var first = new Graph();
        using var second = new Graph();
        first.Package("Publisher.One", "1.0.0", "valid");
        second.Package("Publisher.Two", "9.0.0", "valid");
        var result = GuidanceReader.Discover([first.Assets, second.Assets]);
        result.IsSuccessful.Should().BeTrue();
        result.Packages.Select(p => p.Scope.ProjectPath).Distinct().Should().HaveCount(2);
        result.Packages.Select(p => p.Contribution!.Documents.Single().Identity).Distinct().Should().HaveCount(2);
    }

    [Theory]
    [InlineData("{not json", GuidanceStatus.InvalidManifest)]
    [InlineData("{\"schemaVersion\":1,\"documents\":[{\"path\":\"a.md\",\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}],\"entryPoints\":[\"a.md\"]}", GuidanceStatus.InvalidManifest)]
    [InlineData("{\"schemaVersion\":1,\"documents\":[{\"path\":\"a.md\",\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"},{\"path\":\"A.md\",\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}],\"entryPoints\":[\"a.md\"]}", GuidanceStatus.InvalidManifest)]
    [InlineData("{\"schemaVersion\":1,\"documents\":[{\"path\":\"a.md\",\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}],\"entryPoints\":[]}", GuidanceStatus.InvalidManifest)]
    [InlineData("{\"schemaVersion\":1,\"documents\":[{\"path\":\"a.md\",\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}],\"entryPoints\":[\"a.md\"],\"routes\":{\"net10.0\":\"a.md\"}}", GuidanceStatus.InvalidManifest)]
    public void Discover_Invalid_manifests_fail(string manifest, GuidanceStatus status)
    {
        using var graph = new Graph();
        graph.Package("Other", "1.0.0", manifest: manifest);
        File.WriteAllText(Path.Combine(graph.Root, "cache", "other", "1.0.0", "a.md"), "Hello.\n");
        GuidanceReader.Discover([graph.Assets]).Packages.Single().Status.Should().Be(status);
    }

    [Fact]
    public void Discover_Missing_document_is_not_absent_manifest()
    {
        using var graph = new Graph();
        graph.Package("Other", "1.0.0", "valid");
        File.Delete(Path.Combine(graph.Root, "cache", "other", "1.0.0", "guide", "intro.md"));
        GuidanceReader.Discover([graph.Assets]).Packages.Single().Status.Should().Be(GuidanceStatus.MissingAssets);
    }

    [Fact]
    public void Discover_Incomplete_package_cache_is_not_no_manifest()
    {
        using var graph = new Graph();
        graph.Package("Other", "1.0.0");
        File.Delete(Path.Combine(graph.Root, "cache", "other", "1.0.0", ".nupkg.metadata"));
        GuidanceReader.Discover([graph.Assets]).Packages.Single().Status.Should().Be(GuidanceStatus.MissingAssets);
    }

    [Theory]
    [InlineData("guidance/reference-manifest.json")]
    [InlineData("Guidance\\Reference-Manifest.json")]
    public void Discover_Listed_but_missing_manifest_is_missing_assets(string listedPath)
    {
        using var graph = new Graph();
        graph.Package("Other", "1.0.0", "valid");
        File.Delete(Path.Combine(graph.Root, "cache", "other", "1.0.0", "guidance", "reference-manifest.json"));
        using var assets = JsonDocument.Parse(File.ReadAllText(graph.Assets));
        var node = System.Text.Json.Nodes.JsonNode.Parse(assets.RootElement.GetRawText())!;
        node["libraries"]!["Other/1.0.0"]!["files"] =
            new System.Text.Json.Nodes.JsonArray(".nupkg.metadata", listedPath);
        File.WriteAllText(graph.Assets, node.ToJsonString());

        var result = GuidanceReader.Discover([graph.Assets]);
        result.IsSuccessful.Should().BeFalse();
        result.Packages.Single().Status.Should().Be(GuidanceStatus.MissingAssets);
    }

    [Theory]
    [InlineData("Docs/one.md", "docs/two.md")]
    [InlineData("caf\u00e9/one.md", "cafe\u0301/two.md")]
    [InlineData("docs\\one.md", "Docs/two.md")]
    [InlineData("intro.md", "INTRO.md")]
    [InlineData("guide/intro.md", "guide\\intro.md")]
    [InlineData("guide", "guide/intro.md")]
    public void Discover_Portable_aliases_fail_before_loading_missing_payloads(string first, string second)
    {
        using var graph = new Graph();
        var manifest = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            documents = new[] { new { path = first, sha256 = new string('a', 64) },
                new { path = second, sha256 = new string('a', 64) } },
            entryPoints = new[] { first }
        });
        graph.Package("Other", "1.0.0", manifest: manifest);
        var outcome = GuidanceReader.Discover([graph.Assets]).Packages.Single();
        outcome.Status.Should().Be(GuidanceStatus.InvalidManifest);
        (outcome.Diagnostic!.Contains("alias", StringComparison.Ordinal) ||
            outcome.Diagnostic.Contains("directory prefix", StringComparison.Ordinal)).Should().BeTrue();
    }

    [Fact]
    public void Discover_Modified_bytes_fail_integrity()
    {
        using var graph = new Graph();
        graph.Package("Other", "1.0.0", "valid");
        File.AppendAllText(Path.Combine(graph.Root, "cache", "other", "1.0.0", "guide", "intro.md"), "tampered");
        var outcome = GuidanceReader.Discover([graph.Assets]).Packages.Single();
        outcome.Status.Should().Be(GuidanceStatus.InvalidManifest);
        outcome.Diagnostic.Should().Contain("SHA-256");
    }

    [Fact]
    public void Discover_Assets_missing_is_explicit_failure()
    {
        using var graph = new Graph();
        var result = GuidanceReader.Discover([Path.Combine(graph.Root, "missing.assets.json")]);
        result.IsSuccessful.Should().BeFalse();
        result.Diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public void Discover_No_selected_assets_is_not_a_successful_graph()
    {
        var result = GuidanceReader.Discover([]);
        result.IsSuccessful.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle();
    }

    [Fact]
    public void Discover_Real_restored_test_project_assets_requires_no_manifest()
    {
        var assets = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "obj", "project.assets.json"));
        var result = GuidanceReader.Discover([assets]);
        result.IsSuccessful.Should().BeTrue(string.Join(Environment.NewLine, result.Diagnostics.Concat(
            result.Packages.Where(p => p.Diagnostic is not null).Select(p => p.Diagnostic!))));
        result.Packages.Should().NotBeEmpty();
        result.Packages.Should().OnlyContain(p => p.Status == GuidanceStatus.NoManifest);
    }

    [Fact]
    public void Discover_Linked_document_is_rejected()
    {
        using var graph = new Graph();
        graph.Package("Other", "1.0.0", "valid");
        var path = Path.Combine(graph.Root, "cache", "other", "1.0.0", "guide", "intro.md");
        File.Delete(path);
        try
        {
            File.CreateSymbolicLink(path, Path.Combine(graph.Root, "outside.md"));
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        GuidanceReader.Discover([graph.Assets]).Packages.Single().Status.Should().Be(GuidanceStatus.InvalidManifest);
    }

    [Fact]
    public void Discover_Linked_nested_directory_is_rejected()
    {
        using var graph = new Graph();
        graph.Package("Other", "1.0.0", "valid");
        var packageRoot = Path.Combine(graph.Root, "cache", "other", "1.0.0");
        var guide = Path.Combine(packageRoot, "guide");
        var external = Path.Combine(graph.Root, "external-guide");
        Directory.Move(guide, external);
        try
        {
            Directory.CreateSymbolicLink(guide, external);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        GuidanceReader.Discover([graph.Assets]).Packages.Single().Status.Should().Be(GuidanceStatus.InvalidManifest);
    }

    private sealed class Graph : IDisposable
    {
        private readonly Dictionary<string, object> _libraries = new();
        private readonly Dictionary<string, object> _targets = new();

        public Graph()
        {
            Root = Path.Combine(AppContext.BaseDirectory, "graph-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Path.Combine(Root, "cache"));
            Assets = Path.Combine(Root, "project.assets.json");
        }

        public string Root { get; }
        public string Assets { get; }

        public void Package(string id, string version, string? fixture = null, bool createDirectory = true, string? manifest = null)
        {
            var relative = id.ToLowerInvariant() + "/" + version;
            var packageRoot = Path.Combine(Root, "cache", id.ToLowerInvariant(), version);
            if (createDirectory)
            {
                Directory.CreateDirectory(packageRoot);
                File.WriteAllText(Path.Combine(packageRoot, ".nupkg.metadata"), "{}");
                if (fixture is not null)
                {
                    var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
                    foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                    {
                        var relativeFile = Path.GetRelativePath(source, file);
                        var destination = Path.Combine(packageRoot,
                            relativeFile == "reference-manifest.json" ? Path.Combine("guidance", relativeFile) : relativeFile);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        File.Copy(file, destination);
                    }
                }

                if (manifest is not null)
                {
                    Directory.CreateDirectory(Path.Combine(packageRoot, "guidance"));
                    File.WriteAllText(Path.Combine(packageRoot, "guidance", "reference-manifest.json"), manifest);
                }
            }

            _libraries[id + "/" + version] = new { type = "package", path = relative, files = new[] { ".nupkg.metadata" } };
            _targets[id + "/" + version] = new { type = "package" };
            File.WriteAllText(Assets, JsonSerializer.Serialize(new
            {
                version = 3,
                targets = new Dictionary<string, object> { ["net10.0/win-x64"] = _targets },
                libraries = _libraries,
                packageFolders = new Dictionary<string, object> { [Path.Combine(Root, "cache") + Path.DirectorySeparatorChar] = new { } },
                project = new { restore = new { projectPath = Path.Combine(Root, "app.csproj") } }
            }));
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}

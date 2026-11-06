namespace Trellis.Core.Tests.Packaging;

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

/// <summary>
/// <see cref="ApiReferencePayloadGateTests"/> runs the gate against the live repository, where every package
/// passes. These tests run the same gate logic against small project graphs written to a temporary directory,
/// so each way a package can fail the gate is proven to be detected, and each legitimate route is proven accepted.
/// </summary>
public class ApiReferencePayloadGateGraphTests
{
    private const string Ships = "<TrellisShipsApiReferenceSet>true</TrellisShipsApiReferenceSet>";
    private const string NotPackable = "<IsPackable>false</IsPackable>";

    #region Routes to the reference set

    [Fact]
    public void A_package_that_references_core_is_delivered()
    {
        using var graph = new Graph();
        var core = graph.Package("Trellis.Core", Ships);
        graph.Package("Trellis.Feature", references: new Graph.Ref(core));

        ApiReferencePayloadGateTests.UndeliveredPackages(graph.Root).Should().BeEmpty();
    }

    [Fact]
    public void A_package_reaching_core_through_another_package_is_delivered()
    {
        using var graph = new Graph();
        var core = graph.Package("Trellis.Core", Ships);
        var middle = graph.Package("Trellis.Middle", references: new Graph.Ref(core));
        graph.Package("Trellis.Leaf", references: new Graph.Ref(middle));

        ApiReferencePayloadGateTests.UndeliveredPackages(graph.Root).Should().BeEmpty();
    }

    [Fact]
    public void A_package_with_no_references_is_undelivered()
    {
        using var graph = new Graph();
        graph.Package("Trellis.Core", Ships);
        graph.Package("Trellis.Island");

        ApiReferencePayloadGateTests.UndeliveredPackages(graph.Root).Should().Equal("Trellis.Island");
    }

    [Fact]
    public void A_package_referencing_only_an_undelivered_package_is_undelivered()
    {
        using var graph = new Graph();
        graph.Package("Trellis.Core", Ships);
        var island = graph.Package("Trellis.Island");
        graph.Package("Trellis.Dependent", references: new Graph.Ref(island));

        ApiReferencePayloadGateTests.UndeliveredPackages(graph.Root)
            .Should().BeEquivalentTo(["Trellis.Island", "Trellis.Dependent"]);
    }

    [Fact]
    public async Task A_reference_cycle_with_no_route_terminates_and_is_undelivered()
    {
        using var graph = new Graph();
        graph.Package("Trellis.Core", Ships);
        var first = graph.PathOf("Trellis.First");
        var second = graph.PathOf("Trellis.Second");
        graph.Package("Trellis.First", references: new Graph.Ref(second));
        graph.Package("Trellis.Second", references: new Graph.Ref(first));

        var undelivered = await Task.Run(() => ApiReferencePayloadGateTests.UndeliveredPackages(graph.Root), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        undelivered.Should().BeEquivalentTo(["Trellis.First", "Trellis.Second"]);
    }

    [Fact]
    public async Task A_reference_cycle_that_reaches_core_is_delivered()
    {
        using var graph = new Graph();
        var core = graph.Package("Trellis.Core", Ships);
        var first = graph.PathOf("Trellis.First");
        var second = graph.PathOf("Trellis.Second");
        graph.Package("Trellis.First", references: new Graph.Ref(second));
        graph.Package("Trellis.Second", references: [new Graph.Ref(first), new Graph.Ref(core)]);

        var undelivered = await Task.Run(() => ApiReferencePayloadGateTests.UndeliveredPackages(graph.Root), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        undelivered.Should().BeEmpty();
    }

    [Theory]
    [InlineData("PrivateAssets=\"all\"")]
    [InlineData("PrivateAssets=\"ALL\"")]
    [InlineData("ReferenceOutputAssembly=\"false\"")]
    [InlineData("ReferenceOutputAssembly=\"FALSE\"")]
    public void A_private_or_build_only_reference_to_core_does_not_deliver(string attributes)
    {
        using var graph = new Graph();
        var core = graph.Package("Trellis.Core", Ships);
        graph.Package("Trellis.Hidden", references: new Graph.Ref(core, attributes));

        ApiReferencePayloadGateTests.UndeliveredPackages(graph.Root).Should().Equal("Trellis.Hidden");
    }

    [Theory]
    [InlineData("PrivateAssets=\"contentfiles\"")]
    [InlineData("ReferenceOutputAssembly=\"true\"")]
    public void A_reference_that_still_flows_as_a_dependency_delivers(string attributes)
    {
        using var graph = new Graph();
        var core = graph.Package("Trellis.Core", Ships);
        graph.Package("Trellis.Visible", references: new Graph.Ref(core, attributes));

        ApiReferencePayloadGateTests.UndeliveredPackages(graph.Root).Should().BeEmpty();
    }

    [Fact]
    public void A_private_reference_does_not_cut_off_a_second_public_route()
    {
        using var graph = new Graph();
        var core = graph.Package("Trellis.Core", Ships);
        var middle = graph.Package("Trellis.Middle", references: new Graph.Ref(core));
        graph.Package("Trellis.Both", references: [new Graph.Ref(core, "PrivateAssets=\"all\""), new Graph.Ref(middle)]);

        ApiReferencePayloadGateTests.UndeliveredPackages(graph.Root).Should().BeEmpty();
    }

    [Fact]
    public void A_private_hop_in_the_middle_of_a_route_cuts_it_off()
    {
        using var graph = new Graph();
        var core = graph.Package("Trellis.Core", Ships);
        var middle = graph.Package("Trellis.Middle", references: new Graph.Ref(core, "PrivateAssets=\"all\""));
        graph.Package("Trellis.Leaf", references: new Graph.Ref(middle));

        ApiReferencePayloadGateTests.UndeliveredPackages(graph.Root)
            .Should().BeEquivalentTo(["Trellis.Middle", "Trellis.Leaf"]);
    }

    #endregion

    #region The analyzers exception

    [Fact]
    public void The_analyzers_package_is_delivered_when_it_declares_core_delivers_and_names_its_reference()
    {
        using var graph = new Graph();
        graph.Package("Trellis.Core", Ships);
        graph.Package("Trellis.Analyzers", DeliveredByCore("analyzers"));

        ApiReferencePayloadGateTests.UndeliveredPackages(graph.Root).Should().BeEmpty();
    }

    [Fact]
    public void The_analyzers_package_declaring_core_delivers_without_a_reference_name_is_undelivered()
    {
        using var graph = new Graph();
        graph.Package("Trellis.Core", Ships);
        graph.Package("Trellis.Analyzers", "<TrellisApiReferenceDeliveredByCore>true</TrellisApiReferenceDeliveredByCore>");

        ApiReferencePayloadGateTests.UndeliveredPackages(graph.Root).Should().Equal("Trellis.Analyzers");
    }

    [Fact]
    public void The_analyzers_package_naming_a_reference_without_declaring_core_delivers_is_undelivered()
    {
        using var graph = new Graph();
        graph.Package("Trellis.Core", Ships);
        graph.Package("Trellis.Analyzers", "<TrellisApiRefName>analyzers</TrellisApiRefName>");

        ApiReferencePayloadGateTests.UndeliveredPackages(graph.Root).Should().Equal("Trellis.Analyzers");
    }

    [Fact]
    public void A_package_other_than_analyzers_cannot_claim_that_core_delivers_its_reference()
    {
        using var graph = new Graph();
        graph.Package("Trellis.Core", Ships);
        graph.Package("Trellis.Other", DeliveredByCore("other"));

        ApiReferencePayloadGateTests.UndeliveredPackages(graph.Root).Should().Equal("Trellis.Other");
        ApiReferencePayloadGateTests.PackableProjects(graph.Root).Where(p => p.DeliveredByCore).Select(p => p.Name)
            .Should().Equal("Trellis.Other");
    }

    #endregion

    #region Which projects are enumerated

    [Fact]
    public void Projects_that_opt_out_of_packing_are_not_enumerated()
    {
        using var graph = new Graph();
        graph.Package("Trellis.Core", Ships);
        graph.Package("Trellis.Sample", NotPackable);

        ApiReferencePayloadGateTests.PackableProjects(graph.Root).Select(p => p.Name).Should().Equal("Trellis.Core");
        ApiReferencePayloadGateTests.UndeliveredPackages(graph.Root).Should().BeEmpty();
    }

    [Theory]
    [InlineData("False")]
    [InlineData("FALSE")]
    [InlineData(" false ")]
    public void The_IsPackable_opt_out_ignores_case_and_surrounding_whitespace(string value)
    {
        using var graph = new Graph();
        graph.Package("Trellis.Core", Ships);
        graph.Package("Trellis.Sample", $"<IsPackable>{value}</IsPackable>");

        ApiReferencePayloadGateTests.PackableProjects(graph.Root).Select(p => p.Name).Should().Equal("Trellis.Core");
    }

    [Theory]
    [InlineData("Trellis.Feature/tests", "Trellis.Feature.Tests")]
    [InlineData("Trellis.Feature/src/bin/Debug", "Trellis.Feature")]
    [InlineData("Trellis.Feature/src/obj", "Trellis.Feature")]
    [InlineData("samples/App", "App")]
    public void Projects_outside_a_src_folder_or_inside_bin_and_obj_are_not_enumerated(string directory, string name)
    {
        using var graph = new Graph();
        graph.Package("Trellis.Core", Ships);
        graph.ProjectAt(directory, name);

        ApiReferencePayloadGateTests.PackableProjects(graph.Root).Select(p => p.Name).Should().Equal("Trellis.Core");
    }

    #endregion

    #region Reference names and layout

    [Fact]
    public void A_reference_name_with_no_file_is_reported_as_dangling()
    {
        using var graph = new Graph();
        graph.Package("Trellis.Core", Ships + "<TrellisApiRefName>core</TrellisApiRefName>");

        ApiReferencePayloadGateTests.DanglingReferenceNames(graph.Root).Should().Equal("Trellis.Core -> trellis-api-core.md");
    }

    [Fact]
    public void A_reference_name_that_resolves_to_a_file_is_not_dangling()
    {
        using var graph = new Graph();
        graph.Package("Trellis.Core", Ships + "<TrellisApiRefName>core</TrellisApiRefName>");
        graph.ReferenceFile("core");

        ApiReferencePayloadGateTests.DanglingReferenceNames(graph.Root).Should().BeEmpty();
    }

    [Fact]
    public void A_reference_name_with_a_typo_is_dangling_even_when_a_similar_file_exists()
    {
        using var graph = new Graph();
        graph.Package("Trellis.Core", Ships + "<TrellisApiRefName>cor</TrellisApiRefName>");
        graph.ReferenceFile("core");

        ApiReferencePayloadGateTests.DanglingReferenceNames(graph.Root).Should().Equal("Trellis.Core -> trellis-api-cor.md");
    }

    [Fact]
    public void A_package_that_declares_no_reference_name_is_not_reported_as_dangling()
    {
        using var graph = new Graph();
        graph.Package("Trellis.Core", Ships);

        ApiReferencePayloadGateTests.DanglingReferenceNames(graph.Root).Should().BeEmpty();
    }

    [Fact]
    public void A_package_at_the_conventional_path_is_not_misplaced()
    {
        using var graph = new Graph();
        graph.Package("Trellis.Core", Ships);

        ApiReferencePayloadGateTests.MisplacedProjects(graph.Root).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Trellis.Feature/src", "Trellis.Other")]
    [InlineData("Trellis.Feature/src/nested", "Trellis.Feature")]
    [InlineData("Elsewhere/src", "Trellis.Feature")]
    public void A_packable_project_away_from_its_conventional_path_is_misplaced(string directory, string name)
    {
        using var graph = new Graph();
        var path = graph.ProjectAt(directory, name);

        ApiReferencePayloadGateTests.MisplacedProjects(graph.Root).Should().Equal(path);
    }

    #endregion

    #region Packable projects hiding outside src

    [Fact]
    public void A_project_outside_src_that_does_not_opt_out_of_packing_is_stray()
    {
        using var graph = new Graph();
        graph.ProjectAt("samples/App", "App");

        ApiReferencePayloadGateTests.StrayProjects(graph.Root).Should().Equal(Path.Combine("samples", "App", "App.csproj"));
    }

    [Fact]
    public void A_project_outside_src_that_sets_IsPackable_false_is_not_stray()
    {
        using var graph = new Graph();
        graph.ProjectAt("samples/App", "App", NotPackable);

        ApiReferencePayloadGateTests.StrayProjects(graph.Root).Should().BeEmpty();
    }

    [Theory]
    [InlineData("False")]
    [InlineData("FALSE")]
    [InlineData(" false ")]
    public void The_opt_out_of_a_project_outside_src_ignores_case_and_surrounding_whitespace(string value)
    {
        using var graph = new Graph();
        graph.ProjectAt("samples/App", "App", $"<IsPackable>{value}</IsPackable>");

        ApiReferencePayloadGateTests.StrayProjects(graph.Root).Should().BeEmpty();
    }

    [Fact]
    public void A_test_project_outside_src_is_not_stray()
    {
        using var graph = new Graph();
        graph.ProjectAt("Trellis.Feature/tests", "Trellis.Feature.Tests");

        ApiReferencePayloadGateTests.StrayProjects(graph.Root).Should().BeEmpty();
    }

    [Theory]
    [InlineData("samples", "samples/App")]
    [InlineData("", "samples/App")]
    public void A_project_inherits_the_opt_out_from_a_Directory_Build_props_above_it(string propsDirectory, string projectDirectory)
    {
        using var graph = new Graph();
        graph.DirectoryBuildProps(propsDirectory, NotPackable);
        graph.ProjectAt(projectDirectory, "App");

        ApiReferencePayloadGateTests.StrayProjects(graph.Root).Should().BeEmpty();
    }

    [Fact]
    public void A_Directory_Build_props_in_a_sibling_folder_does_not_apply()
    {
        using var graph = new Graph();
        graph.DirectoryBuildProps("samples", NotPackable);
        graph.ProjectAt("tools/Generator", "Generator");

        ApiReferencePayloadGateTests.StrayProjects(graph.Root).Should().Equal(Path.Combine("tools", "Generator", "Generator.csproj"));
    }

    [Fact]
    public void A_Directory_Build_props_above_the_repository_root_does_not_apply()
    {
        using var enclosing = new Graph();
        enclosing.DirectoryBuildProps(string.Empty, NotPackable);
        using var graph = new Graph(enclosing.Root);
        graph.ProjectAt("samples/App", "App");

        ApiReferencePayloadGateTests.StrayProjects(graph.Root).Should().Equal(Path.Combine("samples", "App", "App.csproj"));
    }

    [Fact]
    public void A_Directory_Build_props_that_does_not_opt_out_leaves_the_project_stray()
    {
        using var graph = new Graph();
        graph.DirectoryBuildProps("samples", "<LangVersion>latest</LangVersion>");
        graph.ProjectAt("samples/App", "App");

        ApiReferencePayloadGateTests.StrayProjects(graph.Root).Should().Equal(Path.Combine("samples", "App", "App.csproj"));
    }

    [Fact]
    public void Projects_under_bin_or_obj_are_never_stray()
    {
        using var graph = new Graph();
        graph.Package("Trellis.Core", Ships);
        graph.ProjectAt("samples/App/bin", "App");
        graph.ProjectAt("samples/App/obj", "App2");

        ApiReferencePayloadGateTests.StrayProjects(graph.Root).Should().BeEmpty();
    }

    #endregion

    private static string DeliveredByCore(string referenceName) =>
        $"<TrellisApiReferenceDeliveredByCore>true</TrellisApiReferenceDeliveredByCore><TrellisApiRefName>{referenceName}</TrellisApiRefName>";

    /// <summary>A throwaway repository root holding minimal csproj files.</summary>
    private sealed class Graph : IDisposable
    {
        public Graph(string? parent = null)
        {
            Root = Path.Combine(parent ?? Path.GetTempPath(), $"payload-gate-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public readonly record struct Ref(string Target, string Attributes = "");

        /// <summary>The path a package named <paramref name="name"/> has at the conventional location.</summary>
        public string PathOf(string name) => Path.Combine(Root, name, "src", name + ".csproj");

        /// <summary>Writes a package at <c>&lt;name&gt;/src/&lt;name&gt;.csproj</c> and returns its path.</summary>
        public string Package(string name, string properties = "", params Ref[] references) =>
            ProjectAt($"{name}/src", name, properties, references);

        public string ProjectAt(string relativeDirectory, string name, string properties = "", params Ref[] references)
        {
            var directory = Path.Combine(Root, relativeDirectory);
            Directory.CreateDirectory(directory);
            var path = Path.GetFullPath(Path.Combine(directory, name + ".csproj"));
            var items = string.Concat(references.Select(reference =>
                $"<ProjectReference Include=\"{Path.GetRelativePath(directory, reference.Target)}\" {reference.Attributes} />"));
            File.WriteAllText(
                path,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>{properties}</PropertyGroup><ItemGroup>{items}</ItemGroup></Project>",
                new UTF8Encoding(false));
            return path;
        }

        /// <summary>Writes a <c>Directory.Build.props</c> that uses the legacy MSBuild namespace, as the repository file does.</summary>
        public void DirectoryBuildProps(string relativeDirectory, string properties)
        {
            var directory = Path.Combine(Root, relativeDirectory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "Directory.Build.props"),
                $"<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><PropertyGroup>{properties}</PropertyGroup></Project>",
                new UTF8Encoding(false));
        }

        public void ReferenceFile(string name)
        {
            var directory = Path.Combine(Root, "docs", "docfx_project", "api_reference");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, $"trellis-api-{name}.md"), "# reference", new UTF8Encoding(false));
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

namespace Trellis.Core.Tests.Packaging;

using System.Xml.Linq;

public sealed class OpenApiDependencyBoundaryTests
{
    [Theory]
    [InlineData("Trellis.Asp")]
    [InlineData("Trellis.ServiceDefaults")]
    public void Package_ProjectReferenceGraph_DoesNotDependOnNativeOpenApi(string package)
    {
        var project = Path.Combine(RepositoryRoot(), package, "src", package + ".csproj");
        var dependencies = PackageReferences(project, new HashSet<string>(StringComparer.OrdinalIgnoreCase)).ToArray();

        dependencies.Should().NotContain(name =>
            name == "Microsoft.AspNetCore.OpenApi" || name == "Microsoft.OpenApi",
            "native OpenAPI dependencies belong only to the optional Trellis.Asp.OpenApi adapter");
    }

    private static IEnumerable<string> PackageReferences(string project, HashSet<string> visited)
    {
        project = Path.GetFullPath(project);
        if (!visited.Add(project))
            yield break;

        var document = XDocument.Load(project);
        foreach (var reference in document.Descendants()
                     .Where(element => element.Name.LocalName == "PackageReference"))
            yield return reference.Attribute("Include")!.Value;

        foreach (var reference in document.Descendants()
                     .Where(element => element.Name.LocalName == "ProjectReference"))
        {
            var dependency = Path.Combine(Path.GetDirectoryName(project)!,
                reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar));
            foreach (var name in PackageReferences(dependency, visited))
                yield return name;
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Trellis.slnx")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the test must locate the repository's project graph");
        return directory!.FullName;
    }
}

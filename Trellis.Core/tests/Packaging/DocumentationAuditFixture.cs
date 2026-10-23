namespace Trellis.Core.Tests.Packaging;

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Trellis.Docs.Audit;

internal sealed class DocumentationAuditFixture : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"Trellis doc audit {Guid.NewGuid():N}");
    private readonly string _docsDirectory;

    public DocumentationAuditFixture()
    {
        _docsDirectory = Path.Combine(_directory, "docs", "docfx_project", "api_reference");
        Directory.CreateDirectory(Path.Combine(_docsDirectory, "audit-completeness"));
        WriteLedger("ambiguous-members.txt", "");
    }

    public void WritePackage(string name, string source, string documentation, params string[] packageReferences)
    {
        var projectDirectory = Path.Combine(_directory, name, "src");
        WriteBuild(name, source, "Release", packageReferences);
        new XDocument(new XElement("Project", new XElement("PropertyGroup",
            new XElement("TrellisApiRefName", name.ToLowerInvariant()))))
            .Save(Path.Combine(projectDirectory, name + ".csproj"));

        WriteDocument($"trellis-api-{name.ToLowerInvariant()}.md", documentation);
    }

    public void WriteBuild(string name, string source, string configuration, params string[] packageReferences)
    {
        var path = AssemblyPath(name, configuration);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var references = packageReferences
            .Select(package => MetadataReference.CreateFromFile(AssemblyPath(package, "Release")))
            .Prepend(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        var compilation = CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = File.Create(path);
        var result = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.Diagnostics));
    }

    public void MoveBuildToPackage(string name, string configuration, string destinationPackage) =>
        File.Move(AssemblyPath(name, configuration),
            Path.Combine(Path.GetDirectoryName(AssemblyPath(destinationPackage, "Release"))!, name + ".dll"));

    public void WriteDocument(string name, string documentation) =>
        File.WriteAllText(Path.Combine(_docsDirectory, name), documentation, new UTF8Encoding(true));

    public void WriteLedger(string name, string contents) =>
        File.WriteAllText(Path.Combine(_docsDirectory, "audit-completeness", name), contents, new UTF8Encoding(true));

    public DocMemberAudit.Catalog CreateMemberCatalog(string name)
    {
        var path = AssemblyPath(name, "Release");
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var resolver = new PathAssemblyResolver(Directory.GetFiles(runtimeDirectory, "*.dll").Append(path));
        using var context = new MetadataLoadContext(resolver);
        return DocMemberAudit.CreateCatalog([path], context);
    }

    public (int ExitCode, string Output) RunMemberAudit(DocMemberAudit.Catalog catalog)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var exitCode = DocMemberAudit.Run(_docsDirectory, catalog, output);
        return (exitCode, output.ToString());
    }

    public async Task<(int ExitCode, string Output)> RunAsync()
    {
        var testAssembly = typeof(DocumentationAuditFixture).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[]
        {
            "exec",
            "--runtimeconfig", Path.ChangeExtension(testAssembly, "runtimeconfig.json"),
            "--depsfile", Path.ChangeExtension(testAssembly, "deps.json"),
            typeof(ApiDocumentationEvidence).Assembly.Location,
        })
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["TRELLIS_FW_ROOT"] = _directory;
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the completeness audit.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await stdout + await stderr);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string AssemblyPath(string name, string configuration) =>
        Path.Combine(_directory, name, "src", "bin", configuration, "net10.0", name + ".dll");
}
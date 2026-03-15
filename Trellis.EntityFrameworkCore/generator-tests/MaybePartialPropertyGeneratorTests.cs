namespace Trellis.EntityFrameworkCore.Generator.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
/// Tests for <see cref="MaybePartialPropertyGenerator"/> to verify it correctly handles
/// init accessors on partial Maybe&lt;T&gt; properties.
/// </summary>
public class MaybePartialPropertyGeneratorTests
{
    /// <summary>
    /// Verifies that a Maybe&lt;T&gt; property declared with { get; init; } produces
    /// a generated implementation using an init accessor, not a set accessor.
    /// </summary>
    [Fact]
    public void Init_Accessor_Should_Produce_Init_In_Generated_Code()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class Customer
            {
                public int Id { get; set; }
                public partial Maybe<string> NickName { get; init; }
            }
            """;

        var (generatedSources, diagnostics) = RunGenerator(source, cancellationToken);

        // The generated code must compile — CS9250 would mean init/set mismatch
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generated partial property implementation must match the init accessor");

        // Verify the generated code contains 'init =>' not 'set =>'
        generatedSources.Should().Contain(s => s.Contains("init =>"),
            "the generator should emit 'init' accessor when the property declaration uses 'init'");
    }

    /// <summary>
    /// Regression: { get; set; } should continue to work as before.
    /// </summary>
    [Fact]
    public void Set_Accessor_Should_Produce_Set_In_Generated_Code()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class Customer
            {
                public int Id { get; set; }
                public partial Maybe<string> NickName { get; set; }
            }
            """;

        var (generatedSources, diagnostics) = RunGenerator(source, cancellationToken);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generated code with set accessor should compile");

        generatedSources.Should().Contain(s => s.Contains("set =>"),
            "the generator should emit 'set' accessor when the property declaration uses 'set'");
    }

    private static (List<string> Sources, IReadOnlyList<Diagnostic> Diagnostics) RunGenerator(
        string source, CancellationToken cancellationToken)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var references = GetMetadataReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: "MaybeGeneratorTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new MaybePartialPropertyGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics,
            cancellationToken);

        var allDiagnostics = generatorDiagnostics
            .Concat(outputCompilation.GetDiagnostics(cancellationToken))
            .ToList();

        var sources = driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString())
            .ToList();

        return (sources, allDiagnostics);
    }

    private static MetadataReference[] GetMetadataReferences()
    {
        var references = new List<MetadataReference>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        // Ensure Trellis.Results is included (contains Maybe<T> in namespace Trellis)
        var trellisLocation = typeof(Maybe<>).Assembly.Location;
        if (!references.Any(r => r.Display?.Equals(trellisLocation, StringComparison.OrdinalIgnoreCase) == true))
            references.Add(MetadataReference.CreateFromFile(trellisLocation));

        return references.ToArray();
    }
}

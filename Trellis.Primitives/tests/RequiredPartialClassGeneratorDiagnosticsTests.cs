namespace Trellis.Primitives.Tests;

using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceGenerator;

/// <summary>
/// Tests for source-generator diagnostics emitted by <see cref="RequiredPartialClassGenerator"/>.
/// </summary>
public class RequiredPartialClassGeneratorDiagnosticsTests
{
    [Fact]
    public void InvalidStringLengthRange_Reports_TRLSGEN002()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            [StringLength(5, MinimumLength = 10)]
            public partial class ImpossibleName : RequiredString<ImpossibleName>
            {
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var references = GetMetadataReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorDiagnosticTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RequiredPartialClassGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDriverDiagnostics,
            cancellationToken);

        var diagnostics = generatorDriverDiagnostics
            .Concat(outputCompilation.GetDiagnostics(cancellationToken))
            .ToArray();

        diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "TRLSGEN002");

        var diagnostic = diagnostics.Single(d => d.Id == "TRLSGEN002");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("ImpossibleName");
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("StringLength(5, MinimumLength = 10)");
    }

    [Fact]
    public void SameClassNameInDifferentNamespaces_DoesNotCollideGeneratedHintNames()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace Sales
            {
                public partial class CustomerId : RequiredGuid<CustomerId>
                {
                }
            }

            namespace Support
            {
                public partial class CustomerId : RequiredGuid<CustomerId>
                {
                }
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var references = GetMetadataReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorNamespaceCollisionTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RequiredPartialClassGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDriverDiagnostics,
            cancellationToken);

        var diagnostics = generatorDriverDiagnostics
            .Concat(outputCompilation.GetDiagnostics(cancellationToken))
            .ToArray();

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generator should support identical type names in different namespaces without hint-name collisions");

        var generatedSources = driver.GetRunResult().Results
            .SelectMany(static result => result.GeneratedSources)
            .Select(static generated => generated.HintName)
            .ToArray();

        generatedSources.Should().HaveCount(2);
        generatedSources.Should().OnlyHaveUniqueItems();
    }

    private static MetadataReference[] GetMetadataReferences() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static location => MetadataReference.CreateFromFile(location))
            .ToArray();
}
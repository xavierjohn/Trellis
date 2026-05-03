namespace Trellis.Primitives.Tests;

using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceGenerator;

/// <summary>
/// Tests for source-generator diagnostics emitted by <see cref="RequiredPartialClassGenerator"/>.
/// </summary>
public class RequiredPartialClassGeneratorDiagnosticsTests
{
    [Fact]
    public void InvalidStringLengthRange_Reports_TRLS032()
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

        diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "TRLS032");

        var diagnostic = diagnostics.Single(d => d.Id == "TRLS032");
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

    [Fact]
    public void NestedValueObjectInStaticClass_PreservesContainingTypeModifiers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public static partial class ValueObjects
            {
                public partial class Code : RequiredString<Code>
                {
                }
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generated partial declaration must match the static containing type");
    }

    [Fact]
    public void NestedValueObjectInSealedClass_PreservesContainingTypeModifiers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public sealed partial class Container
            {
                public partial class Code : RequiredString<Code>
                {
                }
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generated partial declaration must match the sealed containing type");
    }

    [Fact]
    public void GlobalNamespaceValueObject_GeneratesWithoutInvalidNamespaceDeclaration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            public partial class GlobalCode : RequiredString<GlobalCode>
            {
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generator must omit the namespace declaration for global-namespace value objects");
    }

    private static MetadataReference[] GetMetadataReferences() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static location => MetadataReference.CreateFromFile(location))
            .ToArray();

    private static Diagnostic[] RunGeneratorAndGetDiagnostics(string source, CancellationToken cancellationToken)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorNestingTests",
            syntaxTrees: [syntaxTree],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RequiredPartialClassGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDriverDiagnostics,
            cancellationToken);

        return generatorDriverDiagnostics
            .Concat(outputCompilation.GetDiagnostics(cancellationToken))
            .ToArray();
    }

    [Fact]
    public void InvalidIntRange_Reports_TRLS033()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            [Range(100, 1)]
            public partial class BadQuantity : RequiredInt<BadQuantity>
            {
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorDiagnosticTests",
            syntaxTrees: [syntaxTree],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RequiredPartialClassGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out var generatorDriverDiagnostics, cancellationToken);

        var diagnostics = generatorDriverDiagnostics
            .Concat(outputCompilation.GetDiagnostics(cancellationToken))
            .ToArray();

        diagnostics.Should().ContainSingle(d => d.Id == "TRLS033");
        var diagnostic = diagnostics.Single(d => d.Id == "TRLS033");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("BadQuantity");
    }

    [Fact]
    public void InvalidDecimalRange_Reports_TRLS033()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            [Range(999, 1)]
            public partial class BadPrice : RequiredDecimal<BadPrice>
            {
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorDiagnosticTests",
            syntaxTrees: [syntaxTree],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RequiredPartialClassGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out var generatorDriverDiagnostics, cancellationToken);

        var diagnostics = generatorDriverDiagnostics
            .Concat(outputCompilation.GetDiagnostics(cancellationToken))
            .ToArray();

        diagnostics.Should().ContainSingle(d => d.Id == "TRLS033");
        var diagnostic = diagnostics.Single(d => d.Id == "TRLS033");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("BadPrice");
    }

    [Fact]
    public void InvalidLongRange_Reports_TRLS033()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            [Range(1000, 1)]
            public partial class BadSequence : RequiredLong<BadSequence>
            {
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorDiagnosticTests",
            syntaxTrees: [syntaxTree],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RequiredPartialClassGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out var generatorDriverDiagnostics, cancellationToken);

        var diagnostics = generatorDriverDiagnostics
            .Concat(outputCompilation.GetDiagnostics(cancellationToken))
            .ToArray();

        diagnostics.Should().ContainSingle(d => d.Id == "TRLS033");
        var diagnostic = diagnostics.Single(d => d.Id == "TRLS033");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("BadSequence");
    }

    [Fact]
    public void DecimalRangeExceedsDecimalRange_Reports_TRLS034()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            [Range(0.0, 1e30)]
            public partial class TooBigPrice : RequiredDecimal<TooBigPrice>
            {
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorDiagnosticTests",
            syntaxTrees: [syntaxTree],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RequiredPartialClassGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out var generatorDriverDiagnostics, cancellationToken);

        var diagnostics = generatorDriverDiagnostics
            .Concat(outputCompilation.GetDiagnostics(cancellationToken))
            .ToArray();

        diagnostics.Should().ContainSingle(d => d.Id == "TRLS034");
        var diagnostic = diagnostics.Single(d => d.Id == "TRLS034");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("TooBigPrice");
    }
}
